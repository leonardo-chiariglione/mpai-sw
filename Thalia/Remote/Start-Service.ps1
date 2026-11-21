# THALIA REMOTE - the MPAI as a Service of THIS machine, offered to the User's own devices over Tailscale (a private, encrypted network).
#
#   - It listens on this machine's Tailscale address only (not on the local network, not on the internet).
#   - It presents a certificate for this machine's Tailscale name (made by "tailscale cert").
#   - It answers only a caller holding the User's token (a file of this User on this machine; it is never in the repository).
#
# The token and the certificate are in %LOCALAPPDATA%\Thalia\remote. Port 5106 (the local Service of Thalia keeps 5105 on this machine only).
param([int]$Port = 5106)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$ts   = 'C:\Program Files\Tailscale\tailscale.exe'
if (-not (Test-Path $ts)) { throw 'Tailscale is not installed on this machine (https://tailscale.com/download).' }

$status = & $ts status --json | ConvertFrom-Json
if ($status.BackendState -ne 'Running') { throw "Tailscale is not connected ($($status.BackendState)): open Tailscale and sign in." }
$name = $status.Self.DNSName.TrimEnd('.')
$ip   = ($status.Self.TailscaleIPs | Where-Object { $_ -notmatch ':' } | Select-Object -First 1)
if (-not $name -or -not $ip) { throw 'Tailscale gives this machine no name or address.' }

$dir = Join-Path $env:LOCALAPPDATA 'Thalia\remote'
New-Item -ItemType Directory -Force $dir | Out-Null
$crt = Join-Path $dir "$name.crt"; $key = Join-Path $dir "$name.key"
& $ts cert --cert-file $crt --key-file $key $name | Out-Null
if (-not (Test-Path $crt)) { throw "No certificate for ${name}: HTTPS certificates must be enabled for the tailnet (Tailscale admin console, DNS, HTTPS Certificates)." }

$tokenFile = Join-Path $dir 'token.txt'
if (-not (Test-Path $tokenFile)) {
    $b = New-Object byte[] 32; [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b)
    [IO.File]::WriteAllText($tokenFile, [Convert]::ToBase64String($b).TrimEnd('=').Replace('+','-').Replace('/','_'))
}
$token = (Get-Content $tokenFile -Raw).Trim()

# The Service's configuration is the one of this machine's own Service, with the address, the certificate and the token of the remote one.
$base = Get-Content (Join-Path $root 'Apps\MAS\Service\mas-server-AI.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$cfg = [ordered]@{}
foreach ($p in $base.PSObject.Properties) { $cfg[$p.Name] = $p.Value }
$cfg['ListenUrl']       = "https://$($ip):$Port/"
$cfg['CertificatePath'] = $crt
$cfg['PrivateKeyPath']  = $key
$cfg['BearerToken']     = $token
$cfgFile = Join-Path $dir 'mas-server-remote.json'
$cfg | ConvertTo-Json -Depth 6 | Set-Content $cfgFile -Encoding UTF8

# Written for the laptop: what it needs to reach this Service.
$remote = [ordered]@{ Service = "https://$($name):$Port/"; Token = $token }
$remote | ConvertTo-Json | Set-Content (Join-Path $dir 'remote.json') -Encoding UTF8

Write-Host ''
Write-Host "Thalia Remote Service: https://$($name):$Port/   (listening on $ip)"
Write-Host 'Leave this window open. Close it to stop the Service.'
Write-Host ''
dotnet run --project (Join-Path $root 'Apps\MAS\Service\src\MasService.csproj') -- $cfgFile
