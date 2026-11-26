# THE MPAI AS A SERVICE OF THE MEETING PARTICIPANTS (2026-11-26) - started on the desktop of the MMM Manager, for the Thalia given to the participants.
#
#   - An instance of MPAI as a Service of its own (port 5107, on this machine only), apart from the Manager's own (5105): the Apps whose client is the browser (no window of their own).
#   - It answers only a caller holding the participants' token (a file of this User on this machine, %LOCALAPPDATA%\Thalia\participants\token.txt; never in the repository).
#   - Tailscale Funnel puts it on the internet at https://<this machine's Tailscale name>:8443/ (Funnel's port 8443; the meeting's join page keeps 443).
#   - Closing this window stops the instance and takes it off the internet.
# participants.json (beside the token) holds what the participants' Thalia needs: the address, the token, and the join page of the MMM meeting. Make-Package.ps1 puts it in the package.
param([int]$Port = 5107, [int]$PublicPort = 8443)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$ts   = 'C:\Program Files\Tailscale\tailscale.exe'
if (-not (Test-Path $ts)) { throw 'Tailscale is not installed on this machine.' }
$status = & $ts status --json | ConvertFrom-Json
if ($status.BackendState -ne 'Running') { throw "Tailscale is not connected ($($status.BackendState)): open Tailscale and sign in." }
$name = $status.Self.DNSName.TrimEnd('.')

$dir = Join-Path $env:LOCALAPPDATA 'Thalia\participants'
New-Item -ItemType Directory -Force $dir | Out-Null
$tokenFile = Join-Path $dir 'token.txt'
if (-not (Test-Path $tokenFile)) {
    $b = New-Object byte[] 32; [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b)
    [IO.File]::WriteAllText($tokenFile, [Convert]::ToBase64String($b).TrimEnd('=').Replace('+', '-').Replace('/', '_'))
}
$token = (Get-Content $tokenFile -Raw).Trim()

# The configuration: this machine's own, with the participants' address and token, the Apps whose client is the browser, and no warm-up (the Manager's instance keeps the card warm).
$base = Get-Content (Join-Path $root 'Apps\MAS\Service\mas-server-AI.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$cfg = [ordered]@{}
foreach ($p in $base.PSObject.Properties) { if ($p.Name -ne 'WarmUpDirectory') { $cfg[$p.Name] = $p.Value } }
$cfg['ListenUrl']   = "http://127.0.0.1:$Port/"
$cfg['BearerToken'] = $token
$cfg['Apps']        = @('MTS', 'MCA', 'AMQ', 'NPR', 'AMC', 'ACR', 'MAC', 'TST', 'MQA')
$cfgFile = Join-Path $dir 'mas-server-participants.json'
$cfg | ConvertTo-Json -Depth 6 | Set-Content $cfgFile -Encoding UTF8

[ordered]@{ Service = "https://$($name):$PublicPort/"; Token = $token; MmmJoin = "https://$name/mmm/join.html" } | ConvertTo-Json | Set-Content (Join-Path $dir 'participants.json') -Encoding UTF8

$out = & $ts funnel --bg "--https=$PublicPort" "http://127.0.0.1:$Port" 2>&1 | Out-String
if ($out -match 'not enabled|error') { throw "Funnel could not publish the instance:`n$out" }
Write-Host ''
Write-Host "The participants' MPAI as a Service: https://$($name):$PublicPort/  (on this machine: http://127.0.0.1:$Port/)"
Write-Host 'Leave this window open during the meeting. Closing it stops the instance and takes it off the internet.'
Write-Host ''
# The program as built (the Manager's own instance may be running it: a build now would find its files in use).
$built = Join-Path $root 'Apps\MAS\Service\src\bin\Debug\net10.0\MasService.dll'
try { if (Test-Path $built) { dotnet $built $cfgFile } else { dotnet run --project (Join-Path $root 'Apps\MAS\Service\src\MasService.csproj') -- $cfgFile } }
finally { & $ts funnel "--https=$PublicPort" off 2>&1 | Out-Null; Write-Host 'Taken off the internet.' }
