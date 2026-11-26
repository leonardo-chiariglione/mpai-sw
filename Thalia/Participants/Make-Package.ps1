# THALIA FOR THE MEETING PARTICIPANTS (2026-11-26) - a closed Thalia (Remote Profile: the Apps of MPAI as a Service only, and the person's MMM User) for Windows 64 bit, as a folder and a zip
# in Downloads. It holds Thalia, the App client, the avatar files, the seasonal pictures, and remote.json: the address of the participants' Service, THEIR TOKEN, and the join page of the MMM
# meeting. It needs no installation (no .NET, no Tailscale); it needs Edge. Start Thalia-Participants-Service.bat once first: it makes participants.json.
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$participants = Join-Path $env:LOCALAPPDATA 'Thalia\participants\participants.json'
if (-not (Test-Path $participants)) { throw 'Start Thalia-Participants-Service.bat once first: it makes participants.json (the address of the Service, the token, the join page).' }
$pkg = Join-Path $env:TEMP 'Thalia-MPAI'
if (Test-Path $pkg) { Remove-Item $pkg -Recurse -Force }
New-Item -ItemType Directory -Force $pkg | Out-Null

# NO PROGRAM OF OUR OWN (2026-11-26): an antivirus (AVG, IDP.HELU.PSD11) blocked the unsigned Thalia.exe that a self-contained publish makes. The package holds .dll files only, run by
# a copy of Microsoft's own dotnet.exe and the .NET runtime of this machine (runtime\: signed by Microsoft), so that nothing has to be installed and no unknown program is started.
dotnet publish (Join-Path $root 'Thalia\Core\Thalia.csproj') -c Release --no-self-contained -p:UseAppHost=false -o (Join-Path $pkg 'Thalia') -nologo -v q -clp:ErrorsOnly
if ($LASTEXITCODE) { throw 'publish of Thalia failed' }
dotnet publish (Join-Path $root 'AIF\UserAgent\Clients\Browser\Host\RcaWeb.Host.csproj') -c Release --no-self-contained -p:UseAppHost=false -o (Join-Path $pkg 'Host') -nologo -v q -clp:ErrorsOnly
if ($LASTEXITCODE) { throw 'publish of the App client failed' }
$dotnetRoot = Split-Path (Get-Command dotnet).Source
$version = (dotnet --list-runtimes | Select-String '^Microsoft\.AspNetCore\.App (10\.\S+)' | Select-Object -Last 1).Matches[0].Groups[1].Value
$rt = Join-Path $pkg 'runtime'
New-Item -ItemType Directory -Force (Join-Path $rt 'host\fxr'), (Join-Path $rt 'shared') | Out-Null
Copy-Item (Join-Path $dotnetRoot 'dotnet.exe') $rt
Copy-Item (Join-Path $dotnetRoot "host\fxr\$version") (Join-Path $rt 'host\fxr') -Recurse
foreach ($fw in 'Microsoft.NETCore.App', 'Microsoft.AspNetCore.App') {
    New-Item -ItemType Directory -Force (Join-Path $rt "shared\$fw") | Out-Null
    Copy-Item (Join-Path $dotnetRoot "shared\$fw\$version") (Join-Path $rt "shared\$fw") -Recurse
}
Get-ChildItem $rt -Recurse -Filter createdump.exe | Remove-Item -Force      # the runtime's crash-dump tool: not needed
$unsigned = @(Get-ChildItem $pkg -Recurse -Filter *.exe | Where-Object { (Get-AuthenticodeSignature $_.FullName).SignerCertificate.Subject -notmatch 'O=Microsoft Corporation' })
if ($unsigned) { throw "a program not signed by Microsoft is in the package: $($unsigned.Name -join ', ')" }

New-Item -ItemType Directory -Force (Join-Path $pkg 'AIF\UserAgent') | Out-Null
Copy-Item (Join-Path $root 'AIF\UserAgent\Assets') (Join-Path $pkg 'AIF\UserAgent\Assets') -Recurse
Copy-Item (Join-Path $root 'AIF\UserAgent\Orchestration') (Join-Path $pkg 'AIF\UserAgent\Orchestration') -Recurse
New-Item -ItemType Directory -Force (Join-Path $pkg 'Access\THL') | Out-Null
Copy-Item (Join-Path $root 'Access\THL\seasons') (Join-Path $pkg 'Access\THL\seasons') -Recurse
Copy-Item $participants (Join-Path $pkg 'remote.json')
Copy-Item (Join-Path $PSScriptRoot 'Thalia.bat') (Join-Path $pkg 'Thalia.bat')
Copy-Item (Join-Path $PSScriptRoot 'README.txt') (Join-Path $pkg 'README.txt')

$zip = Join-Path $env:USERPROFILE 'Downloads\Thalia-MPAI.zip'
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $pkg '*') -DestinationPath $zip
Write-Host "Package: $pkg"
Write-Host ("Zip:     $zip ({0:N0} MB)" -f ((Get-Item $zip).Length / 1MB))
