# N-27: the SRS App in the DESKTOP client of the MAS App, driven by UI Automation (as a person would): open the App, pick a ready-made damaged recording,
# accept the table of damaged sections, choose a network, wait for Play again. Screenshots of the client's windows to Downloads (srs-desktop-<n>.png).
# powershell -File desktop_srs.ps1 -Service https://localhost:5115/ -Network LJ-00min
param([string]$Service = "https://localhost:5115/", [string]$Network = "LJ-00min", [string]$Recording = "LJ016-0277")
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms
Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class Win32Print { [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags); }'
$exe = "D:\AI\AIF\UserAgent\Clients\Desktop\src\bin\Debug\net10.0-windows10.0.19041.0\RcaApp.exe"
$out = "C:\Users\Thor\Downloads"
$env:MPAI_MAS_SERVER = $Service
$p = Start-Process $exe -PassThru
$root = [System.Windows.Automation.AutomationElement]::RootElement
$T = [System.Windows.Automation.TreeScope]

function Say($m) { Write-Output ("{0:HH:mm:ss} {1}" -f (Get-Date), $m) }
function WaitFor([scriptblock]$find, [int]$seconds, [string]$what) {
  $end = (Get-Date).AddSeconds($seconds)
  while ((Get-Date) -lt $end) { $e = & $find; if ($e) { return $e }; Start-Sleep -Milliseconds 500 }
  throw "timeout waiting for $what"
}
function Windows() {
  $c = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $p.Id)
  $wc = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Window)
  foreach ($w in $root.FindAll($T::Children, $c)) { $w; foreach ($o in $w.FindAll($T::Children, $wc)) { $o } }   # a window it owns is its child
}
function ByName($el, $name) {
  $c = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $name)
  $el.FindFirst($T::Descendants, $c)
}
function Invoke($el) { $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
function Shot($el, $n) {
  $r = $el.Current.BoundingRectangle
  $bmp = New-Object System.Drawing.Bitmap ([int]$r.Width), ([int]$r.Height)
  # the window draws itself (PrintWindow, full content): a window under others, or a screen that is off at night, still gives its picture
  $g = [System.Drawing.Graphics]::FromImage($bmp); $hdc = $g.GetHdc()
  [void][Win32Print]::PrintWindow([IntPtr]$el.Current.NativeWindowHandle, $hdc, 2)
  $g.ReleaseHdc($hdc); $g.Dispose()
  $bmp.Save("$out\srs-desktop-$n.png", [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
  Say "screenshot srs-desktop-$n.png"
}

try {
  $main = WaitFor { Windows | Where-Object { $_.Current.Name -match "MPAI" } | Select-Object -First 1 } 60 "the main window"
  Say "main window: $($main.Current.Name)"
  # the list holds only the Apps it shows: it is scrolled to its end, where the App is
  $listCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::List)
  $list = WaitFor { $main.FindFirst($T::Descendants, $listCond) } 60 "the App list"
  $item = WaitFor { try { $list.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern).SetScrollPercent(-1, 100) } catch { }; ByName $main "Speech Restoration System" } 120 "the App in the list"
  $sel = $item
  while ($sel -and -not ($sel.GetSupportedPatterns() | Where-Object { $_.ProgrammaticName -match "SelectionItem" })) { $sel = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($sel) }
  $sel.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
  Start-Sleep 1
  # chosen while the launcher waits for a choice, the App starts at once; else its detail has an Open button
  $open = $null; try { $open = WaitFor { $b = ByName $main "Open"; if ($b -and -not $b.Current.IsOffscreen) { $b } } 5 "Open" } catch { }
  if ($open) { Invoke $open }
  Say "opened the App"
  $examples = WaitFor { Windows | Where-Object { $_.Current.Name -eq "Examples" } | Select-Object -First 1 } 180 "the Examples window"
  Shot $main 1-explains
  Shot $examples 2-recordings
  Invoke (WaitFor { ByName $examples $Recording } 10 $Recording)
  Say "picked $Recording"
  $table = WaitFor { Windows | Where-Object { $_.Current.Name -eq "Damaged sections" } | Select-Object -First 1 } 120 "the table window"
  Start-Sleep 3
  Shot $table 3-table
  # what the table holds, read from its text boxes (from, to, words for each section)
  $editCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)
  $values = foreach ($e in $table.FindAll($T::Descendants, $editCond)) { $e.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value }
  Say ("table: " + ($values -join " | "))
  Invoke (ByName $table "OK")
  Say "table accepted"
  $nets = WaitFor { $w = Windows | Where-Object { $_.Current.Name -eq "Examples" } | Select-Object -First 1; if ($w -and (ByName $w $Network)) { $w } } 120 "the networks"
  Shot $nets 4-networks
  Invoke (ByName $nets $Network)
  Say "chose $Network"
  $play = WaitFor { $b = ByName $main "Play again"; if ($b -and -not $b.Current.IsOffscreen) { $b } } 180 "Play again"
  Start-Sleep 2
  Shot $main 5-restored
  Say "restored; Play again shown"
  $instr = ByName $main "Here is the recording, restored. Play it again, or go round once more and choose another network to compare."
  Say ("the closing prompt shown: " + [bool]$instr)
}
finally {
  if (-not $p.HasExited) { $p.CloseMainWindow() | Out-Null; Start-Sleep 3; if (-not $p.HasExited) { Say "the client did not close by itself; left running, PID $($p.Id)" } else { Say "client closed" } }
}
