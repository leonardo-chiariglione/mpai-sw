# "OPEN THE DOOR WHEN THE BELL RINGS" (M3313), one command:
#   MMM\Run-Door.ps1
# Starts the M-Instance of the room and the door, with the Host User (an A-User App: User Agent + the A-User Module of
# PGM-AUA on the AIF Controller) and the Visitor, and opens its page in the default browser. On the page: "Enter the room",
# then send (or speak) the command when the page says "The A-User is ready.", then let the Visitor knock or ring.
# "New scene" on the page starts the scene again (door closed, Personae at their places, the A-User Module restarted).
# Ctrl+C stops it. -Llm: the language model of Basic Knowledge (Ollama).
param([string]$Llm = 'llama3.2:3b')

$url = 'http://127.0.0.1:7101'
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = 'C:\Program Files\dotnet\dotnet.exe' }
if (Get-NetTCPConnection -LocalPort 7101 -State Listen -ErrorAction SilentlyContinue) {
    Write-Host 'Port 7101 is in use: the door demonstration is already running. Stop it first (Ctrl+C in its window).'; exit 1
}
# The page opens as soon as the server answers (the first run builds the server first: it can take a minute).
Start-Job {
    for ($i = 0; $i -lt 240; $i++) { try { Invoke-WebRequest "$using:url/door/log" -UseBasicParsing -TimeoutSec 2 | Out-Null; break } catch { Start-Sleep -Milliseconds 500 } }
    Start-Process "$using:url/viewer/door.html"
} | Out-Null
& $dotnet run --project (Join-Path $PSScriptRoot 'Server\Mpai.Mmm.Server.csproj') -- $url --door --llm $Llm
