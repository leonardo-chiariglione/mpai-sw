<#
  Test-Thalia.ps1 - checks the functions of Thalia that do not need a person: the Store, obtaining, updating and removing an App, running, pausing and resuming it, and the settings.

  It starts its own Thalia (port 5299) with its own folder of data and its own Store (port 5021), in a temporary folder: nothing of the User's Thalia, Store or Apps is touched. It builds what it runs
  (Thalia into Thalia\Core\bin\Dev, the Store into Thalia\Store\bin). Running an App starts its Module (up to a minute or two): -NoRun leaves that out.

    powershell -ExecutionPolicy Bypass -File Thalia\Test-Thalia.ps1 [-NoRun]
#>
param([switch]$NoRun)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$data = Join-Path ([IO.Path]::GetTempPath()) ("thalia-test-" + [guid]::NewGuid().ToString('N').Substring(0, 8))
$base = 'http://localhost:5299'; $storeAt = 'http://127.0.0.1:5021/'
$script:failed = 0
function Check([string]$what, [bool]$ok, [string]$detail = '') {
    if ($ok) { Write-Host ("  ok    " + $what) -ForegroundColor Green } else { Write-Host ("  FAIL  " + $what + $(if ($detail) { ' - ' + $detail })) -ForegroundColor Red; $script:failed++ }
}
function Post($path, $body = $null) {
    try { Invoke-RestMethod -Method Post "$base$path" -ContentType 'application/json' -Body $(if ($null -ne $body) { $body } else { '{}' }) -TimeoutSec 120 }
    catch { [pscustomobject]@{ error = $true; status = [int]$_.Exception.Response.StatusCode.value__ } }
}

Write-Host "Building..." -ForegroundColor Cyan
Push-Location $root
try {
    & dotnet build Thalia\Core\Thalia.csproj -nologo -v q -o Thalia\Core\bin\Dev | Out-Null; if ($LASTEXITCODE) { throw 'The build of Thalia failed.' }
    & dotnet build Store\Service\StoreService.csproj -nologo -v q -o Thalia\Store\bin | Out-Null; if ($LASTEXITCODE) { throw 'The build of the Store failed.' }
} finally { Pop-Location }

$thalia = Start-Process dotnet -ArgumentList 'Thalia\Core\bin\Dev\Thalia.dll', '--Urls', $base, '--Data', "`"$data`"", '--Store', $storeAt -WorkingDirectory $root -WindowStyle Hidden -PassThru `
    -RedirectStandardOutput (Join-Path $data 'thalia.log' | ForEach-Object { New-Item -ItemType Directory -Force (Split-Path $_) | Out-Null; $_ }) -RedirectStandardError (Join-Path $data 'thalia.err')
try {
    for ($i = 0; $i -lt 40; $i++) { try { Invoke-RestMethod "$base/api/prefs" -TimeoutSec 2 | Out-Null; break } catch { Start-Sleep 1 } }

    Write-Host "The Store" -ForegroundColor Cyan
    $list = Invoke-RestMethod "$base/api/store/apps" -TimeoutSec 120
    Check 'the Store answers' (-not $list.problem) $list.problem
    $ids = @($list.apps | ForEach-Object { $_.id })
    Check 'it offers the Apps Thalia can run' (($ids -contains 'AMC') -and ($ids -contains 'ACR') -and ($ids -contains 'MAC'))
    Check 'what comes with Thalia is marked' (($list.apps | Where-Object id -eq 'MCA').state -eq 'bundled')
    Check 'what is to obtain is marked' (($list.apps | Where-Object id -eq 'AMC').state -eq 'available')
    $one = Invoke-RestMethod "$base/api/store/apps/ACR"
    Check 'an App shows what its Module needs' (@($one.needs.aims).Count -gt 0 -and $one.needs.why)
    Check 'and the licences of what its AIMs obtain' (@($one.sources).Count -gt 0 -and ($one.sources | Where-Object licence))
    Check 'a search finds by what an App does' (@((Invoke-RestMethod "$base/api/store/apps?search=newspapers").apps).Count -eq 1)

    Write-Host "Obtain, update, remove" -ForegroundColor Cyan
    Check 'nothing is obtained without the User accepting' ((Post '/api/store/obtain/AMC' '{"accepted":false}').error)
    $o = Post '/api/store/obtain/AMC' '{"accepted":true,"licences":["MIT"]}'
    Check 'an accepted App is obtained' ($o.version -eq '1.0') ($o | ConvertTo-Json -Compress)
    Check 'the Controller holds its Module' ($o.controller.state -like 'held*')
    $c = (Invoke-RestMethod "$base/api/catalogue").apps | Where-Object { $_.id -eq 'AMC' -and $_.where -eq 'local' }
    Check 'it is in the catalogue as obtained' ($c.obtained -eq $true)
    Check 'the Store now says it is obtained' ((((Invoke-RestMethod "$base/api/store/apps").apps | Where-Object id -eq 'AMC').state) -eq 'obtained')
    # a newer version in the Store
    $tmp = Join-Path $data 'newer\Apps\MMC\AMC'; New-Item -ItemType Directory -Force $tmp | Out-Null
    Copy-Item (Join-Path $root 'Apps\MMC\AMC\MMC-AMC.orch'), (Join-Path $root 'Apps\MMC\AMC\icon.svg') $tmp
    $j = Get-Content (Join-Path $root 'Apps\MMC\AMC\app.json') -Raw | ConvertFrom-Json; $j.Version = '1.1'; $j | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $tmp 'app.json') -Encoding utf8
    & (Join-Path $root 'Store\Service\Submit-Apps.ps1') -Folder (Join-Path $data 'newer\Apps') -Store $storeAt -AdminKey ((Get-Content (Join-Path $data 'Store\admin-key.txt') -Raw).Trim()) | Out-Null
    Check 'a newer version is offered as an update' ((((Invoke-RestMethod "$base/api/store/apps").apps | Where-Object id -eq 'AMC').state) -eq 'update')
    Check 'the update is obtained' ((Post '/api/store/obtain/AMC' '{"accepted":true}').version -eq '1.1')

    if (-not $NoRun) {
        Write-Host "Run, pause, resume, stop" -ForegroundColor Cyan
        Post '/api/run/local/AMC' | Out-Null
        $r = $null
        for ($i = 0; $i -lt 120; $i++) { Start-Sleep 2; $r = (Invoke-RestMethod "$base/api/runs") | Where-Object app -eq 'AMC'; if ($r.phase -eq 'running' -or $r.phase -like 'failed*') { break } }
        Check 'an App obtained runs from the User''s folder' ($r.phase -eq 'running') $r.phase
        Check 'it cannot be removed while it runs' ((Post '/api/remove/AMC').error)
        Check 'it pauses' ((Post '/api/pause/AMC').paused -eq $true)
        Check 'the run says it is paused' (((Invoke-RestMethod "$base/api/runs") | Where-Object app -eq 'AMC').paused -eq $true)
        Check 'it resumes' ((Post '/api/resume/AMC').paused -eq $false)
        Post '/api/stop/local/AMC' | Out-Null; Start-Sleep 3
    }
    Check 'what is not running cannot be paused' ((Post '/api/pause/NPR').error)
    Check 'what comes with Thalia cannot be removed' ((Post '/api/remove/MCA').error)
    $rm = Post '/api/remove/AMC'
    Check 'what was obtained is removed' ($rm.removed -eq $true) ($rm | ConvertTo-Json -Compress)
    Check 'and leaves the catalogue' (-not ((Invoke-RestMethod "$base/api/catalogue").apps | Where-Object { $_.id -eq 'AMC' -and $_.where -eq 'local' }))

    Write-Host "Settings" -ForegroundColor Cyan
    Check 'a Store is tested before it is kept' ((Post '/api/settings/test' ('{"kind":"store","url":"' + $storeAt + '"}')).ok -eq $true)
    Check 'an address that does not answer is said so' ((Post '/api/settings/test' '{"kind":"store","url":"http://127.0.0.1:5999/"}').ok -eq $false)
    Check 'what is not an address is refused' ((Post '/api/settings' '{"mas":"not an address"}').error)
    Check 'an address is kept' ((Post '/api/settings' '{"mas":"https://localhost:5105/"}').mas -eq 'https://localhost:5105/')
    Check 'preferences have the devices' ((Post '/api/prefs' '{"micLabel":"Test microphone"}').micLabel -eq 'Test microphone')
}
finally {
    try { Stop-Process -Id $thalia.Id -Force -ErrorAction SilentlyContinue } catch { }
    Get-Process dotnet -ErrorAction SilentlyContinue | ForEach-Object { $cl = (Get-CimInstance Win32_Process -Filter "ProcessId=$($_.Id)" -ErrorAction SilentlyContinue).CommandLine; if ($cl -match [regex]::Escape($data)) { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue } }
    Start-Sleep 2
    try { Remove-Item $data -Recurse -Force -ErrorAction SilentlyContinue } catch { }
}
if ($script:failed) { Write-Host "$script:failed check(s) failed." -ForegroundColor Red; exit 1 } else { Write-Host 'All checks passed.' -ForegroundColor Green }
