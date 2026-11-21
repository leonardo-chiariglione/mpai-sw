# THALIA REMOTE PACKAGE - a Thalia (Remote Profile) for the User's laptop (Windows, 64 bit), as a folder and a zip in Downloads.
# It holds: Thalia, the App client, the avatar files, the four seasonal pictures, and remote.json (the address of the Service and THE USER'S TOKEN).
# The package needs no installation of .NET on the laptop; it needs Tailscale (signed in with the same account) and Edge.
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$remoteJson = Join-Path $env:LOCALAPPDATA 'Thalia\remote\remote.json'
if (-not (Test-Path $remoteJson)) { throw 'Start Thalia-Service.bat once first: it makes remote.json (the address of the Service and the token).' }
$pkg = Join-Path $env:TEMP 'Thalia-Remote'
if (Test-Path $pkg) { Remove-Item $pkg -Recurse -Force }
New-Item -ItemType Directory -Force $pkg | Out-Null

dotnet publish (Join-Path $root 'Thalia\Core\Thalia.csproj') -c Release -r win-x64 --self-contained -o (Join-Path $pkg 'Thalia') -nologo -v q -clp:ErrorsOnly
if ($LASTEXITCODE) { throw 'publish of Thalia failed' }
dotnet publish (Join-Path $root 'AIF\UserAgent\Clients\Browser\Host\RcaWeb.Host.csproj') -c Release -r win-x64 --self-contained -o (Join-Path $pkg 'Host') -nologo -v q -clp:ErrorsOnly
if ($LASTEXITCODE) { throw 'publish of the App client failed' }

New-Item -ItemType Directory -Force (Join-Path $pkg 'AIF\UserAgent') | Out-Null
Copy-Item (Join-Path $root 'AIF\UserAgent\Assets') (Join-Path $pkg 'AIF\UserAgent\Assets') -Recurse
Copy-Item (Join-Path $root 'AIF\UserAgent\Orchestration') (Join-Path $pkg 'AIF\UserAgent\Orchestration') -Recurse
New-Item -ItemType Directory -Force (Join-Path $pkg 'Access\THL') | Out-Null
Copy-Item (Join-Path $root 'Access\THL\seasons') (Join-Path $pkg 'Access\THL\seasons') -Recurse
Copy-Item $remoteJson (Join-Path $pkg 'remote.json')
Copy-Item (Join-Path $PSScriptRoot 'Thalia-Remote.bat') (Join-Path $pkg 'Thalia-Remote.bat')
Copy-Item (Join-Path $PSScriptRoot 'README-Remote.txt') (Join-Path $pkg 'README.txt')

$zip = Join-Path $env:USERPROFILE 'Downloads\Thalia-Remote.zip'
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $pkg '*') -DestinationPath $zip
Write-Host "Package: $pkg"
Write-Host ("Zip:     $zip ({0:N0} MB)" -f ((Get-Item $zip).Length / 1MB))
