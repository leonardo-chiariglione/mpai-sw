# A MEETING IN THE MMM WORLD: the guests install nothing, each opens a personal link.
#   AllApps\_support\Run-Meeting.ps1 -Chair Leonardo -Guests Anna,Bruno,Carla -Until 19:00
#   AllApps\_support\Run-Meeting.ps1 -Chair Leonardo -Guests Anna,Bruno,Carla -Via cloudflare
# It starts the M-Instance with its world (the Metaverse Square, the MPAI building: a public atrium and the wings of rooms) and puts it on the internet at an https address (the microphone needs
# https): -Via funnel (Tailscale Funnel, a stable address on this PC's Tailscale name; needs Tailscale logged in, Serve and Funnel enabled in its console) or -Via cloudflare (the default: a Cloudflare
# quick tunnel: no account, a new address each time; cloudflared.exe).
# THE CHAIR INVITES: each guest gets a personal link (?invite=...) that this script prints. The server turns it into the guest's name; the guests listed are the ones given the Rights to the wing of the
# meeting rooms and to the MMM meeting room, for the meeting - the rights end with it. Without the link nobody is answered; with -Visitors anybody may come into the atrium (public), and no further.
# You, on this PC: the address http://127.0.0.1:7099 - you are the chair. Close the server window when the meeting is over (the address goes with it).
# A GROUP'S MEETING: -Group MMM -When "Thursday 22 October, 17:00 CEST" takes the people of the group from AllApps\MMM\Registry\people.json (see Registry\README.md): the chair is the one who chairs the group, the others
# are the guests, each is sent an email with the personal link, composed through Outlook. -Mail ask (default): the recipients are shown and you are asked to confirm before anything is sent;
# draft: the emails are saved as Outlook drafts, nothing is sent; send: they are sent without asking (only when you have said so); none: no emails.
# -Stay: the script does not wait for Enter at the end and leaves the server and the tunnel running (to be started a day before the meeting); close the server window and end cloudflared when it is over.
# -Written: no speech recognition (Whisper) and no language model (Ollama): the participants write their requests to the Secretary (the requests of the list); no minutes, no summary.
# Without -Written (spoken): Whisper, the language model (-Llm, default llama3.2:3b, which fits on a 4 GB card; it is loaded before the server starts) and the voices.
# -Join funnel (the default): the emails link to the join page AllApps\MMM\Join\join.html, published at this PC's stable Tailscale address (Funnel, only that folder), which sends each guest on to
# the meeting's own address; mail filters distrust trycloudflare.com links (2026-11-18). The meeting itself does not go through Funnel (too slow for the avatars). -Join none: the emails carry the meeting's address.
# -Officers A,B: the people, besides the chair, who may register with the MPAI Officer Account Level (the Rights to every place of the building).
param([string]$Chair = '', [string]$Guests = '', [string]$Until = '', [double]$Hours = 3, [int]$Port = 7099, [ValidateSet('funnel', 'cloudflare')][string]$Via = 'cloudflare', [switch]$Visitors,
      [string]$Group = '', [string]$When = '', [string]$Officers = '', [string]$HostName = '', [ValidateSet('ask', 'draft', 'send', 'none')][string]$Mail = 'ask', [switch]$Preview, [switch]$Stay, [switch]$Written, [ValidateSet('funnel', 'none')][string]$Join = 'funnel', [string]$Llm = 'llama3.2:3b')

$here = (Resolve-Path (Join-Path $PSScriptRoot "..\MMM")).Path
$local = "http://127.0.0.1:$Port"
$people = @{}      # the name in the M-Instance -> the person (Email, Name), for the emails
if ($Group) {
    $groups = @(Get-Content "$here\Registry\groups.json" -Raw -Encoding UTF8 | ConvertFrom-Json | ForEach-Object { $_ })
    $grp = $groups | Where-Object { $_.Id -eq $Group } | Select-Object -First 1
    if (-not $grp) { Write-Host "The group $Group is not in Registry\groups.json."; exit 1 }
    if ($grp.Room -ne 'MeetingRoom') { Write-Host "The meeting room of $Group ($($grp.Room)) has no meeting set up yet: only the MMM room has seats and rights. Stopping."; exit 1 }
    $all = @(Get-Content "$here\Registry\people.json" -Raw -Encoding UTF8 | ConvertFrom-Json | ForEach-Object { $_ } | Where-Object { $_.Groups -contains $Group })
    if (-not $all) { Write-Host "Nobody is in the group $Group in Registry\people.json."; exit 1 }
    # The name in the M-Instance: letters only, the first name by default, made unique.
    $used = @{}
    foreach ($p in $all) {
        $id = if ($p.Id) { $p.Id } else { (([string]($p.Name -split '\s+')[0]).Normalize([Text.NormalizationForm]::FormD)) -replace '\p{Mn}', '' -replace '[^A-Za-z]', '' }
        if (-not $id) { $id = 'Guest' }
        $base = $id; $k = 2; while ($used.ContainsKey($id)) { $id = "$base$k"; $k++ }
        $used[$id] = $true; $people[$id] = $p
    }
    # THE HOST is whoever runs the meeting on this PC: the one with Role President in the list (the Officer of every place). THE CHAIR is who convenes this group's meeting: the one marked (C) in
    # the list - a person with a link of their own, if it is not the host - or, if nobody is, the host.
    if (-not $HostName) { $HostName = @($people.Keys | Where-Object { $people[$_].Role -eq 'President' })[0] }
    if (-not $HostName) { $HostName = Read-Host 'Who is the host, on this PC (the President)? The name as in the list' }
    $chairs = @($people.Keys | Where-Object { $people[$_].Chair -contains $Group -and $people[$_].Role -ne 'President' })
    if (-not $Chair) { $Chair = if ($chairs) { $chairs[0] } else { $HostName } }
    if (-not $Guests) { $Guests = ($people.Keys | Where-Object { $_ -ne $HostName } | Sort-Object) -join ',' }
}
if (-not $Chair) { $Chair = Read-Host 'Your name in the meeting (the chair)' }
if (-not $HostName) { $HostName = $Chair }      # after the chair is known: asked at the prompt, it was empty here and the host had no name
if (-not $Guests) { $Guests = Read-Host 'The guests, their names separated by commas (for example Anna,Bruno,Carla)' }
$names = @($Guests -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ -and $_ -ne $HostName })
if ($names.Count -lt 1) { Write-Host 'No guests.'; exit 1 }

# A personal token for each guest: 18 hexadecimal characters, from the system's random numbers.
$tokens = @{}
foreach ($n in $names) { $b = New-Object byte[] 9; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b); $tokens[$n] = ([BitConverter]::ToString($b) -replace '-', '').ToLower() }
# the host has a link of its own too, for another device (a laptop away from this PC); on this PC it needs none
$b = New-Object byte[] 9; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b); $hostToken = ([BitConverter]::ToString($b) -replace '-', '').ToLower()
$invites = (@($names | ForEach-Object { "$_=$($tokens[$_])" }) + "$HostName=$hostToken") -join ','      # @(): with ONE guest the pipeline gives a string, and + would glue the host's token onto it

function Get-Mails($base, $joinAt = $null) {
    $subject = "MPAI $($grp.Id) meeting" + $(if ($When) { " - $When" } else { '' })
    @(foreach ($n in $names) {
        $p = $people[$n]; if (-not $p -or -not $p.Email) { continue }
        $link = if ($joinAt) { "$joinAt/join.html?invite=$($tokens[$n])" } else { "$base/viewer/?world=1&mic=1&invite=$($tokens[$n])" }
        $body = @"
Dear $(($p.Name -split '\s+')[0]),

you are invited to the $($grp.Name) meeting$(if ($When) { " on $When" }), in the MMM virtual world: the MPAI building, the meeting room of the group.

$(if ($n -eq $Chair) { 'You chair the meeting: you can share your screen, and end the meeting for everybody.' + [Environment]::NewLine + [Environment]::NewLine })Your personal link (it is yours: please do not pass it on):

$link

What you need: a PC with Chrome or Edge, and a headset (no loudspeakers: the room has its own echo). Open the link. The first time you register: keep the Account Level *Participant*, give your first name, last name and email, and choose your avatar. Allow the microphone when the browser asks.
You move by clicking the point you want to reach: your avatar walks there, through the doors of the MPAI building to the meeting rooms, and into the MMM meeting room, where you sit down by yourself.
The mouse wheel turns you: wheel forward turns you counterclockwise, wheel backward clockwise. Ctrl + wheel forward zooms in, Ctrl + wheel backward zooms out.
Q stands up, E sits again.
$(if ($joinAt) { 'The link opens the meeting while it is on; before, it says when the meeting is.' } else { 'The link works while the meeting is on; the address changes for each meeting.' })

$($HostName)
"@
        [pscustomobject]@{ To = $p.Email; Name = $n; Subject = $subject; Body = $body }
    })
}

if ($Preview) {
    Write-Host "Group $Group - chair: $Chair; guests: $($names -join ', ')"
    foreach ($m in (Get-Mails 'https://(the address of the meeting)' $(if ($Join -eq 'funnel' -and $Via -ne 'funnel') { 'https://(this PC''s Tailscale name)/mmm' }))) { Write-Host ('-' * 70); Write-Host "To: $($m.To)   Subject: $($m.Subject)"; Write-Host $m.Body }
    exit 0
}

if (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue) { Write-Host "Port $Port is in use: a server is already running. Close its window first."; exit 1 }

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = 'C:\Program Files\dotnet\dotnet.exe' }
$period = if ($Until) { "--until $Until" } else { "--hours $Hours" }      # not $when: PowerShell names ignore case, and $when was $When, the date the emails give (they said "--hours 3")
$arguments = "run --project `"$here\Server\Mpai.Mmm.Server.csproj`" -c Release -- $local --world --identity --chair $Chair --host $HostName --invites $invites --invite $((@($Chair) + @($HostName) + $names | Select-Object -Unique) -join ',') $period" + $(if ($Visitors) { ' --visitors' } else { '' }) + $(if ($Officers) { " --officers $Officers" } else { '' }) + $(if ($Written) { ' --no-asr --no-summary' } else { " --llm $Llm" })
# SPOKEN (without -Written): the language model of the Secretary and of the summary is loaded on the card now (the first load takes about 20 s), so the first request is answered at once.
if (-not $Written) {
    Write-Host "Loading the language model $Llm..."
    try { $null = Invoke-RestMethod -Uri 'http://127.0.0.1:11434/api/generate' -Method Post -ContentType 'application/json' -TimeoutSec 120 -Body (@{ model = $Llm; prompt = 'hi'; stream = $false; keep_alive = '6h'; options = @{ num_predict = 1 } } | ConvertTo-Json -Compress); Write-Host 'The language model is ready.' }
    catch { Write-Host "Ollama did not load $Llm ($($_.Exception.Message)): the Secretary will understand only the written requests until it does." }
}
# Compiled HERE, in this window, so that an error shows (2026-11-19: the server's own window closed during its build and took the message with it); then started without building, in a
# window that stays open if the server stops (cmd /k), so that its last message can be read.
Write-Host 'Compiling the server (the first time about a minute)...'
& $dotnet build "$here\Server\Mpai.Mmm.Server.csproj" -c Release -v q -nologo -clp:ErrorsOnly
if ($LASTEXITCODE -ne 0) { Write-Host 'The server did not compile: the errors are above.'; exit 1 }
Start-Process -FilePath 'cmd.exe' -ArgumentList "/k `"`"$dotnet`" $($arguments -replace '^run ', 'run --no-build ')`"" -WindowStyle Normal
Write-Host 'The server starts...'
$up = $false
for ($i = 0; $i -lt 180 -and -not $up; $i++) {
    Start-Sleep 2
    try { Invoke-WebRequest "$local/viewer/" -UseBasicParsing -TimeoutSec 3 | Out-Null; $up = $true } catch { if ($_.Exception.Response) { $up = $true } }
}
if (-not $up) { Write-Host 'The server did not start. Look at its window.'; exit 1 }

# THE SCREEN HELPER on this PC (the one the chair shares from): it shares a window with no browser "Stop sharing" bar. Started here, so nobody has to find it; it stays in the tray until Quit.
$helperDll = Join-Path $here 'ScreenHelper\bin\Release\net10.0-windows\Mpai.Mmm.ScreenHelper.dll'
$helperUp = $false
try { $helperUp = [bool](Invoke-RestMethod 'http://127.0.0.1:7391/status' -TimeoutSec 2).helper } catch { }
if (-not $helperUp) {
    if (-not (Test-Path $helperDll)) { Write-Host 'Building the screen helper (once)...'; & $dotnet build "$here\ScreenHelper\Mpai.Mmm.ScreenHelper.csproj" -c Release -v q | Out-Null }
    if (Test-Path $helperDll) { Start-Process -FilePath $dotnet -ArgumentList "`"$helperDll`"" -WindowStyle Hidden; Write-Host 'Screen helper started (tray icon).' }
    else { Write-Host 'The screen helper could not be built: sharing will use the browser capture, with its own "Stop sharing" bar.' }
}

$base = $null
if ($Via -eq 'funnel') {
    $ts = (Get-Command tailscale -ErrorAction SilentlyContinue).Source
    if (-not $ts) { $ts = @('C:\Program Files\Tailscale\tailscale.exe') | Where-Object { Test-Path $_ } | Select-Object -First 1 }
    if (-not $ts) { Write-Host 'Tailscale is not installed. Use -Via cloudflare.'; exit 1 }
    $out = & $ts funnel --bg $Port 2>&1 | Out-String
    $dns = $null
    try { $dns = ((& $ts status --json | ConvertFrom-Json).Self.DNSName).TrimEnd('.') } catch { }
    if (-not $dns -or $out -match 'not enabled') { Write-Host "Funnel is not enabled on the tailnet:`n$out`nEnable it (the address above), or use -Via cloudflare."; exit 1 }
    $base = "https://$dns"
}
else {
    $cf = @("$here\tools\cloudflared.exe", "$here\..\..\Apps\ABV\AVS\tools\cloudflared.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $cf) { $cf = (Get-Command cloudflared -ErrorAction SilentlyContinue).Source }
    if (-not $cf) { Write-Host 'cloudflared.exe is not found (AllApps\MMM\tools\, Apps\ABV\AVS\tools\).'; exit 1 }
    $log = Join-Path $env:TEMP 'mmm-tunnel.log'
    Remove-Item $log -ErrorAction SilentlyContinue
    Start-Process -FilePath $cf -ArgumentList 'tunnel', '--no-autoupdate', '--url', $local -RedirectStandardError $log -WindowStyle Minimized
    for ($i = 0; $i -lt 40 -and -not $base; $i++) {
        Start-Sleep 1.5
        if (Test-Path $log) { $m = [regex]::Match((Get-Content $log -Raw -ErrorAction SilentlyContinue), 'https://[a-z0-9-]+\.trycloudflare\.com'); if ($m.Success) { $base = $m.Value } }
    }
    if (-not $base) { Write-Host 'The tunnel did not give an address. Look at the log:' $log; exit 1 }
}

# THE JOIN PAGE (-Join funnel): Funnel forwards /mmm of this PC's Tailscale name to /join of the meeting server, which answers there only join.html and the meeting.json written here
# (AllApps\MMM\Join). Funnel serves a folder only for a Windows administrator; it forwards to a port for anybody. The emails link there.
$joinUrl = $null      # not $join: PowerShell names ignore case, and $join is $Join, the option (funnel or none), which refuses an address
function Set-JoinMeeting($address) {
    $json = [ordered]@{ Address = $address; When = $When } | ConvertTo-Json -Compress
    [IO.File]::WriteAllText((Join-Path $here 'Join\meeting.json'), $json, (New-Object Text.UTF8Encoding($false)))
}
if ($Join -eq 'funnel' -and $Via -ne 'funnel') {
    $tsj = (Get-Command tailscale -ErrorAction SilentlyContinue).Source
    if (-not $tsj) { $tsj = @('C:\Program Files\Tailscale\tailscale.exe') | Where-Object { Test-Path $_ } | Select-Object -First 1 }
    $dnsj = $null
    if ($tsj) { try { $dnsj = ((& $tsj status --json | ConvertFrom-Json).Self.DNSName).TrimEnd('.') } catch { } }
    if ($dnsj) {
        Set-JoinMeeting $base
        $out = & $tsj funnel --bg --set-path /mmm "http://127.0.0.1:$Port/join" 2>&1 | Out-String
        $ok = $false
        for ($i = 0; $i -lt 10 -and -not $ok; $i++) { try { $ok = (Invoke-WebRequest "https://$dnsj/mmm/meeting.json" -UseBasicParsing -TimeoutSec 5).Content -match [regex]::Escape($base) } catch { Start-Sleep 1 } }
        if ($ok) { $joinUrl = "https://$dnsj/mmm"; Write-Host "  The join page is up: $joinUrl/join.html (the emails link there)." }
        else { Write-Host "  The join page could not be published (Funnel): the emails carry the meeting's address.`n$out" }
    }
    else { Write-Host "  Tailscale is not running: no join page; the emails carry the meeting's address." }
}

Write-Host ''
Write-Host '  For each guest - send them THEIR OWN link (Chrome or Edge on a PC, a headset, allow the microphone):' -ForegroundColor Green
foreach ($n in $names) { Write-Host ("    {0,-12} {1}/viewer/?world=1&mic=1&invite={2}" -f $n, $base, $tokens[$n]) }
Write-Host "    the meeting ends: $(if ($Until) { 'at ' + $Until } else { 'in ' + $Hours + ' hours' }), or when you press the button (bottom left); the rights of everybody end with it"
Write-Host ''
Write-Host '  For you, on this PC:' -ForegroundColor Green
Write-Host "    $local/viewer/?world=1&mic=1&user=$HostName"
Write-Host "  For you, on another device (the same person - use it on one device or the other, not both at once): $base/viewer/?world=1&mic=1&invite=$hostToken" -ForegroundColor Yellow
Write-Host ''
Write-Host '  The first time, each chooses a Persona. Click the point to walk to - the MPAI doors, across the atrium to the south wing: the MMM meeting room is the first on the right. The wheel turns you, Ctrl + wheel zooms. Q stands up, E sits.'
# THE EMAILS: one for each guest of the group, with the personal link, through Outlook (the account of this PC): shown first, sent only if you confirm - or saved as drafts.
if ($Group -and $Mail -ne 'none') {
    $mails = @(Get-Mails $base $joinUrl)
    Write-Host ''
    Write-Host "  Emails to send ($($mails.Count)), from your Outlook:" -ForegroundColor Green
    foreach ($m in $mails) { Write-Host ("    {0,-10} {1}" -f $m.Name, $m.To) }
    $do = $Mail
    if ($Mail -eq 'ask') {
        $ans = Read-Host "Send these $($mails.Count) emails now? (y = send, d = save as drafts, anything else = no)"
        $do = if ($ans -match '^[yY]') { 'send' } elseif ($ans -match '^[dD]') { 'draft' } else { 'none' }
    }
    if ($do -ne 'none' -and $mails.Count -gt 0) {
        try {
            $ol = New-Object -ComObject Outlook.Application
            foreach ($m in $mails) {
                $item = $ol.CreateItem(0); $item.To = $m.To; $item.Subject = $m.Subject; $item.Body = $m.Body
                if ($do -eq 'send') { $item.Send() } else { $item.Save() }
            }
            Write-Host "  $($mails.Count) emails $(if ($do -eq 'send') { 'sent' } else { 'saved as drafts in Outlook' })."
        }
        catch { Write-Host "  Outlook could not make the emails: $($_.Exception.Message)`n  The links above are what each guest needs." }
    }
}
# THE LINKS ON TOP: the server window and the browser open over this window; it is brought to the front (made topmost and released, so it does not stay above everything).
function Show-OnTop {
    try {
        Add-Type -Namespace Mmm -Name Win -MemberDefinition @'
[DllImport("kernel32.dll")] public static extern IntPtr GetConsoleWindow();
[DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
[DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
'@ -ErrorAction SilentlyContinue
        $w = [Mmm.Win]::GetConsoleWindow()
        if ($w -eq [IntPtr]::Zero) { return }
        [Mmm.Win]::ShowWindow($w, 9) | Out-Null                                              # 9: restore, if minimised
        [Mmm.Win]::SetWindowPos($w, [IntPtr](-1), 0, 0, 0, 0, 0x43) | Out-Null               # topmost (no move, no size, show)
        [Mmm.Win]::SetWindowPos($w, [IntPtr](-2), 0, 0, 0, 0, 0x43) | Out-Null               # released: in front, not pinned
        [Mmm.Win]::SetForegroundWindow($w) | Out-Null
    } catch { }
}
if ($Stay) { Show-OnTop; Write-Host '  The server and the tunnel stay up (-Stay).'; exit 0 }
Start-Process "$local/viewer/?world=1&mic=1&user=$HostName"
Start-Sleep 4                                                                                 # the browser takes the front when it opens; the links come back over it
Show-OnTop
Read-Host 'Enter to take the meeting off the internet (the server has its own window)' | Out-Null
if ($Via -eq 'funnel') { & $ts funnel reset | Out-Null } else { Get-Process cloudflared -ErrorAction SilentlyContinue | Stop-Process -Force }
if ($joinUrl) { Set-JoinMeeting ''; & $tsj funnel --bg --set-path /mmm off 2>&1 | Out-Null }      # the join page goes off the internet with the meeting
