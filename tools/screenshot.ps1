# Запускает MiniBrowser (publish\MiniBrowser.exe) и снимает главное окно через PrintWindow.
param(
    [string]$OutPng = (Join-Path $PSScriptRoot '..\screenshot.png'),
    [switch]$KeepOpen
)

Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class SShot
{
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint f);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, Rt, B; }
}
'@

Get-Process MiniBrowser -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 400

$exe = Join-Path $PSScriptRoot '..\publish\MiniBrowser.exe'
if (-not (Test-Path $exe)) { throw "not found: $exe" }
Start-Process (Resolve-Path $exe).Path | Out-Null

$proc = $null
for ($i = 0; $i -lt 30 -and -not $proc; $i++) {
    Start-Sleep -Milliseconds 500
    $proc = Get-Process MiniBrowser -ErrorAction SilentlyContinue |
            Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
}
if (-not $proc) { throw 'no window' }

$h = $proc.MainWindowHandle
[SShot]::ShowWindow($h, 9) | Out-Null
[SShot]::BringWindowToTop($h) | Out-Null
[SShot]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Seconds 3

$r = New-Object SShot+RECT
[SShot]::GetWindowRect($h, [ref]$r) | Out-Null
$w = $r.Rt - $r.L; $ht = $r.B - $r.T
Write-Host "window: $($r.L),$($r.T) ${w}x${ht}"

$bmp = [System.Drawing.Bitmap]::new($w, $ht)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
[SShot]::PrintWindow($h, $hdc, 2) | Out-Null
$g.ReleaseHdc($hdc); $g.Dispose()

$out = [System.IO.Path]::GetFullPath($OutPng)
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Host "SAVED: $out"

if (-not $KeepOpen) { $proc | Stop-Process -Force }