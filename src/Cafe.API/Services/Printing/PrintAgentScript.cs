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
$Base  = '__BASE__'
$Token = '__TOKEN__'

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
                    $resp = [CafeHttp]::Send($q.method, $q.url, $q.contentType, $body)
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
