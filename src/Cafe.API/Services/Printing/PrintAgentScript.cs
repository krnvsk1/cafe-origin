namespace Cafe.API.Services.Printing
{
    /// <summary>
    /// Программа печати для кассового ПК на Windows — скрипт PowerShell без установки.
    /// Ждёт задания на сервере и отправляет их байтами (RAW) на принтер Windows, например RONGTA «Printer bar».
    /// При первом запуске сама добавляется в автозагрузку.
    /// </summary>
    public static class PrintAgentScript
    {
        public static string Build(string baseUrl, string token) => Template
            .Replace("__BASE__", baseUrl.Replace("'", "''"))
            .Replace("__TOKEN__", token.Replace("'", "''"));

        private const string Template = """
# Программа печати счетов для гостей (Cafe) -> чековый принтер на этом ПК.
# Запуск: правой кнопкой -> «Выполнить с помощью PowerShell». Окно можно свернуть, но не закрывать.
# При первом запуске программа добавляется в автозагрузку Windows.
$Base  = '__BASE__'
$Token = '__TOKEN__'

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$Host.UI.RawUI.WindowTitle = 'Cafe: печать счетов'

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
'@

# Автозагрузка: ярлык в папке «Автозагрузка» текущего пользователя
try {
    $startup = [Environment]::GetFolderPath('Startup')
    $lnk = Join-Path $startup 'Cafe - печать счетов.lnk'
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
Write-Host "Печать счетов: сервер $Base. Окно не закрывайте."
$lastError = ''
while ($true) {
    try {
        $r = Invoke-RestMethod -Uri "$Base/api/print-agent/jobs?wait=20" -Headers $headers -TimeoutSec 40
        if ($lastError) { Write-Host "$(Get-Date -Format HH:mm:ss) Связь с сервером есть"; $lastError = '' }
        foreach ($j in $r.jobs) {
            try {
                [CafeRawPrinter]::Send($r.printer, [string]$j.title, [Convert]::FromBase64String($j.payload))
                $res = @{ ok = $true }
                Write-Host "$(Get-Date -Format HH:mm:ss) Напечатано: $($j.title)"
            } catch {
                $res = @{ ok = $false; error = $_.Exception.Message }
                Write-Host "$(Get-Date -Format HH:mm:ss) Ошибка печати: $($_.Exception.Message)"
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
