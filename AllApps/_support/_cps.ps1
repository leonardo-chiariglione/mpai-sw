# CONVERSATION WITH PERSONAL STATUS, picked with dialogs: you use the camera and the microphone of this PC (the Camera Unit and the Microphone Unit), or you choose a picture of your face and say what you
# say (typed, or a WAV file), as many turns as you like; the App is built if it is not, run, and the folder of the answers (the Portable Avatars, one for each turn) is opened.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName Microsoft.VisualBasic
$root = $env:MPAI_ROOT; if (-not $root) { $root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path }
$title = 'Conversation with Personal Status'

function Pick([string]$caption, [string]$filter) {
    $d = New-Object Windows.Forms.OpenFileDialog; $d.Title = $caption; $d.Filter = $filter
    if ($d.ShowDialog() -ne 'OK') { throw 'Cancelled.' }
    $d.FileName
}
function Yes([string]$question) { [Windows.Forms.MessageBox]::Show($question, $title, 'YesNo', 'Question') -eq 'Yes' }

$project = Join-Path $root 'Apps\MMC\CPS\App\CpsApp.csproj'
$exe = Join-Path $root 'Apps\MMC\CPS\App\bin\Debug\net10.0-windows10.0.19041.0\CpsApp.exe'
$out = Join-Path (Join-Path $env:USERPROFILE 'Downloads') "MPAI MMC\CPS\$(Get-Date -Format 'yyyy-MM-dd HH.mm.ss')"

try {
    $arguments = @()
    if (Yes "Use the camera and the microphone of this PC?`n`nYes: the camera sees your face and you speak; if you say nothing the conversation ends.`nNo: you pick a picture of your face and type or pick what you say.") {
        $arguments += @('--camera', '--microphone')
    } else {
        $arguments += @('--face', (Pick 'A picture of your face' 'Pictures (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png|All files (*.*)|*.*'))
        do {
            if (Yes "Do you type what you say?`n`nYes: you type it.`nNo: you pick a WAV file of your voice.") {
                $words = [Microsoft.VisualBasic.Interaction]::InputBox('What do you say?', $title, 'I am so excited about the concert tonight! Do you know what time it starts?')
                if ($words -eq '') { throw 'Cancelled.' }
                $arguments += @('--text', $words)
            } else { $arguments += @('--speech', (Pick 'What you say (a 16-bit WAV file)' 'WAV files (*.wav)|*.wav|All files (*.*)|*.*')) }
        } while (Yes 'Another turn of the conversation?')
    }
} catch { Write-Host $_.Exception.Message; Start-Sleep 2; exit 1 }
$arguments += @('--out', $out)

Write-Host "Building $title (a first build takes a while)..." -ForegroundColor Cyan
& dotnet build $project -nologo -v q 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host "The build of $project failed." -ForegroundColor Red; exit 1 }
& $exe @arguments
if ($LASTEXITCODE -eq 0) { Write-Host "`nResults in $out" -ForegroundColor Green; Invoke-Item $out } else { Write-Host "`nThe App stopped: see what it said above." -ForegroundColor Yellow }
