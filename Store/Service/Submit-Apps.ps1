<#
  Submit-Apps.ps1 - publish Apps to the MPAI Store as packages (an App is a Workflow written against one Module, with its descriptor and icon).

  Each App is a folder holding app.json, its Workflow (*.orch) and its icon; the package is made of those files and nothing else (not the stand-alone clients or the source that may lie beside them).
  The Store keeps every version it is given and never overwrites one: an App already published at its Version is reported and left.

  -Folder     where the Apps are (default: the Apps folder of the software, searched one level down: Apps\MMC\AMC, Apps\OSD\ACR ...)
  -Ids        which Apps, by folder name; default: every folder with an app.json and a Workflow
  -Store      the Store Service (default http://127.0.0.1:5020/)
  -AdminKey   the admin key of the Store (default: read from the Store's own folder, <local application data>\Thalia\Store\admin-key.txt)
#>
param(
    [string]$Folder = (Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path 'Apps'),
    [string[]]$Ids,
    [string]$Store = 'http://127.0.0.1:5020/',
    [string]$AdminKey
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$Store = $Store.TrimEnd('/') + '/'
$Ids = @($Ids | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
if (-not $AdminKey) {
    $file = Join-Path $env:LOCALAPPDATA 'Thalia\Store\admin-key.txt'
    if (-not (Test-Path $file)) { throw "No -AdminKey, and $file does not exist: start the Store once, or give the key." }
    $AdminKey = (Get-Content $file -Raw).Trim()
}
$apps = Get-ChildItem $Folder -Directory | ForEach-Object { Get-ChildItem $_.FullName -Directory } |
    Where-Object { (Test-Path (Join-Path $_.FullName 'app.json')) -and (Get-ChildItem $_.FullName -Filter *.orch -File) } |
    Where-Object { $Ids.Count -eq 0 -or $Ids -contains $_.Name }
foreach ($a in $apps) {
    $zipPath = Join-Path ([IO.Path]::GetTempPath()) ("mpai-app-" + $a.Name + ".zip")
    if (Test-Path $zipPath) { Remove-Item $zipPath }
    $zip = [IO.Compression.ZipFile]::Open($zipPath, 'Create')
    try {
        foreach ($f in Get-ChildItem $a.FullName -File | Where-Object { $_.Name -eq 'app.json' -or $_.Extension -in '.orch', '.svg', '.png', '.jpg' }) {
            [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $f.FullName, $f.Name)
        }
    } finally { $zip.Dispose() }
    try {
        $r = Invoke-RestMethod -Uri ($Store + 'MPAI/Store/App/' + $a.Name) -Method Post -Headers @{ 'MPAI-Admin' = $AdminKey } -InFile $zipPath -ContentType 'application/zip'
        Write-Host ("published {0} {1}" -f $r.id, $r.version)
    } catch {
        $body = if ($_.ErrorDetails.Message) { $_.ErrorDetails.Message } else { $_.Exception.Message }
        Write-Host ("{0}: {1}" -f $a.Name, $body) -ForegroundColor Yellow
    } finally { Remove-Item $zipPath -ErrorAction SilentlyContinue }
}
