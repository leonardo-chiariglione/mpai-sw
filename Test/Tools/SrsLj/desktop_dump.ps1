# N-27: what the desktop client shows after N seconds (names of its elements), then it is closed.
param([string]$Service = "https://localhost:5115/", [int]$Seconds = 60)
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$env:MPAI_MAS_SERVER = $Service
$p = Start-Process "D:\AI\AIF\UserAgent\Clients\Desktop\src\bin\Debug\net10.0-windows10.0.19041.0\RcaApp.exe" -PassThru
Start-Sleep $Seconds
$c = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $p.Id)
foreach ($w in [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $c)) {
  "WINDOW: " + $w.Current.Name
  foreach ($e in $w.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
    $n = $e.Current.Name; if ($n) { "  " + $e.Current.ControlType.ProgrammaticName + " | " + $n.Substring(0, [Math]::Min(120, $n.Length)) + " | enabled " + $e.Current.IsEnabled + " offscreen " + $e.Current.IsOffscreen }
  }
}
$p.CloseMainWindow() | Out-Null; Start-Sleep 3; if (-not $p.HasExited) { "still running PID $($p.Id)" }
