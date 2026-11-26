<#
  Test-ThaliaApps.ps1 - one check per App that Thalia runs on this machine besides the browser Apps: Thalia starts the App, and the App does its job.

  It starts its own Thalia (port 5299) with its own folder of data: nothing of the User's Thalia, Store or Apps is touched (the Apps' storage is in that folder too).
    - AES, SME, SRS, EES, ARP, EAE, CPP: Thalia starts the App's local Controller; the App's own program is then run with files on that Controller (--controller) and must give its result.
    - ASM: Thalia starts the ASM Controller and window; the window must show the Module's welcome.
    - CAS, CPS, TMA: Thalia starts the App's Controller and its questions (dialogs nobody answers here); the App's program is then run with files on that Controller and must give its result.
    - AUA: Thalia starts the door server and its page; the A-User is ready, is told to open the door when the bell rings, the Visitor rings, and the door opens.
    - LTP, VMS: Thalia starts the program (LTP) or the server and its page (VMS); they must be running 20 s later (and the page must answer).
  Then Thalia stops the App, and what it started must be gone. Needs the Apps built (their launchers build them) and, for ASM, CAS and CPS, Ollama.

    powershell -ExecutionPolicy Bypass -File Thalia\Test-ThaliaApps.ps1 [-Apps AES,ASM,...] [-Data <folder>] [-Where local|mas] [-FromStore]
  (N-49, 2026-11-24)

  -FromStore (2026-11-25): the Apps are not given to Thalia with the software: Thalia obtains each from its Store first (the User's Obtain), then runs it as a local App.
  -Where mas (2026-11-25): each App is run on an MPAI as a Service instance - one of its own (port 5108, built apart, nothing of the User's instance on 5105 is touched) - as the
  User runs it from the "MPAI as a Service" row; the App's own program is given the instance as its Controller.
  The browser Apps (MTS, MCA, ...): the Controller (local, or the instance) must also start the Module of the App (MODULE/Start), not only the page answer.
#>
param([string[]]$Apps = @('AES', 'SME', 'SRS', 'EES', 'ARP', 'EAE', 'CPP', 'ASM', 'CAS', 'CPS', 'TMA', 'LTP', 'VMS', 'AUA'), [string]$Data = '',
      [ValidateSet('local', 'mas')][string]$Where = 'local', [switch]$FromStore)
$Apps = @($Apps | ForEach-Object { $_ -split ',' })
$ErrorActionPreference = 'Continue'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $Data) { $Data = Join-Path $env:USERPROFILE ('Downloads\Claude-work\thalia-apps-' + [guid]::NewGuid().ToString('N').Substring(0, 8)) }
$results = Join-Path $Data 'results'; New-Item -ItemType Directory -Force $Data, $results | Out-Null
$base = 'http://localhost:5299'
$script:failed = 0
$startedAt = Get-Date
function Check([string]$what, [bool]$ok, [string]$detail = '') {
    if ($ok) { Write-Host ("  ok    " + $what) -ForegroundColor Green } else { Write-Host ("  FAIL  " + $what + $(if ($detail) { ' - ' + $detail })) -ForegroundColor Red; $script:failed++ }
}
function Kids($top) { $all = Get-CimInstance Win32_Process; $set = @($top); $more = $true; while ($more) { $more = $false; foreach ($p in $all) { if ($set -contains $p.ParentProcessId -and -not ($set -contains $p.ProcessId)) { $set += $p.ProcessId; $more = $true } } }; $set | Select-Object -Skip 1 }
# A client host of the browser (RcaWeb.Host) whose Controller is gone is a leftover: it keeps its files locked (the build of Thalia then fails). Those started in this run are stopped.
function StopOrphanHosts([datetime]$since) {
    foreach ($p in @(Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -match 'RcaWeb\.Host' -and $_.CreationDate -ge $since })) {
        if ($p.CommandLine -match '--Service http://127\.0\.0\.1:(\d+)/' -and -not (Get-NetTCPConnection -LocalPort ([int]$Matches[1]) -State Listen -ErrorAction SilentlyContinue)) { Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue }
    }
}
function RunWithFiles([string]$file, [string[]]$arguments, [string]$name) {
    $line = ($arguments | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }) -join ' '
    $p = Start-Process -FilePath $file -ArgumentList $line -NoNewWindow -PassThru -RedirectStandardOutput (Join-Path $results "$name.out.txt") -RedirectStandardError (Join-Path $results "$name.err.txt")
    $null = $p.Handle
    if (-not $p.WaitForExit(900000)) { Stop-Process -Id $p.Id -Force; return -1 }
    $p.WaitForExit(); return $p.ExitCode
}
# The Controller (local, or the instance) starts the Module of the App: a Controller, then MODULE/Start, through the Controller-User Agent API (as Thalia's Pause does); the Module is then stopped.
function ModuleStarts([string]$controller, [string]$module) {
    $made = & curl.exe -k -s -m 30 -X POST -H 'Content-Type: application/json' -d '{}' "$controller/MPAI/AIFU/Controller" | ConvertFrom-Json
    if (-not $made.id) { return "no Controller made" }
    $body = Join-Path $results 'module.json'; [IO.File]::WriteAllText($body, (@{ module = $module } | ConvertTo-Json -Compress))
    $text = & curl.exe -k -s -m 600 -X POST -H 'Content-Type: application/json' --data-binary "@$body" "$controller/MPAI/AIFU/$($made.id)/MODULE/Start"
    $started = try { $text | ConvertFrom-Json } catch { $null }
    if ($null -eq $started -or $null -eq $started.id) { return "MODULE/Start: $text" }
    & curl.exe -k -s -m 30 "$controller/MPAI/AIFU/$($made.id)/MODULE/$($started.id)/Stop" | Out-Null
    return ''
}

Push-Location $root
& dotnet build Thalia\Core\Thalia.csproj -nologo -v q -o Thalia\Core\bin\Dev | Out-Null; if ($LASTEXITCODE) { throw 'The build of Thalia failed.' }
& dotnet build Thalia\Controller\ThaliaController.csproj -nologo -v q | Out-Null; if ($LASTEXITCODE) { throw 'The build of the local Controller failed.' }
$masUrl = 'https://localhost:5108'; $mas = $null
if ($Where -eq 'mas') {
    # an instance of MPAI as a Service of this check: built apart (bin\Review), its own port, the Apps of the User's instance; no warm-up (the models load when an App starts)
    & dotnet build Apps\MAS\Service\src\MasService.csproj -nologo -v q -o Apps\MAS\Service\bin\Review | Out-Null; if ($LASTEXITCODE) { throw 'The build of the MPAI as a Service instance failed.' }
    $config = Get-Content Apps\MAS\Service\mas-server-AI.json -Raw | ConvertFrom-Json
    $config.ListenUrl = "$masUrl/"; $config.PSObject.Properties.Remove('WarmUpDirectory')
    $configFile = Join-Path $Data 'mas-review.json'; [IO.File]::WriteAllText($configFile, ($config | ConvertTo-Json -Depth 5))
    $mas = Start-Process dotnet -ArgumentList 'Apps\MAS\Service\bin\Review\MasService.dll', "`"$configFile`"" -WorkingDirectory $root -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $Data 'mas.log') -RedirectStandardError (Join-Path $Data 'mas.err')
    $up = $false; for ($i = 0; $i -lt 300 -and -not $up; $i++) { $up = (& curl.exe -k -s -o NUL -w '%{http_code}' "$masUrl/MPAI/AIFU/Apps") -eq '200'; if (-not $up) { Start-Sleep 1 } }
    if (-not $up) { Stop-Process -Id $mas.Id -Force -ErrorAction SilentlyContinue; throw 'The MPAI as a Service instance of the check did not come up.' }
}
$bundled = if ($FromStore -or $Where -eq 'mas') { 'NONE' } else { 'MTS,MCA,AMQ,NPR,' + ($Apps -join ',') }
$thalia = Start-Process dotnet -ArgumentList 'Thalia\Core\bin\Dev\Thalia.dll', '--Urls', $base, '--Apps', $bundled, '--Data', "`"$Data`"", '--Store', 'http://127.0.0.1:5021/', '--Mas', "$masUrl/", '--NoWindows', 'true' `
    -WorkingDirectory $root -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $Data 'thalia.log') -RedirectStandardError (Join-Path $Data 'thalia.err')
try {
    for ($i = 0; $i -lt 60; $i++) { try { Invoke-RestMethod "$base/api/prefs" -TimeoutSec 2 | Out-Null; break } catch { Start-Sleep 1 } }
    $catalogue = Invoke-RestMethod "$base/api/catalogue" -TimeoutSec 60
    if ($Where -eq 'mas') { $offered = @($catalogue.apps | Where-Object where -eq 'mas' | ForEach-Object id); Write-Host ("The instance offers: " + ($offered -join ', ')) }
    if ($FromStore) {
        # the User's Obtain: the Store's shelf as Thalia shows it, then each App obtained (what it needs accepted), into this User's folder of Apps
        $shelf = Invoke-RestMethod "$base/api/store/apps" -TimeoutSec 120
        Check 'the Store answers' (-not $shelf.problem) $shelf.problem
        foreach ($app in $Apps) {
            $item = @($shelf.apps) | Where-Object id -eq $app
            if (-not $item) { Check "the Store has $app" $false 'not on its shelf'; continue }
            if (-not $item.runsHere) { Check "Thalia says it runs $app" $false 'runsHere is false' }
            $got = try { Invoke-RestMethod -Method Post "$base/api/store/obtain/$app" -ContentType 'application/json' -Body '{"accepted":true}' -TimeoutSec 120 } catch { $null }
            Check "Thalia obtains $app from the Store" ($got -and $got.version) $(if ($got) { $got | ConvertTo-Json -Compress -Depth 4 } else { $Error[0].ToString() })
        }
        $catalogue = Invoke-RestMethod "$base/api/catalogue" -TimeoutSec 60
    }
    $speech = Join-Path $root 'Test\Data\Speech\clip174.wav'; $model = Join-Path $root 'Test\Data\Speech\clip251.wav'
    $asmLog = Join-Path $root 'Apps\CAE\ASM\Ua\bin\Debug\net10.0-windows10.0.19041.0\asm-ua.log'
    foreach ($app in $Apps) {
        Write-Host $app -ForegroundColor Cyan
        $mark = @(Get-Content $asmLog -ErrorAction SilentlyContinue).Count
        $already = @(Kids $thalia.Id)          # what Thalia runs before the App (its Store): not the App's
        $listed = @($catalogue.apps) | Where-Object { $_.id -eq $app -and $_.where -eq $Where }
        if (-not $listed) { Check "Thalia lists $app ($Where)" $false 'not in the catalogue'; continue }
        try { Invoke-RestMethod -Method Post "$base/api/run/$Where/$app" -TimeoutSec 30 | Out-Null } catch { Check "Thalia runs $app" $false $_.Exception.Message; continue }
        $r = $null
        for ($i = 0; $i -lt 400; $i++) {
            Start-Sleep 1
            $r = @(Invoke-RestMethod "$base/api/runs" -TimeoutSec 10) | ForEach-Object { $_ } | Where-Object { $_.key -eq "${Where}:$app" }
            if (-not $r -or $r.phase -eq 'running' -or $r.phase -like 'failed*' -or $r.phase -like 'ended*') { break }
        }
        Check "Thalia runs $app ($Where)" ($r -and $r.phase -eq 'running') $r.phase
        if (-not $r -or $r.phase -ne 'running') {
            $log = try { Invoke-RestMethod "$base/api/log/$Where/$app" -TimeoutSec 10 } catch { '' }
            if ($log) { Set-Content -Path (Join-Path $results "$app.thalia-log.txt") -Value $log -Encoding utf8 }
            continue
        }
        $c = if ($Where -eq 'mas') { $masUrl } elseif ($r.controller) { $r.controller.TrimEnd('/') } else { '' }
        $exe = Join-Path $root "Apps\CAE\$app\App\bin\Debug\net10.0\$((Get-Culture).TextInfo.ToTitleCase($app.ToLower()))App.exe"
        $out = Join-Path $results $app
        switch ($app) {
            'AES' { Check 'AES adds the emotion' ((RunWithFiles $exe @('--controller', $c, '--speech', $speech, '--emotion', 'SADNESS', '--degree', '0.7', '--out', "$out.wav") $app) -eq 0 -and (Test-Path "$out.wav")) }
            'SME' { Check 'SME gives the prosody of the model' ((RunWithFiles $exe @('--controller', $c, '--speech', $speech, '--model', $model, '--out', "$out.wav") $app) -eq 0 -and (Test-Path "$out.wav")) }
            'EES' { Check 'EES adds the emotion' ((RunWithFiles $exe @('--controller', $c, '--speech', $speech, '--emotion', 'HAPPINESS', '--degree', '0.6', '--out', "$out.wav") $app) -eq 0 -and (Test-Path "$out.wav")) }
            'SRS' {
                & $exe --demo "$out-demo" --original $speech --sections '1.0-1.8' | Out-Null
                Check 'SRS restores the damaged section' ((RunWithFiles $exe @('--controller', $c, '--voice', (Join-Path $root 'Test\Data\Speech\clip1272.wav'), '--damaged', "$out-demo\damaged.wav", '--sections', '1.0-1.8', '--say', 'the', '--out', "$out.wav") $app) -eq 0 -and (Test-Path "$out.wav"))
            }
            'ARP' {
                $tape = Join-Path $root 'Test\Data\Music\tic-golden\sb.wav'
                $code = RunWithFiles $exe @('--controller', $c, '--tape', $tape, '--out', $out) $app
                Check 'ARP keeps the tape as it is and gives the access copy' ($code -eq 0 -and (Test-Path "$out\access-copy.wav") -and ((Get-FileHash "$out\master\preservation-audio.wav").Hash -eq (Get-FileHash $tape).Hash))
            }
            'EAE' {
                & $exe --demo "$out-room" | Out-Null
                $code = RunWithFiles $exe @('--controller', $c, '--capture', "$out-room\room.wav", '--array', "$out-room\array.json", '--out', $out) $app
                Check 'EAE finds the two speakers of the demo room' ($code -eq 0 -and (Test-Path "$out\scene.json") -and ((Get-Content "$out\scene.json" -Raw) -match '"AudioObjectCount":\s*2'))
            }
            'CPP' { Check 'CPP gives the report' ((RunWithFiles 'dotnet' @((Join-Path $root 'Apps\CUI\CPP\Ua\bin\Debug\net10.0\CuiUa.dll'), '--company', (Join-Path $root 'Apps\CUI\CPP\Samples\distressed.json'), '--controller', $c, '--json', "$out.json") $app) -eq 0 -and (Test-Path "$out.json")) }
            'ASM' {
                $w = $null
                for ($i = 0; $i -lt 120 -and -not $w; $i++) { $w = Get-Content $asmLog | Select-Object -Skip $mark | Where-Object { $_ -match 'answer shown' } | Select-Object -First 1; if (-not $w) { Start-Sleep 1 } }
                Check 'the ASM window shows the welcome of the Module' ([bool]$w) 'no answer in 2 minutes'
            }
            'CAS' { $code = RunWithFiles (Join-Path $root 'Apps\MMC\CAS\App\bin\Debug\net10.0-windows10.0.19041.0\CasApp.exe') @('--controller', $c, '--scene', (Join-Path $root 'Test\Data\Images\Animals\cat\01-pexels-32557420.jpg'), '--speech', (Join-Path $root 'Test\Data\question.wav'), '--quiet', '--out', $out) $app
                    Check 'CAS answers about the scene' ($code -eq 0 -and (Test-Path "$out\answer-1.wav")) }
            'CPS' { $code = RunWithFiles (Join-Path $root 'Apps\MMC\CPS\App\bin\Debug\net10.0-windows10.0.19041.0\CpsApp.exe') @('--controller', $c, '--face', (Join-Path $root 'Test\Data\Images\Faces\angry\01-pexels-34625421.jpg'), '--text', 'I am so excited about the concert tonight! Do you know what time it starts?', '--quiet', '--out', $out) $app
                    Check 'CPS answers the turn' ($code -eq 0 -and (Test-Path "$out\answer-1.wav")) }
            'CEC' { $code = RunWithFiles (Join-Path $root 'Apps\HMC\CEC\App\bin\Debug\net10.0-windows10.0.19041.0\CecApp.exe') @('--controller', $c, '--face', (Join-Path $root 'Test\Data\Images\Faces\happy\01-pexels-5510278.jpg'), '--text', 'I am so excited about the concert tonight! Do you know what time it starts?', '--from', 'en', '--to', 'it', '--quiet', '--out', $out) $app
                    Check 'CEC answers the turn in Italian' ($code -eq 0 -and (Test-Path "$out\avatar-1.json")) }    # N-50 (2026-11-25)
            'TMA' { $code = RunWithFiles (Join-Path $root 'Apps\OSD\TMA\App\bin\Debug\net10.0\TmaApp.exe') @('--controller', $c, '--video', (Join-Path $root 'Test\Data\TMA\AMI-IS1009c-10m00-15m00-side-by-side.mp4'), '--seconds', '20', '--out', $out) $app
                    Check 'TMA describes the programme' ($code -eq 0 -and (Test-Path "$out\event-descriptors.json")) }
            'LTP' {
                # Thalia's LTP waits for the Start button on its stage; the whole show is then run without the stage on the same Controller (the MAS instance, or its own), 2x fast:
                # every Cue the Script gives must be done in both venues (2026-11-26)
                Start-Sleep 20
                $r2 = @(Invoke-RestMethod "$base/api/runs" -TimeoutSec 10) | ForEach-Object { $_ } | Where-Object { $_.key -eq "${Where}:$app" }
                Check "$app is running 20 s later" ($r2.phase -eq 'running') $r2.phase
                # Thalia's LTP is stopped first: on the MAS instance every client of a Module shares its one instance, and the two would take each other's outputs
                Invoke-RestMethod -Method Post "$base/api/stop/$Where/$app" -TimeoutSec 30 | Out-Null; Start-Sleep 5
                $show = @('run', '--no-build', '--project', (Join-Path $root 'Apps\XRV\LTP\TwoMetaverses'), '--', '--no-video', '--scale', '0.5', '--out', $out)
                if ($c) { $show += @('--controller', $c) }
                $code = RunWithFiles 'dotnet' $show "$app-show"
                $acts = @(Select-String -Path (Join-Path $results "$app-show.out.txt") -Pattern 'Module -> ').Count   # the LongerEvening show: 7 Cues, an Action in each venue
                Check "the show runs to its end with every Cue done$(if ($c) { ' (Module on ' + $c + ')' })" ($code -eq 0 -and $acts -eq 14) ("$acts of 14 Actions | " + ((Get-Content (Join-Path $results "$app-show.out.txt") -Tail 3) -join ' | '))
            }
            'AUA' {
                # the door server and its page; the A-User is told to open the door when the bell rings, the Visitor rings, and the door must open (Night\n37\drive.ps1 drives it as the page does)
                $door = ($r.url -replace '/viewer/.*$', '')
                $ready = $false
                for ($i = 0; $i -lt 240 -and -not $ready; $i++) { try { $ready = ((Invoke-RestMethod "$door/door/log" -TimeoutSec 3).ready -eq $true) } catch { }; if (-not $ready) { Start-Sleep 1 } }
                Check 'the A-User is ready' $ready 'not ready in 4 minutes'
                if ($ready) {
                    $json = & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'Night\n37\drive.ps1') -Command 'When someone rings, open the door and say hello' -Sound bell -Base $door | Select-Object -Last 1
                    $o = try { $json | ConvertFrom-Json } catch { $null }
                    Check 'the A-User opens the door when the bell rings' ($o -and @($o.sounds)[0].opened) $json
                }
            }
            default {
                Start-Sleep 20
                $r2 = @(Invoke-RestMethod "$base/api/runs" -TimeoutSec 10) | ForEach-Object { $_ } | Where-Object { $_.key -eq "${Where}:$app" }
                Check "$app is running 20 s later" ($r2.phase -eq 'running') $r2.phase
                if ($r.url) { Check "the page of $app answers" ((& curl.exe -k -s -o NUL -w '%{http_code}' $r.url) -eq '200') }
                # a browser App: its Controller must build its Module (the page only shows the workflow)
                if ($r.url -match '/app/' -and $c -and $listed.module) { $why = ModuleStarts $c $listed.module; Check "the Controller starts the Module of $app ($($listed.module))" (-not $why) $why }
            }
        }
        $before = @(Kids $thalia.Id | Where-Object { $already -notcontains $_ })
        Invoke-RestMethod -Method Post "$base/api/stop/$Where/$app" -TimeoutSec 30 | Out-Null
        Start-Sleep 5
        $left = @($before | ForEach-Object { Get-Process -Id $_ -ErrorAction SilentlyContinue } | Where-Object { $_ -and $_.ProcessName -ne 'conhost' })
        Check "stopping $app stops what Thalia started" ($left.Count -eq 0) (($left | ForEach-Object { $_.ProcessName }) -join ', ')
        foreach ($p in $left) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
        StopOrphanHosts $startedAt
    }
} finally {
    foreach ($id in @(Kids $thalia.Id)) { Stop-Process -Id $id -Force -ErrorAction SilentlyContinue }
    Stop-Process -Id $thalia.Id -Force -ErrorAction SilentlyContinue
    if ($mas) { foreach ($id in @(Kids $mas.Id)) { Stop-Process -Id $id -Force -ErrorAction SilentlyContinue }; Stop-Process -Id $mas.Id -Force -ErrorAction SilentlyContinue }
    Start-Sleep 2; StopOrphanHosts $startedAt
    Pop-Location
}
Write-Host ''
if ($script:failed -eq 0) { Write-Host 'All checks passed.' -ForegroundColor Green } else { Write-Host "$script:failed check(s) failed." -ForegroundColor Red }
exit $script:failed
