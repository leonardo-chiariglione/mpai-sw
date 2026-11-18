# N-36: the CUI-CPP App in the DESKTOP client of the MAS App, driven by UI Automation: open the App, choose a healthy and a distressed company, then twice the debts of
# the distressed one; the report is read from the texts of the window (window pictures come out blank when the screen is off at night).
param([string]$Service = "https://localhost:5115/")
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$exe = "D:\AI\AIF\UserAgent\Clients\Desktop\src\bin\Debug\net10.0-windows10.0.19041.0\RcaApp.exe"
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
  foreach ($w in $root.FindAll($T::Children, $c)) { $w; foreach ($o in $w.FindAll($T::Children, $wc)) { $o } }
}
function ByName($el, $name) { $el.FindFirst($T::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $name))) }
function ByType($el, $type) { $el.FindAll($T::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, $type))) }
function Invoke($el) { $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
function Texts($el) { (ByType $el ([System.Windows.Automation.ControlType]::Text) | ForEach-Object { $_.Current.Name } | Where-Object { $_ }) -join " | " }
function Choose($win, $name) {
  $combo = (ByType $win ([System.Windows.Automation.ControlType]::ComboBox))[0]
  $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand(); Start-Sleep -Milliseconds 400
  $item = WaitFor { ByName $combo $name } 5 $name
  $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
  $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()
  Invoke (ByName $win "Choose"); Say "chose $name"
}
function Report($win, $label) { Start-Sleep 8; Say ("$label report: " + ((Texts $win) -replace '\s+', ' ')) }
try {
  $main = WaitFor { Windows | Where-Object { $_.Current.Name -match "MPAI" } | Select-Object -First 1 } 60 "the main window"
  $listCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::List)
  $list = WaitFor { $main.FindFirst($T::Descendants, $listCond) } 180 "the App list"
  $item = WaitFor { try { $list.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern).SetScrollPercent(-1, 100) } catch { }; ByName $main "Company Performance Prediction" } 120 "the App"
  $sel = $item
  while ($sel -and -not ($sel.GetSupportedPatterns() | Where-Object { $_.ProgrammaticName -match "SelectionItem" })) { $sel = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($sel) }
  $sel.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
  Say "opened the App"
  $win = WaitFor { Windows | Where-Object { $_.Current.Name -eq "Company Performance Prediction" } | Select-Object -First 1 } 180 "the company window"
  Start-Sleep 2
  Choose $win "A small healthy company"; Report $win "healthy"
  Choose $win "A company in distress"; Report $win "healthy | distressed"
  $slider = ByType $win ([System.Windows.Automation.ControlType]::Slider) | Where-Object { $_.Current.HelpText -eq "Total debts" -or $_.Current.Name -eq "Total debts" } | Select-Object -First 1
  if (-not $slider) { $slider = (ByType $win ([System.Windows.Automation.ControlType]::Slider))[2] }
  $slider.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).SetValue(100)
  Invoke (ByName $win "Apply"); Say "what-if: total debts +100 %"
  Report $win "distressed, twice the debts"
}
finally {
  if (-not $p.HasExited) { $p.CloseMainWindow() | Out-Null; Start-Sleep 3; if (-not $p.HasExited) { Say "client left running, PID $($p.Id)" } else { Say "client closed" } }
}
