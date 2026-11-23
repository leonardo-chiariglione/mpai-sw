# COMPANY PERFORMANCE PREDICTION (MPAI-CUI CPP V2.1), picked with dialogs: you choose the file of a company and say the horizon; the Controller and the User Agent are built if they are not,
# the Controller runs as a program of its own (port 5063) for the run, the User Agent gives the company and prints the report, the report is kept as a JSON file and its folder is opened.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName Microsoft.VisualBasic
$root = $env:MPAI_ROOT; if (-not $root) { $root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path }
$app = Join-Path $root 'Apps\CUI\CPP'
$title = 'Company Performance Prediction'
$port = 5063
$out = Join-Path (Join-Path $env:USERPROFILE 'Downloads') "MPAI CUI\CPP\$(Get-Date -Format 'yyyy-MM-dd HH.mm.ss')"

try {
    $d = New-Object Windows.Forms.OpenFileDialog; $d.Title = 'The file of the company (the folder Samples has a healthy and a distressed one)'
    $d.Filter = 'Company files (*.json)|*.json'; $d.InitialDirectory = Join-Path $app 'Samples'
    if ($d.ShowDialog() -ne 'OK') { throw 'Cancelled.' }
    $company = $d.FileName
    $horizon = [Microsoft.VisualBasic.Interaction]::InputBox("How many months ahead? (12, 24 or 36)`n`nCancel to take the horizon the file gives.", $title, '12')
} catch { Write-Host $_.Exception.Message; Start-Sleep 2; exit 1 }

Write-Host "Building $title (a first build takes a while)..." -ForegroundColor Cyan
foreach ($p in 'Controller\CuiController.csproj', 'Ua\CuiUa.csproj') {
    & dotnet build (Join-Path $app $p) -nologo -v q 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Host "The build of $p failed." -ForegroundColor Red; exit 1 }
}
New-Item -ItemType Directory -Force $out | Out-Null
$model = Join-Path $root 'AIMs\CUI\V2.1\Models\FinancialDefault-Polish5y.json'
$controller = Start-Process dotnet -ArgumentList @((Join-Path $app 'Controller\bin\Debug\net10.0\CuiController.dll'), '--listen', "http://127.0.0.1:$port", '--model', $model) -WindowStyle Minimized -PassThru
try {
    Start-Sleep 8
    $arguments = @((Join-Path $app 'Ua\bin\Debug\net10.0\CuiUa.dll'), '--company', $company, '--controller', "http://127.0.0.1:$port", '--json', (Join-Path $out 'report.json'))
    if ($horizon) { $arguments += @('--horizon', $horizon) }
    & dotnet @arguments
    if ($LASTEXITCODE -eq 0) { Write-Host "`nThe report is in $out" -ForegroundColor Green; Invoke-Item $out } else { Write-Host "`nThe App stopped: see what it said above." -ForegroundColor Yellow }
} finally {
    if ($controller -and -not $controller.HasExited) { Stop-Process -Id $controller.Id -Force }
}
