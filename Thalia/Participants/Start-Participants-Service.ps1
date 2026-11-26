# THE MPAI AS A SERVICE OF THE MEETING PARTICIPANTS (2026-11-26) - started on the desktop of the MMM Manager, for the Thalia given to the participants.
#
#   - An instance of MPAI as a Service of its own (port 5107, on this machine only), apart from the Manager's own (5105): the Apps whose client is the browser (no window of their own).
#   - It answers only a caller holding the participants' token (a file of this User on this machine, %LOCALAPPDATA%\Thalia\participants\token.txt; never in the repository).
#   - TAILSCALE ONLY (the Manager's decision, 2026-11-26): "tailscale serve" offers it at https://<this machine's Tailscale name>:8443/ to the Tailscale network - the Manager's devices and
#     the people the Manager shared this machine with, whom the access rule of the tailnet lets reach ports 443 (the MMM) and 8443 (this) only. Nothing is on the public internet (no Funnel).
#   - Closing this window stops the instance and takes it off the Tailscale network.
# participants.json (beside the token) holds what the participants' Thalia needs: the address of this Service, the token, and the address of the MMM world. Make-Package.ps1 puts it in the package.
param([int]$Port = 5107, [int]$TailnetPort = 8443)
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

[ordered]@{ Service = "https://$($name):$TailnetPort/"; Token = $token; Mmm = "https://$name/" } | ConvertTo-Json | Set-Content (Join-Path $dir 'participants.json') -Encoding UTF8

$ErrorActionPreference = 'Continue'                                   # Tailscale writes to stderr when there is nothing to turn off: not an error
& $ts funnel "--https=$TailnetPort" off 2>&1 | Out-Null          # never on the public internet (an earlier version used Funnel)
$out = & $ts serve --bg "--https=$TailnetPort" "http://127.0.0.1:$Port" 2>&1 | Out-String
if ($out -match 'error') { throw "Tailscale could not offer the instance:`n$out" }
Write-Host ''
Write-Host "The participants' MPAI as a Service: https://$($name):$TailnetPort/ on the Tailscale network only  (on this machine: http://127.0.0.1:$Port/)"
Write-Host 'Leave this window open during the meeting. Closing it stops the instance and takes it off the Tailscale network.'
Write-Host ''
# The program as built (the Manager's own instance may be running it: a build now would find its files in use).
$built = Join-Path $root 'Apps\MAS\Service\src\bin\Debug\net10.0\MasService.dll'
try { if (Test-Path $built) { dotnet $built $cfgFile } else { dotnet run --project (Join-Path $root 'Apps\MAS\Service\src\MasService.csproj') -- $cfgFile } }
finally { & $ts serve "--https=$TailnetPort" off 2>&1 | Out-Null; Write-Host 'Taken off the Tailscale network.' }
