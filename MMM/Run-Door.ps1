# "OPEN THE DOOR WHEN THE BELL RINGS" (M3313), one command:
#   MMM\Run-Door.ps1
# Starts the M-Instance of the room and the door, with the Host User (an A-User App: User Agent + the A-User Module of
# PGM-AUA on the AIF Controller) and the Visitor, and opens its page in the default browser. On the page: "Enter the room",
# then send (or speak) the command, then let the Visitor knock or ring. The scene starts again only with a new run.
# Ctrl+C stops it. -Llm: the language model of Basic Knowledge (Ollama).
param([string]$Llm = 'llama3.2:3b')

$url = 'http://127.0.0.1:7101'
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = 'C:\Program Files\dotnet\dotnet.exe' }
if (Get-NetTCPConnection -LocalPort 7101 -State Listen -ErrorAction SilentlyContinue) {
    Write-Host 'Port 7101 is in use: the door demonstration is already running. Stop it first (Ctrl+C in its window).'; exit 1
}
Start-Job { Start-Sleep 8; Start-Process "$using:url/viewer/door.html" } | Out-Null
& $dotnet run --project (Join-Path $PSScriptRoot 'Server\Mpai.Mmm.Server.csproj') -- $url --door --llm $Llm
