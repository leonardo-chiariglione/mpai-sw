# TELEVISION MEDIA ANALYSIS, picked with dialogs: you choose the video of the programme, say how much of it to analyse and, if you like, what it is about; the App is built if it is not, run, and the
# folder of the result (the Multimodal Event Descriptors) is opened.
param([string]$Controller = '')          # Thalia gives the address of the Controller it started for the App (N-49); the launcher gives none
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName Microsoft.VisualBasic
$root = $env:MPAI_ROOT; if (-not $root) { $root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path }
$title = 'Television Media Analysis'

$project = Join-Path $root 'Apps\OSD\TMA\App\TmaApp.csproj'
$exe = Join-Path $root 'Apps\OSD\TMA\App\bin\Debug\net10.0\TmaApp.exe'
$out = Join-Path (Join-Path $env:USERPROFILE 'Downloads') "MPAI OSD\TMA\$(Get-Date -Format 'yyyy-MM-dd HH.mm.ss')"

try {
    $d = New-Object Windows.Forms.OpenFileDialog; $d.Title = 'The video of the programme'; $d.Filter = 'Videos (*.mp4;*.mkv;*.avi;*.mov;*.webm)|*.mp4;*.mkv;*.avi;*.mov;*.webm|All files (*.*)|*.*'
    if ($d.ShowDialog() -ne 'OK') { throw 'Cancelled.' }
    $seconds = [Microsoft.VisualBasic.Interaction]::InputBox("How many seconds of the programme, from its start?`n`nThe analysis takes several times as long as the part analysed.", $title, '30')
    if ($seconds -eq '') { throw 'Cancelled.' }
    $text = [Microsoft.VisualBasic.Interaction]::InputBox("What is the programme about, and who is in it? (an auxiliary text: you may leave it empty)", $title, '')
} catch { Write-Host $_.Exception.Message; Start-Sleep 2; exit 1 }
$arguments = @('--video', $d.FileName, '--seconds', $seconds, '--out', $out)
if ($text -ne '') { $arguments += @('--text', $text) }

Write-Host "Building $title (a first build takes a while)..." -ForegroundColor Cyan
& dotnet build $project -nologo -v q 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host "The build of $project failed." -ForegroundColor Red; exit 1 }
if ($Controller) { $arguments += @('--controller', $Controller) }
& $exe @arguments
if ($LASTEXITCODE -eq 0) { Write-Host "`nResults in $out" -ForegroundColor Green; Invoke-Item $out } else { Write-Host "`nThe App stopped: see what it said above." -ForegroundColor Yellow }
