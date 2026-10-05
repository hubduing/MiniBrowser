# Проверка HTML5-полноэкранного режима: кликаем по кнопке requestFullscreen на
# локальной странице и смотрим, стал ли HWND окна размером с монитор и исчез ли chrome.
param([string]$OutPng = (Join-Path $PSScriptRoot '..\screenshot_fullscreen.png'))

<#
  ВНИМАНИЕ: скрипт кликает по кнопке по СЛЕПОЙ координате (кнопка нарисована
  страницей, её позиция не запрошена у DOM). На другой геометрии окна/шрифта
  клик промахнётся и скрипт напечатает FAIL, хотя фича работает.
  Надёжнее смотреть скриншот глазами либо кликнуть по кнопке вручную.
#>

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class FS {
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
    [DllImport("user32.dll")] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint f);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, UIntPtr e);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint f, UIntPtr e);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, Rt, B; }
    public struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr h, uint f);
    [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr m, ref MONITORINFO mi);
}
'@

# Страница с <video> и кнопкой, вызывающей requestFullscreen()
$pageDir = Join-Path $env:TEMP 'minibrowser-fs-test'
New-Item -ItemType Directory -Force -Path $pageDir | Out-Null
$page = Join-Path $pageDir 'index.html'
@'
<!doctype html><html><head><meta charset="utf-8">
<style>body{margin:0;background:#123;color:#eee;font:16px sans-serif}
button{font:18px sans-serif;padding:12px 24px;margin:40px}
video{width:480px;height:270px;background:#000}</style></head>
<body>
<button id="go" onclick="document.querySelector('video').requestFullscreen()">FULLSCREEN</button>
<video controls muted loop>
  <source src="data:video/mp4;base64,AAAAIGZ0eXBpc29tAAACAGlzb21pc28yYXZjMW1wNDEAAAAIZnJlZQAAAr1tZGF0AAACrgYF//+q3EXpvebZSLeWLNgg2SPu73gyNjQgLSBjb3JlIDE0OCByMzY0MyA1YzY1NyA0Mzk5LzIwMDIxIG1wNGYtY29kYy41LjEyMyAtZDU0MyA1MDU2NzYgZDU0MyAwMTAwIC0gSC4yNjQvTVBFRy00IEFWQy1jb2RlYyAtIENvcHlsZWZ0IDIwMDMtMjAyMQ==" type="video/mp4">
</video>
</body></html>
'@ | Set-Content -Path $page -Encoding UTF8

Get-Process MiniBrowser -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1

$exe = Join-Path $PSScriptRoot '..\src\MiniBrowser\bin\Debug\net8.0-windows\win-x64\MiniBrowser.exe'
if (-not (Test-Path $exe)) { $exe = Join-Path $PSScriptRoot '..\publish\MiniBrowser.exe' }
Start-Process $exe -ArgumentList ('"' + $page + '"') | Out-Null
Start-Sleep -Seconds 8

$proc = Get-Process MiniBrowser -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $proc) { throw 'окно MiniBrowser не найдено' }
$h = $proc.MainWindowHandle

function Get-Rect([IntPtr]$w) { $r = New-Object FS+RECT; [FS]::GetWindowRect($w, [ref]$r) | Out-Null; return $r }

$r = Get-Rect $h
$mon = [FS+MONITORINFO]::new(); $mon.cbSize = [System.Runtime.InteropServices.Marshal]::SizeOf($mon)
[FS]::GetMonitorInfo([FS]::MonitorFromWindow($h, 2), [ref]$mon) | Out-Null
$screenW = $mon.rcMonitor.Rt - $mon.rcMonitor.L
$screenH = $mon.rcMonitor.B - $mon.rcMonitor.T

Write-Host "окно ДО:      $($r.L),$($r.T) $($r.Rt - $r.L)x$($r.B - $r.T)"
Write-Host "монитор:      $($mon.rcMonitor.L),$($mon.rcMonitor.T) ${screenW}x${screenH}"

[FS]::ShowWindow($h, 9) | Out-Null
[FS]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 700

# Кликаем кнопку FULLSCREEN (страница открыта первой вкладкой)
[FS]::SetCursorPos($r.L + 90, $r.T + 130) | Out-Null
Start-Sleep -Milliseconds 300
[FS]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
[FS]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Seconds 2

$r2 = Get-Rect $h
Write-Host "окно ПОСЛЕ:   $($r2.L),$($r2.T) $($r2.Rt - $r2.L)x$($r2.B - $r2.T)"

$w2 = $r2.Rt - $r2.L; $h2 = $r2.B - $r2.T
$isFs = ($w2 -ge $screenW - 2) -and ($h2 -ge $screenH - 2)
Write-Host ""
if ($isFs) {
    Write-Host "PASS: окно развёрнуто на весь монитор -> HTML5 fullscreen обрабатывается"
} else {
    Write-Host "FAIL: окно НЕ на весь монитор -> chrome полноэкранный режим не сработал"
}

# Скриншот для глазами
[FS]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 300
$wd = [Math]::Max(1, $r2.Rt - $r2.L); $ht = [Math]::Max(1, $r2.B - $r2.T)
$bmp = [System.Drawing.Bitmap]::new($wd, $ht)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r2.L, $r2.T, 0, 0, $bmp.Size)
$g.Dispose()
$outFull = [System.IO.Path]::GetFullPath($OutPng)
$bmp.Save($outFull, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Host "SAVED: $outFull ($wd x $ht)"

# Esc должен вернуть окно из полноэкранного режима
[FS]::keybd_event(0x1B, 0, 0, [UIntPtr]::Zero)
[FS]::keybd_event(0x1B, 0, 2, [UIntPtr]::Zero)
Start-Sleep -Seconds 2
$r3 = Get-Rect $h
$w3 = $r3.Rt - $r3.L; $h3 = $r3.B - $r3.T
Write-Host "окно после Esc: $($r3.L),$($r3.T) ${w3}x${h3}"
if ($w3 -lt $screenW - 2) { Write-Host "PASS: Esc вернул окно в обычный режим" }
else { Write-Host "FAIL: Esc не вернул окно из полноэкранного режима" }
