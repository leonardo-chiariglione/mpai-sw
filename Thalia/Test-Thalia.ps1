<#
  Test-Thalia.ps1 - checks the functions of Thalia that do not need a person: the Store, obtaining, updating and removing an App, running, pausing and resuming it, and the settings.

  It starts its own Thalia (port 5299) with its own folder of data and its own Store (port 5021), in a temporary folder: nothing of the User's Thalia, Store or Apps is touched. It builds what it runs
  (Thalia into Thalia\Core\bin\Dev, the Store into Thalia\Store\bin). Running an App starts its Module (up to a minute or two): -NoRun leaves that out.

    powershell -ExecutionPolicy Bypass -File Thalia\Test-Thalia.ps1 [-NoRun]
#>
param([switch]$NoRun)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$work = Join-Path $env:USERPROFILE 'Downloads\Claude-work'     # the only place it writes: Downloads, deleted at the end (never Temp)
$data = Join-Path $work ("thalia-test-" + [guid]::NewGuid().ToString('N').Substring(0, 8))
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
    # The local Controller the Apps run under (it carries the Store's AIM provider and the codecs of the Data Types).
    & dotnet build Thalia\Controller\ThaliaController.csproj -nologo -v q | Out-Null; if ($LASTEXITCODE) { throw 'The build of the local Controller failed.' }
    # The AIM of an Implementer that the test runs from the Store: its package is made from what this builds.
    & dotnet build AIMs\AIH\V1.1\ADT\Mpai.Aih.Adt.csproj -nologo -v q | Out-Null; if ($LASTEXITCODE) { throw 'The build of the AIM AIH-ADT failed.' }
    # A Store running from Thalia\Store\bin (the one of the User, on 5020) locks the folder: then the Store as it was last built is tested, and this says so, so that the test can run beside other work.
    & dotnet build Store\Service\StoreService.csproj -nologo -v q -o Thalia\Store\bin | Out-Null
    if ($LASTEXITCODE) {
        if (Test-Path Thalia\Store\bin\StoreService.dll) { Write-Host 'The Store could not be rebuilt (it is running from Thalia\Store\bin): the Store as last built is tested.' -ForegroundColor Yellow }
        else { throw 'The build of the Store failed.' }
    }
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

    # THE AIM OF AN IMPLEMENTER, FROM THE STORE (N-45): an App whose Module is an AIM the software does not carry. The Store is given the AIM (as its operator, Implementer 1: the test Store has no other) - its L3 and its
    # package, a zip - and the App; Thalia obtains the App, the Controller of the App obtains the AIM from the Store when the App starts, verifies the package (the Store says Same) and runs it from there.
    Write-Host "An AIM of the Store, run in a Module" -ForegroundColor Cyan
    $admin = (Get-Content (Join-Path $data 'Store\admin-key.txt') -Raw).Trim()
    $adt = Join-Path $data 'adt'; New-Item -ItemType Directory -Force (Join-Path $adt 'l3'), (Join-Path $data 'Store\packages'), (Join-Path $adt 'Apps\AIH\ADTT') | Out-Null
    $abin = Join-Path $root 'AIMs\AIH\V1.1\ADT\bin\Debug\net10.0'
    Compress-Archive -Path ('Mpai.Aih.Adt.dll', 'Mpai.Aih.Adt.deps.json' | ForEach-Object { Join-Path $abin $_ }) -DestinationPath (Join-Path $data 'Store\packages\AIH-ADT-V1.1-I07.zip')
    $l3 = Get-Content (Join-Path $root 'AIMs\AMDs\1AIH-ADT-V1.1-I01.json') -Raw | ConvertFrom-Json
    $l3.Identifier.ImplementationID = 'AIH-ADT-V1.1-I07'; $l3.Identifier.AIMInstID = '1AIH-ADT-V1.1-I07'
    $l3 | ConvertTo-Json -Depth 32 | Set-Content (Join-Path $adt 'l3\1AIH-ADT-V1.1-I07.json') -Encoding utf8
    & (Join-Path $root 'Store\Service\Submit-L3s.ps1') -Folder (Join-Path $adt 'l3') -Store $storeAt -AdminKey $admin -Packages (Join-Path $data 'Store\packages') | Out-Null
    $held = try { Invoke-RestMethod ($storeAt + 'MPAI/Store/L3/1AIH-ADT-V1.1-I07') } catch { $null }
    Check 'the Store has the AIM' ($held.Identifier.AIMInstID -eq '1AIH-ADT-V1.1-I07')
    foreach ($f in 'ADT.orch', 'app.json', 'icon.svg') { Copy-Item (Join-Path $root "Apps\AIH\ADT\$f") (Join-Path $adt 'Apps\AIH\ADTT') }
    foreach ($f in 'ADT.orch', 'app.json') { $p = Join-Path $adt "Apps\AIH\ADTT\$f"; (Get-Content $p -Raw).Replace('2AIH-ADT-V1.1-I01', '1AIH-ADT-V1.1-I07') | Set-Content $p -Encoding utf8 }
    & (Join-Path $root 'Store\Service\Submit-Apps.ps1') -Folder (Join-Path $adt 'Apps') -Store $storeAt -AdminKey $admin | Out-Null
    $needs = Invoke-RestMethod "$base/api/store/apps/ADTT"
    Check 'the App of that AIM is on the shelf and its needs are shown before it is accepted' ($needs.module -eq '1AIH-ADT-V1.1-I07' -and $needs.needs.canRun -eq $true) ($needs | ConvertTo-Json -Depth 4 -Compress)
    $got = Post '/api/store/obtain/ADTT' '{"accepted":true}'
    Check 'the App is obtained' ($got.version -eq '1.0') ($got | ConvertTo-Json -Compress)
    Check 'Thalia says the AIM is in the Store and the Controller obtains it when the App starts' ($got.controller.state -like 'in the Store*') $got.controller.state
    if (-not $NoRun) {
        Post '/api/run/local/ADTT' | Out-Null
        $r = $null
        for ($i = 0; $i -lt 150; $i++) { Start-Sleep 2; $r = (Invoke-RestMethod "$base/api/runs") | Where-Object app -eq 'ADTT'; if ($r.phase -eq 'running' -or $r.phase -like 'failed*') { break } }
        Check 'the Module starts: its Controller obtained the AIM from the Store and the Store said Same' ($r.phase -eq 'running') (($r | ConvertTo-Json -Compress) + ' RUNS ' + ((Invoke-RestMethod "$base/api/runs") | ConvertTo-Json -Compress) + ' ' + $(try { (Invoke-RestMethod "$base/api/log/local/ADTT") | Out-String } catch { 'no log' }))
        if ($r.phase -eq 'running') {
            $ctl = $r.controller.TrimEnd('/')
            $cid = (Invoke-RestMethod -Method Post "$ctl/MPAI/AIFU/Controller" -Body '{}' -ContentType 'application/json').id
            $mid = (Invoke-RestMethod -Method Post "$ctl/MPAI/AIFU/$cid/MODULE/Start" -Body '{"module":"1AIH-ADT-V1.1-I07"}' -ContentType 'application/json').id
            $now = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
            $req = '{"Header":"AIH-ADT-V1.1","UserID":"end-user-1","AuditID":"audit-1","AuditTime":{"Header":"OSD-TIM-V1.5","TimeID":"t1","Data":"' + $now + '"},"Audit":{"Request":{"AuditPeriod":{"Header":"OSD-TIM-V1.5","TimeID":"t0","Data":"2026-11-01T00:00:00Z"},"ProcessingOperations":[],"Tokens":{"Header":"AIH-TKN-V1.1","UserID":"end-user-1","TokensID":"tk1","TokensTime":{"Header":"OSD-TIM-V1.5","TimeID":"t2","Data":"' + $now + '"},"Tokens":[{"ProcessType":"Auditing"}]}}}}'
            Invoke-RestMethod -Method Post "$ctl/MPAI/AIFU/$cid/MODULE/$mid/Input/AIH-ADT-V1.1/1" -Body $req -ContentType 'application/json' | Out-Null
            $resp = $null
            for ($i = 0; $i -lt 30 -and -not $resp; $i++) { try { $resp = Invoke-RestMethod "$ctl/MPAI/AIFU/$cid/MODULE/$mid/Output/AIH-ADT-V1.1/1" -TimeoutSec 20 } catch { Start-Sleep 1 } }
            Check 'the AIM answers an Audit Request with an Audit Response' ($resp.AuditID -eq 'audit-1' -and $resp.UserID -eq 'end-user-1' -and $null -ne $resp.Audit.Response) ($resp | ConvertTo-Json -Depth 6 -Compress)
            Check 'nothing is reported as unlicensed: Auditing was given no AIH Data' (@($resp.Audit.Response.AIHDataIDs).Count -eq 0)
            Check 'it pauses' ((Post '/api/pause/ADTT').paused -eq $true)
            Check 'it resumes' ((Post '/api/resume/ADTT').paused -eq $false)
        }
        Post '/api/stop/local/ADTT' | Out-Null; Start-Sleep 3
    }
    Check 'the App is removed; the Module and its AIM stay with the Controller' ((Post '/api/remove/ADTT').removed -eq $true)
    Check 'the Controller kept what it obtained from the Store' (Test-Path (Join-Path $data 'Controller\AIMs\1AIH-ADT-V1.1-I07.json'))

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
    try { Remove-Item $data -Recurse -Force -ErrorAction SilentlyContinue; if (-not (Get-ChildItem $work -Force -ErrorAction SilentlyContinue)) { Remove-Item $work -Force -ErrorAction SilentlyContinue } } catch { }
}
if ($script:failed) { Write-Host "$script:failed check(s) failed." -ForegroundColor Red; exit 1 } else { Write-Host 'All checks passed.' -ForegroundColor Green }
