# CLOSES AN MMM MEETING: the meeting server (MMM meeting, MMM group meeting, MMM world), its public address (the Cloudflare tunnel of AllApps\MMM\tools), the window that printed the links, and any
# Whisper left by a server that has ended. Kept: Ollama (the Secretary's language model, shared with other Apps) and the screen helper in the tray (Quit from its icon).
$all = @(Get-CimInstance Win32_Process)
$alive = @{}; foreach ($p in $all) { $alive[[int]$p.ProcessId] = $true }
$stop = @()
$stop += $all | Where-Object { $_.Name -eq 'dotnet.exe' -and $_.CommandLine -match 'Mpai\.Mmm\.Server' }                                         # the server (dotnet run and the server itself)
$stop += $all | Where-Object { $_.Name -eq 'cloudflared.exe' -and $_.CommandLine -match '\\MMM\\tools\\cloudflared' }                              # the public address
$stop += $all | Where-Object { $_.Name -eq 'powershell.exe' -and $_.CommandLine -match 'Run-(Meeting|Conference)\.ps1' }                         # the window with the links
$stop += $all | Where-Object { $_.Name -eq 'whisper-server.exe' -and -not $alive[[int]$_.ParentProcessId] }                                     # a Whisper whose server has ended
$stop = @($stop | Sort-Object ProcessId -Unique)
# The join page (Run-Meeting.ps1 -Join funnel): its Funnel path goes off the internet with the meeting, and meeting.json says the meeting is closed.
$ts = @('C:\Program Files\Tailscale\tailscale.exe') | Where-Object { Test-Path $_ } | Select-Object -First 1
if ($ts -and ((& $ts funnel status 2>&1 | Out-String) -match '/mmm')) { & $ts funnel --bg --set-path /mmm off 2>&1 | Out-Null; Write-Host 'Took the join page off the internet.' }
$joinJson = Join-Path $PSScriptRoot '..\MMM\Join\meeting.json'
if (Test-Path $joinJson) { [IO.File]::WriteAllText((Resolve-Path $joinJson), '{"Address":"","When":""}', (New-Object Text.UTF8Encoding($false))) }
if ($stop.Count -eq 0) { Write-Host 'No MMM meeting is running.'; exit 0 }
foreach ($p in $stop) {
    $what = switch ($p.Name) { 'cloudflared.exe' { 'the public address' } 'whisper-server.exe' { 'a leftover Whisper' } 'powershell.exe' { 'the window with the links' } default { 'the meeting server' } }
    try { Stop-Process -Id $p.ProcessId -Force -ErrorAction Stop; Write-Host "Stopped $what ($($p.ProcessId))." } catch { Write-Host "Could not stop $what ($($p.ProcessId)): $($_.Exception.Message)" }
}
