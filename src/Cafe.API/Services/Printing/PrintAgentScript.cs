namespace Cafe.API.Services.Printing
{
    /// <summary>
    /// Программа связи для кассового ПК на Windows — скрипт PowerShell без установки.
    /// Ждёт задания на сервере: счёт печатает на принтер (Windows-принтер или по IP), запрос к кассе АТОЛ
    /// или УТМ ЕГАИС выполняет у себя в сети кафе и возвращает ответ серверу. Сама добавляется в автозагрузку.
    /// </summary>
    public static class PrintAgentScript
    {
        public static string Build(string baseUrl, string token) => Template
            .Replace("__BASE__", baseUrl.Replace("'", "''"))
            .Replace("__TOKEN__", token.Replace("'", "''"));

        private const string Template = """
# Программа связи Cafe для кассового ПК: печать счетов на принтер и связь сервера с кассой АТОЛ и УТМ ЕГАИС.
# Запуск: правой кнопкой -> «Выполнить с помощью PowerShell». Окно можно свернуть, но не закрывать.
# При первом запуске программа добавляется в автозагрузку Windows.
# Касса «через драйвер ККТ 10»: настройка подключения к кассе — ярлык «Cafe — настройка кассы» на рабочем столе (или запуск с -AtolSetup).
param([switch]$AtolSetup)
$Base  = '__BASE__'
$Token = '__TOKEN__'

# Драйвер ККТ АТОЛ 10 часто 32-битный: его объект виден только из 32-битного PowerShell — перезапускаемся в нём
if ([Environment]::Is64BitProcess -and -not $env:CAFE_AGENT_32 -and $PSCommandPath) {
    $has64 = $true
    try { $probe = New-Object -ComObject AddIn.Fptr10; [void][Runtime.InteropServices.Marshal]::ReleaseComObject($probe) } catch { $has64 = $false }
    $ps32 = Join-Path $env:WINDIR 'SysWOW64\WindowsPowerShell\v1.0\powershell.exe'
    if (-not $has64 -and (Test-Path $ps32)) {
        $env:CAFE_AGENT_32 = '1'
        $args32 = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
        if ($AtolSetup) { $args32 += ' -AtolSetup' }
        Start-Process -FilePath $ps32 -ArgumentList $args32
        exit
    }
}

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$Host.UI.RawUI.WindowTitle = 'Cafe: связь с кассовым ПК'

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class CafeRawPrinter {
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public class DOCINFO {
        [MarshalAs(UnmanagedType.LPWStr)] public string pDocName;
        [MarshalAs(UnmanagedType.LPWStr)] public string pOutputFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string pDataType;
    }
    [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool OpenPrinter(string name, out IntPtr handle, IntPtr defaults);
    [DllImport("winspool.drv", SetLastError = true)] static extern bool ClosePrinter(IntPtr handle);
    [DllImport("winspool.drv", EntryPoint = "StartDocPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool StartDocPrinter(IntPtr handle, int level, [In, MarshalAs(UnmanagedType.LPStruct)] DOCINFO doc);
    [DllImport("winspool.drv", SetLastError = true)] static extern bool EndDocPrinter(IntPtr handle);
    [DllImport("winspool.drv", SetLastError = true)] static extern bool StartPagePrinter(IntPtr handle);
    [DllImport("winspool.drv", SetLastError = true)] static extern bool EndPagePrinter(IntPtr handle);
    [DllImport("winspool.drv", SetLastError = true)] static extern bool WritePrinter(IntPtr handle, byte[] data, int count, out int written);

    public static void Send(string printer, string title, byte[] data) {
        IntPtr h;
        if (!OpenPrinter(printer, out h, IntPtr.Zero))
            throw new Exception("Printer '" + printer + "' not found (Windows error " + Marshal.GetLastWin32Error() + ")");
        try {
            DOCINFO doc = new DOCINFO();
            doc.pDocName = title;
            doc.pDataType = "RAW";
            if (!StartDocPrinter(h, 1, doc)) throw new Exception("StartDocPrinter error " + Marshal.GetLastWin32Error());
            try {
                StartPagePrinter(h);
                int written;
                if (!WritePrinter(h, data, data.Length, out written)) throw new Exception("WritePrinter error " + Marshal.GetLastWin32Error());
                EndPagePrinter(h);
            } finally { EndDocPrinter(h); }
        } finally { ClosePrinter(h); }
    }
}

public static class CafeTcp {
    // Принтер в сети кафе (LAN): байты ESC/POS на порт 9100
    public static void Send(string host, int port, byte[] data) {
        using (var c = new System.Net.Sockets.TcpClient()) {
            var ar = c.BeginConnect(host, port, null, null);
            if (!ar.AsyncWaitHandle.WaitOne(5000)) throw new Exception("Printer " + host + ":" + port + " does not answer");
            c.EndConnect(ar);
            using (var st = c.GetStream()) { st.Write(data, 0, data.Length); st.Flush(); }
        }
    }
}

public class CafeHttpResult { public int Status; public string ContentType; public byte[] Body; }

public static class CafeHttp {
    // Запрос к устройству в сети кафе (АТОЛ Web Server, УТМ) — ответ с любым кодом возвращается как есть
    public static CafeHttpResult Send(string method, string url, string contentType, byte[] body) {
        var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
        req.Method = method;
        req.Timeout = 25000;
        req.ReadWriteTimeout = 25000;
        req.Proxy = null;
        req.ServicePoint.Expect100Continue = false;
        if (body != null && method != "GET" && method != "DELETE") {
            if (!string.IsNullOrEmpty(contentType)) req.ContentType = contentType;
            req.ContentLength = body.Length;
            using (var s = req.GetRequestStream()) s.Write(body, 0, body.Length);
        }
        System.Net.HttpWebResponse res;
        try { res = (System.Net.HttpWebResponse)req.GetResponse(); }
        catch (System.Net.WebException ex) {
            if (ex.Response == null) throw;
            res = (System.Net.HttpWebResponse)ex.Response;
        }
        using (res) using (var s = res.GetResponseStream()) using (var ms = new System.IO.MemoryStream()) {
            if (s != null) s.CopyTo(ms);
            return new CafeHttpResult { Status = (int)res.StatusCode, ContentType = res.ContentType, Body = ms.ToArray() };
        }
    }
}
'@

# ---------- Касса через драйвер ККТ 10 (без АТОЛ Web Server) ----------
# Программа сама выполняет задания кассы в том же формате JSON, что и АТОЛ Web Server, и хранит их итог
# в atol-results.json рядом с собой — чтобы после перезапуска сервер узнал судьбу чека и не пробил его второй раз.
$CafeDir = if ($PSCommandPath) { Split-Path $PSCommandPath } else { (Get-Location).Path }
$AtolSettingsFile = Join-Path $CafeDir 'atol-settings.json'
$AtolResultsFile = Join-Path $CafeDir 'atol-results.json'
$script:Fptr = $null
$script:AtolResults = New-Object System.Collections.Specialized.OrderedDictionary
if (Test-Path $AtolResultsFile) {
    try { (Get-Content $AtolResultsFile -Raw -Encoding UTF8 | ConvertFrom-Json).PSObject.Properties | ForEach-Object { $script:AtolResults[$_.Name] = [string]$_.Value } } catch { }
}

function Get-Fptr {
    if ($script:Fptr) { return $script:Fptr }
    try { $f = New-Object -ComObject AddIn.Fptr10 }
    catch { throw "Драйвер ККТ 10 не найден ($([IntPtr]::Size * 8)-битный PowerShell): $($_.Exception.Message)" }
    if ($null -eq $f.LIBFPTR_PARAM_JSON_DATA) { throw 'В этой версии драйвера ККТ нет JSON-заданий — обновите драйвер ККТ 10' }
    if (Test-Path $AtolSettingsFile) { [void]$f.setSettings((Get-Content $AtolSettingsFile -Raw -Encoding UTF8)) }
    $script:Fptr = $f
    return $f
}

function AtolItem([int]$code, [string]$desc, [string]$raw) {
    $json = 'null'
    if ($raw) { try { [void]($raw | ConvertFrom-Json); $json = $raw } catch { } }
    return '{"status":"ready","errorCode":' + $code + ',"errorDescription":' + (ConvertTo-Json $desc) + ',"result":' + $json + '}'
}

function Invoke-AtolTask([string]$taskJson) {
    $f = Get-Fptr
    if ($f.open() -ne 0) { return (AtolItem ([int]$f.errorCode()) ("Нет связи с кассой: " + $f.errorDescription()) '') }
    try {
        [void]$f.setParam($f.LIBFPTR_PARAM_JSON_DATA, $taskJson)
        [void]$f.processJson()
        return (AtolItem ([int]$f.errorCode()) ([string]$f.errorDescription()) ([string]$f.getParamString($f.LIBFPTR_PARAM_JSON_DATA)))
    } finally { [void]$f.close() }
}

function Save-AtolResults {
    while ($script:AtolResults.Count -gt 300) { $script:AtolResults.RemoveAt(0) }
    try { $script:AtolResults | ConvertTo-Json -Compress | Set-Content -Path $AtolResultsFile -Encoding UTF8 } catch { }
}

function AtolReply([int]$status, [string]$text) {
    $x = New-Object CafeHttpResult
    $x.Status = $status
    $x.ContentType = 'application/json; charset=utf-8'
    $x.Body = [Text.Encoding]::UTF8.GetBytes($text)
    return $x
}

# Те же адреса, что у АТОЛ Web Server: POST /api/v2/requests и GET /api/v2/requests/{uuid}
function Invoke-AtolDriver([string]$method, [string]$path, [byte[]]$body) {
    if ($method -eq 'POST' -and $path -eq '/api/v2/requests') {
        $req = [Text.Encoding]::UTF8.GetString($body) | ConvertFrom-Json
        $uuid = [string]$req.uuid
        if ($script:AtolResults.Contains($uuid)) { return (AtolReply 409 '{"error":{"description":"Задание уже выполнено"}}') }
        $items = @()
        foreach ($t in @($req.request)) { $items += (Invoke-AtolTask ($t | ConvertTo-Json -Depth 30 -Compress)) }
        $script:AtolResults[$uuid] = '{"results":[' + ($items -join ',') + ']}'
        Save-AtolResults
        return (AtolReply 201 '{}')
    }
    if ($method -eq 'GET' -and $path -match '^/api/v2/requests/([^/?]+)') {
        $u = [Uri]::UnescapeDataString($Matches[1])
        if ($script:AtolResults.Contains($u)) { return (AtolReply 200 ([string]$script:AtolResults[$u])) }
        return (AtolReply 404 '{"error":{"description":"Касса не знает такого задания"}}')
    }
    return (AtolReply 404 '{"error":{"description":"Неизвестный запрос к кассе"}}')
}

# Настройка подключения к кассе (окно драйвера): выберите ту же модель и порт, что у Interpos
if ($AtolSetup) {
    try {
        $f = Get-Fptr
        [void]$f.showProperties($f.LIBFPTR_GUI_PARENT_NATIVE, 0)
        $settings = [string]$f.getSettings()
        if ($settings) { Set-Content -Path $AtolSettingsFile -Value $settings -Encoding UTF8; Write-Host 'Настройки кассы сохранены.' }
    } catch { Write-Host "Не удалось открыть настройки драйвера ККТ: $($_.Exception.Message)" }
    Read-Host 'Нажмите Enter, чтобы закрыть'
    exit
}

# Ярлык «Cafe — настройка кассы» на рабочем столе
try {
    $desk = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Cafe - настройка кассы.lnk'
    if (-not (Test-Path $desk) -and $PSCommandPath) {
        $ps = Join-Path $env:WINDIR 'SysWOW64\WindowsPowerShell\v1.0\powershell.exe'
        if (-not (Test-Path $ps)) { $ps = Join-Path $PSHOME 'powershell.exe' }
        $sh = New-Object -ComObject WScript.Shell
        $l = $sh.CreateShortcut($desk)
        $l.TargetPath = $ps
        $l.Arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" -AtolSetup"
        $l.WorkingDirectory = Split-Path $PSCommandPath
        $l.Save()
    }
} catch { }

# Автозагрузка: ярлык в папке «Автозагрузка» текущего пользователя
try {
    $startup = [Environment]::GetFolderPath('Startup')
    $lnk = Join-Path $startup 'Cafe - связь с кассовым ПК.lnk'
    if (-not (Test-Path $lnk) -and $PSCommandPath) {
        $sh = New-Object -ComObject WScript.Shell
        $s = $sh.CreateShortcut($lnk)
        $s.TargetPath = (Join-Path $PSHOME 'powershell.exe')
        $s.Arguments = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Minimized -File `"$PSCommandPath`""
        $s.WorkingDirectory = Split-Path $PSCommandPath
        $s.Save()
        Write-Host 'Добавлено в автозагрузку Windows.'
    }
} catch { Write-Host "Не удалось добавить в автозагрузку: $($_.Exception.Message)" }

$headers = @{ 'X-Print-Token' = $Token; 'X-Print-Host' = $env:COMPUTERNAME }
Write-Host "Связь с сервером ${Base}: печать счетов, касса АТОЛ, УТМ. Окно не закрывайте."
$lastError = ''
while ($true) {
    try {
        $r = Invoke-RestMethod -Uri "$Base/api/print-agent/jobs?wait=20" -Headers $headers -TimeoutSec 40
        if ($lastError) { Write-Host "$(Get-Date -Format HH:mm:ss) Связь с сервером есть"; $lastError = '' }
        foreach ($j in $r.jobs) {
            try {
                if ($j.type -eq 'http') {
                    # Запрос сервера к кассе или УТМ в сети кафе
                    $q = $j.payload | ConvertFrom-Json
                    $body = $null
                    if ($q.body) { $body = [Convert]::FromBase64String($q.body) }
                    $atolBase = [string]$r.atolDriverBase
                    if ($atolBase -and $q.url.StartsWith($atolBase, [StringComparison]::OrdinalIgnoreCase)) {
                        # Касса через драйвер ККТ 10 — без АТОЛ Web Server
                        $resp = Invoke-AtolDriver $q.method $q.url.Substring($atolBase.Length) $body
                    } else {
                        $resp = [CafeHttp]::Send($q.method, $q.url, $q.contentType, $body)
                    }
                    $res = @{ ok = $true; status = $resp.Status; contentType = $resp.ContentType; body = [Convert]::ToBase64String($resp.Body) }
                } else {
                    $data = [Convert]::FromBase64String($j.payload)
                    if ($r.printerHost) { [CafeTcp]::Send($r.printerHost, [int]$r.printerPort, $data) }
                    else { [CafeRawPrinter]::Send($r.printer, [string]$j.title, $data) }
                    $res = @{ ok = $true }
                    Write-Host "$(Get-Date -Format HH:mm:ss) Напечатано: $($j.title)"
                }
            } catch {
                $res = @{ ok = $false; error = $_.Exception.Message }
                Write-Host "$(Get-Date -Format HH:mm:ss) Ошибка ($($j.title)): $($_.Exception.Message)"
            }
            $body = [Text.Encoding]::UTF8.GetBytes(($res | ConvertTo-Json -Compress))
            Invoke-RestMethod -Method Post -Uri "$Base/api/print-agent/jobs/$($j.id)/result" -Headers $headers -ContentType 'application/json; charset=utf-8' -Body $body | Out-Null
        }
    } catch {
        $msg = $_.Exception.Message
        if ($msg -ne $lastError) { Write-Host "$(Get-Date -Format HH:mm:ss) Нет связи с сервером: $msg"; $lastError = $msg }
        Start-Sleep -Seconds 5
    }
}
""";
    }
}
