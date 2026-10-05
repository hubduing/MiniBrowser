# Открывает меню «☰» в MiniBrowser и снимает popup-окно меню через PrintWindow.
param([string]$OutPng = (Join-Path $PSScriptRoot '..\screenshot_menu.png'))

Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class MT
{
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
    [DllImport("user32.dll")] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint f);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, UIntPtr e);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, Rt, B; }
}
'@

$proc = Get-Process MiniBrowser -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $proc) {
    $exe = Join-Path $PSScriptRoot '..\src\MiniBrowser\bin\Release\net8.0-windows\win-x64\MiniBrowser.exe'
    Start-Process $exe | Out-Null
    Start-Sleep -Seconds 7
    $proc = Get-Process MiniBrowser | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
}
if (-not $proc) { throw 'no window' }

$h = $proc.MainWindowHandle
$myPid = [uint32]$proc.Id

[MT]::ShowWindow($h, 9) | Out-Null
[MT]::BringWindowToTop($h) | Out-Null
[MT]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 400

$r = New-Object MT+RECT
[MT]::GetWindowRect($h, [ref]$r) | Out-Null
Write-Host "window: $($r.L),$($r.T) $($r.Rt - $r.L)x$($r.B - $r.T)"

# Клик по кнопке «☰» (справа от адресной строки)
[MT]::SetCursorPos($r.L + 1174, $r.T + 51) | Out-Null
Start-Sleep -Milliseconds 250
[MT]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
[MT]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 600

# Собираем все окна нашего процесса
$windows = [System.Collections.Generic.List[object]]::new()
$callback = [MT+EnumProc]{
    param($x, $l)
    $q = [uint32]0
    [MT]::GetWindowThreadProcessId($x, [ref]$q) | Out-Null
    if ($q -eq $script:myPid) {
        $sb = [System.Text.StringBuilder]::new(256)
        [MT]::GetClassName($x, $sb, 256) | Out-Null
        $script:windows.Add([pscustomobject]@{
            Hwnd = $x; Class = $sb.ToString(); Visible = [MT]::IsWindowVisible($x)
        })
    }
    return $true
}
[MT]::EnumWindows($callback, [IntPtr]::Zero) | Out-Null

foreach ($w in $windows) {
    $wr = New-Object MT+RECT
    [MT]::GetWindowRect($w.Hwnd, [ref]$wr) | Out-Null
    Write-Host ("win: class='{0}' vis={1} rect={2},{3} {4}x{5}" -f $w.Class, $w.Visible, $wr.L, $wr.T, ($wr.Rt - $wr.L), ($wr.B - $wr.T))
}

# Печатаем каждое видимое окно кроме главного (popup меню)
$idx = 0
foreach ($w in $windows) {
    if (-not $w.Visible -or $w.Hwnd -eq $h) { continue }
    $idx++
    $wr = New-Object MT+RECT
    [MT]::GetWindowRect($w.Hwnd, [ref]$wr) | Out-Null
    $wd = [Math]::Max(1, $wr.Rt - $wr.L); $ht = [Math]::Max(1, $wr.B - $wr.T)
    if ($wd -lt 8 -or $ht -lt 8) { continue }
    $bmp = [System.Drawing.Bitmap]::new($wd, $ht)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $g.GetHdc()
    [MT]::PrintWindow($w.Hwnd, $hdc, 2) | Out-Null
    $g.ReleaseHdc($hdc); $g.Dispose()
    $file = [System.IO.Path]::Combine([System.IO.Path]::GetDirectoryName((Resolve-Path $OutPng).Path), "menu_popup_$idx.png")
    $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "SAVED: $file ($wd x $ht, class=$($w.Class))"
}
