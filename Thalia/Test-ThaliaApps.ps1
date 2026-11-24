<#
  Test-ThaliaApps.ps1 - one check per App that Thalia runs on this machine besides the browser Apps: Thalia starts the App, and the App does its job.

  It starts its own Thalia (port 5299) with its own folder of data: nothing of the User's Thalia, Store or Apps is touched (the Apps' storage is in that folder too).
    - AES, SME, SRS, EES, ARP, EAE, CPP: Thalia starts the App's local Controller; the App's own program is then run with files on that Controller (--controller) and must give its result.
    - ASM: Thalia starts the ASM Controller and window; the window must show the Module's welcome.
    - CAS, CPS, TMA: Thalia starts the App's Controller and its questions (dialogs nobody answers here); the App's program is then run with files on that Controller and must give its result.
    - AUA: Thalia starts the door server and its page; the A-User is ready, is told to open the door when the bell rings, the Visitor rings, and the door opens.
    - LTP, VMS: Thalia starts the program (LTP) or the server and its page (VMS); they must be running 20 s later (and the page must answer).
  Then Thalia stops the App, and what it started must be gone. Needs the Apps built (their launchers build them) and, for ASM, CAS and CPS, Ollama.

    powershell -ExecutionPolicy Bypass -File Thalia\Test-ThaliaApps.ps1 [-Apps AES,ASM,...] [-Data <folder>]
  (N-49, 2026-11-24)
#>
param([string[]]$Apps = @('AES', 'SME', 'SRS', 'EES', 'ARP', 'EAE', 'CPP', 'ASM', 'CAS', 'CPS', 'TMA', 'LTP', 'VMS', 'AUA'), [string]$Data = '')
$Apps = @($Apps | ForEach-Object { $_ -split ',' })
$ErrorActionPreference = 'Continue'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $Data) { $Data = Join-Path $env:USERPROFILE ('Downloads\Claude-work\thalia-apps-' + [guid]::NewGuid().ToString('N').Substring(0, 8)) }
$results = Join-Path $Data 'results'; New-Item -ItemType Directory -Force $Data, $results | Out-Null
$base = 'http://localhost:5299'
$script:failed = 0
function Check([string]$what, [bool]$ok, [string]$detail = '') {
    if ($ok) { Write-Host ("  ok    " + $what) -ForegroundColor Green } else { Write-Host ("  FAIL  " + $what + $(if ($detail) { ' - ' + $detail })) -ForegroundColor Red; $script:failed++ }
}
function Kids($top) { $all = Get-CimInstance Win32_Process; $set = @($top); $more = $true; while ($more) { $more = $false; foreach ($p in $all) { if ($set -contains $p.ParentProcessId -and -not ($set -contains $p.ProcessId)) { $set += $p.ProcessId; $more = $true } } }; $set | Select-Object -Skip 1 }
function RunWithFiles([string]$file, [string[]]$arguments, [string]$name) {
    $line = ($arguments | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }) -join ' '
    $p = Start-Process -FilePath $file -ArgumentList $line -NoNewWindow -PassThru -RedirectStandardOutput (Join-Path $results "$name.out.txt") -RedirectStandardError (Join-Path $results "$name.err.txt")
    $null = $p.Handle
    if (-not $p.WaitForExit(900000)) { Stop-Process -Id $p.Id -Force; return -1 }
    $p.WaitForExit(); return $p.ExitCode
}

Push-Location $root
& dotnet build Thalia\Core\Thalia.csproj -nologo -v q -o Thalia\Core\bin\Dev | Out-Null; if ($LASTEXITCODE) { throw 'The build of Thalia failed.' }
& dotnet build Thalia\Controller\ThaliaController.csproj -nologo -v q | Out-Null; if ($LASTEXITCODE) { throw 'The build of the local Controller failed.' }
$thalia = Start-Process dotnet -ArgumentList 'Thalia\Core\bin\Dev\Thalia.dll', '--Urls', $base, '--Apps', ('MTS,MCA,AMQ,NPR,' + ($Apps -join ',')), '--Data', "`"$Data`"", '--Store', 'http://127.0.0.1:5021/' `
    -WorkingDirectory $root -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $Data 'thalia.log') -RedirectStandardError (Join-Path $Data 'thalia.err')
try {
    for ($i = 0; $i -lt 60; $i++) { try { Invoke-RestMethod "$base/api/prefs" -TimeoutSec 2 | Out-Null; break } catch { Start-Sleep 1 } }
    $speech = Join-Path $root 'Test\Data\Speech\clip174.wav'; $model = Join-Path $root 'Test\Data\Speech\clip251.wav'
    $asmLog = Join-Path $root 'Apps\CAE\ASM\Ua\bin\Debug\net10.0-windows10.0.19041.0\asm-ua.log'
    foreach ($app in $Apps) {
        Write-Host $app -ForegroundColor Cyan
        $mark = @(Get-Content $asmLog -ErrorAction SilentlyContinue).Count
        try { Invoke-RestMethod -Method Post "$base/api/run/local/$app" -TimeoutSec 30 | Out-Null } catch { Check "Thalia runs $app" $false $_.Exception.Message; continue }
        $r = $null
        for ($i = 0; $i -lt 400; $i++) {
            Start-Sleep 1
            $r = @(Invoke-RestMethod "$base/api/runs" -TimeoutSec 10) | ForEach-Object { $_ } | Where-Object { $_.key -eq "local:$app" }
            if (-not $r -or $r.phase -eq 'running' -or $r.phase -like 'failed*' -or $r.phase -like 'ended*') { break }
        }
        Check "Thalia runs $app" ($r -and $r.phase -eq 'running') $r.phase
        if (-not $r -or $r.phase -ne 'running') { continue }
        $c = if ($r.controller) { $r.controller.TrimEnd('/') } else { '' }
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
            'TMA' { $code = RunWithFiles (Join-Path $root 'Apps\OSD\TMA\App\bin\Debug\net10.0\TmaApp.exe') @('--controller', $c, '--video', (Join-Path $root 'Test\Data\TMA\AMI-IS1009c-10m00-15m00-side-by-side.mp4'), '--seconds', '20', '--out', $out) $app
                    Check 'TMA describes the programme' ($code -eq 0 -and (Test-Path "$out\event-descriptors.json")) }
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
                $r2 = @(Invoke-RestMethod "$base/api/runs" -TimeoutSec 10) | ForEach-Object { $_ } | Where-Object { $_.key -eq "local:$app" }
                Check "$app is running 20 s later" ($r2.phase -eq 'running') $r2.phase
                if ($r.url) { Check "the page of $app answers" ((& curl.exe -k -s -o NUL -w '%{http_code}' $r.url) -eq '200') }
            }
        }
        $before = @(Kids $thalia.Id)
        Invoke-RestMethod -Method Post "$base/api/stop/local/$app" -TimeoutSec 30 | Out-Null
        Start-Sleep 5
        $left = @($before | ForEach-Object { Get-Process -Id $_ -ErrorAction SilentlyContinue } | Where-Object { $_ -and $_.ProcessName -ne 'conhost' })
        Check "stopping $app stops what Thalia started" ($left.Count -eq 0) (($left | ForEach-Object { $_.ProcessName }) -join ', ')
        foreach ($p in $left) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
    }
} finally {
    foreach ($id in @(Kids $thalia.Id)) { Stop-Process -Id $id -Force -ErrorAction SilentlyContinue }
    Stop-Process -Id $thalia.Id -Force -ErrorAction SilentlyContinue
    Pop-Location
}
Write-Host ''
if ($script:failed -eq 0) { Write-Host 'All checks passed.' -ForegroundColor Green } else { Write-Host "$script:failed check(s) failed." -ForegroundColor Red }
exit $script:failed
