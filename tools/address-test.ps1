# Проверяет адресную строку: клик очищает поле и показывает подсказку,
# ввод + Enter уходит в Google, Escape восстанавливает URL.
# Значение поля читается через UI Automation, а не распознаётся на скриншоте.
param([string]$OutDir = (Join-Path $PSScriptRoot '..'))

# Без этого Write-Host выводит русский текст в CP866 и он нечитаем.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class Addr
{
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint f);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, UIntPtr e);
    [DllImport("user32.dll")] public static extern void keybd_event(byte k, byte s, uint f, UIntPtr e);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, Rt, B; }
}
'@

$script:Fail = 0
function Check([string]$what, [bool]$ok, [string]$detail) {
    if ($ok) { Write-Host "PASS  $what ($detail)" }
    else { Write-Host "FAIL  $what ($detail)"; $script:Fail++ }
}

function Shot([IntPtr]$h, [string]$name) {
    $r = New-Object Addr+RECT
    [Addr]::GetWindowRect($h, [ref]$r) | Out-Null
    $bmp = [System.Drawing.Bitmap]::new($r.Rt - $r.L, $r.B - $r.T)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $g.GetHdc()
    [Addr]::PrintWindow($h, $hdc, 2) | Out-Null
    $g.ReleaseHdc($hdc); $g.Dispose()
    $file = [System.IO.Path]::GetFullPath((Join-Path $OutDir $name))
    $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "SAVED: $file"
}

function Click([int]$x, [int]$y) {
    [Addr]::SetCursorPos($x, $y) | Out-Null
    Start-Sleep -Milliseconds 200
    [Addr]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
    [Addr]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 400
}

# Первый клик по окну из фонового процесса часто уходит на активацию окна,
# а не на само поле. Повторяем, пока поле не получит фокус.
function ClickAddress([IntPtr]$h, $r) {
    for ($i = 0; $i -lt 3; $i++) {
        Click ($r.L + 600) ($r.T + 89)
        if ([string]::IsNullOrEmpty((ReadBox))) { return }
    }
}

# Ввод идёт через KEYEVENTF_UNICODE, а не через keybd_event: раскладка в системе
# русская, и виртуальные коды печатали бы «weather» как «цуферук».
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class Uni {
    [StructLayout(LayoutKind.Sequential)] public struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Explicit, Size = 32)] public struct INPUTUNION {
        [FieldOffset(0)] public KEYBDINPUT ki;
        // union в native INPUT — это MOUSEINPUT (32 байта на x64). Без явного
        // размера union схлопывается до 24 байт, INPUT выходит 32 байт, и
        // SendInput возвращает 0, молча не печатая текст.
    }
    [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public INPUTUNION u; }
    [DllImport("user32.dll", SetLastError=true)] public static extern uint SendInput(uint n, INPUT[] p, int size);

    public static int Size { get { return Marshal.SizeOf(typeof(INPUT)); } }

    public static int Text(string s) {
        int size = Size;
        int sent = 0;
        foreach (char c in s) {
            var down = new INPUT { type = 1 };
            down.u.ki = new KEYBDINPUT { wScan = c, dwFlags = 0x0004 };
            var up = new INPUT { type = 1 };
            up.u.ki = new KEYBDINPUT { wScan = c, dwFlags = 0x0004 | 0x0002 };
            sent += (int)SendInput(1, new[]{ down }, size);
            sent += (int)SendInput(1, new[]{ up }, size);
            System.Threading.Thread.Sleep(20);
        }
        return sent;
    }
}
'@
Write-Host "INPUT size: $([Uni]::Size)"

function TypeText([string]$s) {
    $n = [Uni]::Text($s)
    if ($n -ne 2 * $s.Length) { Write-Host "WARN  SendInput delivered $n of $($s.Length * 2)" }
    Start-Sleep -Milliseconds 400
}

function Key([byte]$vk) {
    [Addr]::keybd_event($vk, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 40
    [Addr]::keybd_event($vk, 0, 2, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 600
}

$VK_SPACE = 0x20; $VK_RETURN = 0x0D; $VK_ESCAPE = 0x1B

Get-Process MiniBrowser -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 400
$exe = Join-Path $PSScriptRoot '..\publish\MiniBrowser.exe'
Start-Process (Resolve-Path $exe).Path | Out-Null

$proc = $null
for ($i = 0; $i -lt 40 -and -not $proc; $i++) {
    Start-Sleep -Milliseconds 500
    $proc = Get-Process MiniBrowser -ErrorAction SilentlyContinue |
            Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
}
if (-not $proc) { throw 'no window' }

$h = $proc.MainWindowHandle
[Addr]::ShowWindow($h, 9) | Out-Null
[Addr]::BringWindowToTop($h) | Out-Null
[Addr]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Seconds 4

# SetForegroundWindow из фонового процесса блокируется. ALT-нажатие — известный
# обход: система пропускает переключение фокуса, если текущий процесс «держит» ALT.
[Addr]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero)
[Addr]::SetForegroundWindow($h) | Out-Null
[Addr]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 400

if ([Addr]::GetForegroundWindow() -ne $h) {
    Write-Host 'WARN  окно не на переднем плане, ввод может не дойти'
}

# Адресная строка — единственный Edit в окне
$root = [System.Windows.Automation.AutomationElement]::FromHandle($h)
$cond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::Edit)
$box = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
if (-not $box) { throw 'address box not found' }

function ReadBox() {
    $vp = $box.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    return $vp.Current.Value
}

# Стартовая страница грузится асинхронно — без ожидания поле ещё пустое.
$startUrl = ''
for ($i = 0; $i -lt 40; $i++) {
    $startUrl = (ReadBox)
    if ($startUrl -like 'https://*') { break }
    Start-Sleep -Milliseconds 500
}

# Окно по умолчанию центрируется на втором мониторе и уезжает в отрицательные
# координаты — клики по адресной строке улетают за пределы экрана. Ставим в (100,100).
[Addr]::SetWindowPos($h, [IntPtr]::Zero, 100, 100, 1200, 800, 0x0004) | Out-Null
Start-Sleep -Milliseconds 500

$r = New-Object Addr+RECT
[Addr]::GetWindowRect($h, [ref]$r) | Out-Null
Write-Host "window: $($r.L),$($r.T) $($r.Rt - $r.L)x$($r.B - $r.T)"

Check 'поле показывает URL страницы при старте' ($startUrl -like 'https://*') $startUrl

# --- 1. Клик очищает поле ---
ClickAddress $h $r
$afterClick = (ReadBox)
Check 'клик очищает адресную строку' ([string]::IsNullOrEmpty($afterClick)) "[$afterClick]"
Shot $h 'test_1_focus_cleared.png'

# --- 2. Ввод работает (кириллица: проверяем и раскладку, и юникод) ---
TypeText 'погода в москве'
$typed = (ReadBox)
Check 'ввод попадает в поле' ($typed -eq 'погода в москве') "[$typed]"
Shot $h 'test_2_typed.png'

# --- 3. Escape восстанавливает адрес ---
Key $VK_ESCAPE
Start-Sleep -Milliseconds 600
$afterEsc = (ReadBox)
Check 'Escape восстанавливает URL' ($afterEsc -eq $startUrl) "[$afterEsc]"
Shot $h 'test_3_escape_restored.png'

# --- 4. Ввод + Enter уходит в Google ---
ClickAddress $h $r
Check 'повторный клик снова очищает поле' ([string]::IsNullOrEmpty((ReadBox))) ''
TypeText 'погода в москве'
Key $VK_RETURN
Start-Sleep -Seconds 6
$afterEnter = (ReadBox)
Check 'Enter открывает поиск Google' ($afterEnter -like '*google.com/search?q=*') "[$afterEnter]"
Shot $h 'test_4_google_result.png'

# --- 5. Пустой Enter ничего не открывает ---
$before = (ReadBox)
ClickAddress $h $r
Key $VK_RETURN
Start-Sleep -Seconds 2
Check 'пустой Enter не navigates и восстанавливает URL' ((ReadBox) -eq $before) "[$(ReadBox)]"

$proc | Stop-Process -Force
Write-Host ""
if ($script:Fail -eq 0) { Write-Host 'ALL PASS' } else { Write-Host "$($script:Fail) FAILED" }
exit $script:Fail