using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

// THE PERSON'S MMM USER (2026-11-26). Thalia is the person's own User in the MMM world: it keeps the person's Account - the address of the M-Instance (the personal link the chair
// sent, or the world's address), the person's data, the Account Level and the password - and registers or enters for the person (MMM-TEC Register: a human registers with its
// credentials, and enters with them). It then opens the world in the browser with the User's token after #mmm-token= (the part of an address that is never sent to a server);
// the page takes it and removes it. The password is kept encrypted for this Windows user (DPAPI), never in clear, and goes only to the M-Instance named.
static class MmmAccount
{
    public static void Map(WebApplication app, string thaliaData, string? defaultAddress = null, bool autoEnter = false)
    {
        var file = Path.Combine(thaliaData, "mmm-account.json");
        JsonObject Read() { try { return File.Exists(file) ? JsonNode.Parse(File.ReadAllText(file))!.AsObject() : new JsonObject(); } catch { return new JsonObject(); } }

        app.MapGet("/api/mmm/account", () =>
        {
            var a = Read(); var p = UserProfile.Read(thaliaData);
            return Results.Json(new JsonObject
            {
                ["address"] = (string?)a["address"] ?? defaultAddress, ["first"] = (string?)p["first"], ["last"] = (string?)p["last"], ["organisation"] = (string?)p["organisation"],
                ["email"] = (string?)p["email"], ["hasPassword"] = a["password"] is not null, ["user"] = (string?)a["user"],
                ["linked"] = a["device"] is not null, ["linkPending"] = a["linkRequest"] is not null, ["linkResult"] = (string?)a["linkResult"]
            });
        });

        app.MapPost("/api/mmm/account", async (HttpContext ctx) =>
        {
            var given = JsonNode.Parse(await new StreamReader(ctx.Request.Body).ReadToEndAsync()) as JsonObject ?? new JsonObject();
            var a = Read();
            foreach (var k in new[] { "address", "first", "last", "organisation", "email" }) if (given[k] is not null) a[k] = ((string?)given[k])?.Trim();      // the role is chosen in the MMM (2026-11-27)
            if ((string?)given["password"] is { Length: > 0 } pw) a["password"] = Convert.ToBase64String(Protect(Encoding.UTF8.GetBytes(pw)));
            Directory.CreateDirectory(thaliaData);
            await File.WriteAllTextAsync(file, a.ToJsonString());
            return Results.Json(new { ok = true });
        });

        // REGISTER, for the person: the Account is opened (201), or the person has one already (200); nothing is opened. ENTER: the same, then the address that opens the world, entered.
        app.MapPost("/api/mmm/register", () => RegisterOrEnter(false));
        app.MapPost("/api/mmm/enter", (string? launch) => RegisterOrEnter(true, launch == "1"));      // not (HttpContext ctx) =>: that is taken for a raw handler, and the answer is lost
        // ENTERING BY ITSELF (2026-11-26; every Thalia since the MMM tile was removed): once the person has entered the MMM once, Thalia enters it when it starts and opens the world.
        if (autoEnter)
            app.Lifetime.ApplicationStarted.Register(() => _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(3));
                var a = Read();
                if (a["user"] is not null && (UserProfile.Password(UserProfile.Read(thaliaData)) is not null || a["password"] is not null || a["device"] is not null) && (bool?)a["autoEnter"] != false)
                    try { await RegisterOrEnter(true, true); } catch (Exception e) { Console.WriteLine("entering the MMM: " + e.Message); }
            }));
        async Task<(Uri? Link, string? Problem)> WorldOf(JsonObject a)
        {
            if (((string?)a["address"] is { Length: > 0 } given ? given : defaultAddress) is not { Length: > 0 } address || !Uri.TryCreate(address, UriKind.Absolute, out var link)) return (null, "The address of the M-Instance is not set.");
            // THE JOIN PAGE (a stable address, AllApps\MMM\Join): it names the address of the meeting now (meeting.json), which changes from one meeting to the next.
            if (link.AbsolutePath.EndsWith("/join.html", StringComparison.OrdinalIgnoreCase) || link.AbsolutePath.TrimEnd('/').EndsWith("/mmm", StringComparison.OrdinalIgnoreCase))
            {
                var folder = link.AbsolutePath.EndsWith("/join.html", StringComparison.OrdinalIgnoreCase) ? link.AbsolutePath[..^"join.html".Length] : link.AbsolutePath.TrimEnd('/') + "/";
                try
                {
                    using var j = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                    var meeting = JsonNode.Parse(await j.GetStringAsync(new Uri(link, folder + "meeting.json"))) as JsonObject;
                    if ((string?)meeting?["Address"] is not { Length: > 0 } now) return (null, $"The meeting is not on now{((string?)meeting?["When"] is { Length: > 0 } w ? ": it is " + w : "")}.");
                    link = new Uri(now.TrimEnd('/') + "/viewer/?world=1&mic=1");
                }
                catch (Exception e) { return (null, "The meeting is not on now (its join page does not answer: " + e.Message + ")."); }
            }
            return (link, null);
        }

        // LINKING THIS THALIA TO AN ACCOUNT THAT EXISTS (2026-11-26): Thalia asks the M-Instance for a link and shows its code; the person, entered in the world with its password, approves
        // it there (the world opens on the request); the M-Instance gives this Thalia a key of its own, which Thalia keeps encrypted for this Windows user and enters with - the password
        // is never typed into Thalia. The person removes the link in the world (its list of devices) at any time.
        app.MapPost("/api/mmm/link", async (string? launch) =>
        {
            var a = Read();
            var (link, problem) = await WorldOf(a);
            if (link is null) return Results.Json(new { ok = false, text = problem });
            var origin = link.GetLeftPart(UriPartial.Authority);
            using var http = new HttpClient { BaseAddress = new Uri(origin), Timeout = TimeSpan.FromSeconds(20) };
            JsonObject? r;
            try
            {
                using var rs = await http.PostAsync("/api/link/request", new StringContent(new JsonObject { ["device"] = $"Thalia on {Environment.MachineName}" }.ToJsonString(), Encoding.UTF8, "application/json"));
                r = JsonNode.Parse(await rs.Content.ReadAsStringAsync()) as JsonObject;
            }
            catch (Exception e) { return Results.Json(new { ok = false, text = "The M-Instance does not answer: " + e.Message }); }
            if ((string?)r?["id"] is not { } id) return Results.Json(new { ok = false, text = "This M-Instance does not link devices (its software is older)." });
            a["linkRequest"] = id; a.Remove("linkResult"); Directory.CreateDirectory(thaliaData); await File.WriteAllTextAsync(file, a.ToJsonString());
            var url = $"{origin}/viewer/?world=1&mic=1&link={id}";
            if (launch == "1") OpenWorld(url);
            _ = Task.Run(() => WaitForLink(origin, id));
            return Results.Json(new { ok = true, code = (string?)r["code"], url, opened = launch == "1",
                text = $"In the world window, enter with your password and approve the link. The code to check: {(string?)r["code"]}." });
        });
        app.MapPost("/api/mmm/unlink", async () =>
        {
            var a = Read(); a.Remove("device"); a.Remove("linkRequest"); await File.WriteAllTextAsync(file, a.ToJsonString());
            return Results.Json(new { ok = true, text = "This Thalia forgot its key. Remove it also from the list of your devices in the world." });
        });
        async Task WaitForLink(string origin, string id)
        {
            using var http = new HttpClient { BaseAddress = new Uri(origin), Timeout = TimeSpan.FromSeconds(15) };
            for (var i = 0; i < 330; i++)                                     // 11 minutes: a request lasts 10
            {
                await Task.Delay(TimeSpan.FromSeconds(2));
                JsonObject? st = null;
                try { st = JsonNode.Parse(await http.GetStringAsync($"/api/link/status?id={id}")) as JsonObject; } catch { continue; }
                var status = (string?)st?["status"];
                if (status == "pending") continue;
                var a = Read();
                if ((string?)a["linkRequest"] != id) return;                  // another request since
                a.Remove("linkRequest");
                if (status == "approved" && (string?)st!["key"] is { Length: > 0 } key)
                {
                    a["device"] = Convert.ToBase64String(Protect(Encoding.UTF8.GetBytes(key)));
                    a["user"] = (string?)st["user"];
                    a["linkResult"] = "linked";
                }
                else a["linkResult"] = status ?? "unknown";
                await File.WriteAllTextAsync(file, a.ToJsonString());
                return;
            }
        }

        async Task<IResult> RegisterOrEnter(bool open, bool launch = false)
        {
            var a = Read();
            var (link, problem) = await WorldOf(a);
            if (link is null) return Results.Json(new { ok = false, text = problem });
            // LINKED: this Thalia enters with its own key, as the person it is linked to.
            var deviceKey = a["device"] is not null && (string?)a["user"] is { Length: > 0 } ? Encoding.UTF8.GetString(Unprotect(Convert.FromBase64String((string)a["device"]!))) : null;
            var me0 = UserProfile.Read(thaliaData);
            if (deviceKey is null && !UserProfile.Registered(me0)) return Results.Json(new { ok = false, text = "Register with Thalia first (your name)." });
            // THE PERSON'S PASSWORD (2026-11-26): the one given to Thalia at registration - Thalia represents the person, with the person's credential; else the one kept for the MMM before.
            var password = deviceKey is not null ? "" : UserProfile.Password(me0) ?? (a["password"] is not null ? Encoding.UTF8.GetString(Unprotect(Convert.FromBase64String((string)a["password"]!))) : null);
            if (password is null) return Results.Json(new { ok = false, text = "Your password is not set: Settings, You." });
            var invite = System.Web.HttpUtility.ParseQueryString(link.Query)["invite"];
            var origin = link.GetLeftPart(UriPartial.Authority);
            using var http = new HttpClient { BaseAddress = new Uri(origin), Timeout = TimeSpan.FromSeconds(30) };
            if (invite is { Length: > 0 }) http.DefaultRequestHeaders.Add("Cookie", "mmm-id=" + invite);          // the invitation names the person, as it does in the browser
            JsonObject? me;
            try { me = JsonNode.Parse(await http.GetStringAsync("/viewer/whoami")) as JsonObject; }
            catch (Exception e) { return Results.Json(new { ok = false, text = "The M-Instance does not answer: " + e.Message }); }
            if (me is null || me["accounts"] is null) return Results.Json(new { ok = false, text = "This M-Instance has no Accounts: open it in the browser." });
            var visitor = (bool?)me["byAccount"] == true || (string?)me["kind"] == "visitor";      // the person is the Account it registers with
            var user = deviceKey is not null ? (string)a["user"]! : visitor ? AccountId((string?)me0["first"] ?? "", (string?)me0["last"] ?? "") : (string?)me["name"] ?? "";
            if (user.Length == 0) return Results.Json(new { ok = false, text = "Your first and last name are not set." });
            var data = new JsonObject { ["FirstName"] = (string?)me0["first"], ["LastName"] = (string?)me0["last"] };
            if ((string?)me0["organisation"] is { Length: > 0 } org) data["Organisation"] = org;
            if ((string?)me0["email"] is { Length: > 0 } mail) data["Email"] = mail;
            var profile = new JsonObject
            {
                ["Header"] = "MMM-PPR-V2.2", ["MInstanceID"] = "MI1", ["humanID"] = user + "-human", ["PersonalProfileID"] = $"Profile-{user}-human",
                ["PersonalProfile"] = deviceKey is not null
                    ? new JsonObject      // entering with the key of this device
                    {
                        ["DeviceKey"] = deviceKey,
                        ["Processes"] = new JsonArray(new JsonObject { ["ProcessIDOrProcess"] = new JsonArray(new JsonObject { ["ProcessID"] = user }), ["PersonaIDOrPersona"] = new JsonArray(new JsonObject { ["PersonaID"] = user + "-Persona" }) })
                    }
                    : new JsonObject
                    {
                        ["Credential"] = password, ["Data"] = new JsonArray(data),          // no Account Level: the person chooses its role at its first entry into the MMM (2026-11-27)
                        ["Processes"] = new JsonArray(new JsonObject { ["ProcessIDOrProcess"] = new JsonArray(new JsonObject { ["ProcessID"] = user }), ["PersonaIDOrPersona"] = new JsonArray(new JsonObject { ["PersonaID"] = user + "-Persona" }) })
                    }
            };
            using var rq = new HttpRequestMessage(HttpMethod.Post, "/api/v2.2/identity/register")
            {
                Content = new StringContent(new JsonObject { ["SourceProcessID"] = user + "-human", ["Action"] = "", ["Complements"] = new JsonObject { ["With"] = new JsonArray(profile) } }.ToJsonString(), Encoding.UTF8, "application/json")
            };
            rq.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            using var rs = await http.SendAsync(rq);
            var said = JsonNode.Parse(await rs.Content.ReadAsStringAsync()) as JsonObject;
            var tokens = rs.Headers.TryGetValues("MMM-Tokens", out var t) ? t.FirstOrDefault() ?? "" : "";
            var token = tokens.Split(',').Select(p => p.Split('=')).FirstOrDefault(p => p.Length == 2 && Uri.UnescapeDataString(p[0]) == user)?[1];
            if (token is null) return Results.Json(new { ok = false, text = $"Not entered: {(string?)said?["paStatus"]?["detail"] ?? ((int)rs.StatusCode).ToString()}" });
            a["user"] = user; await File.WriteAllTextAsync(file, a.ToJsonString());
            if (!open) return Results.Json(new { ok = true, text = (int)rs.StatusCode == 201 ? $"Registered: your Account is open, {user}. Enter the MMM whenever you like; the first time you choose your Persona there." : $"You have an Account already, {user}: Enter the MMM." });
            var url = $"{origin}/viewer/?world=1&mic=1{(invite is { Length: > 0 } ? "&invite=" + Uri.EscapeDataString(invite) : "")}#mmm-token={token}&mmm-user={Uri.EscapeDataString(user)}";
            if (launch) OpenWorld(url);
            return Results.Json(new { ok = true, url, opened = launch, text = (int)rs.StatusCode == 201 ? $"Your Account is open, {user}: the world opens; choose your Persona there." : $"Entered as {user}: the world opens." });
        }
    }

    // THE WORLD IN ITS OWN EDGE WINDOW, opened by Thalia (not by the page: no pop-up to allow), in the Edge profile of Thalia, which keeps the Personae it downloaded - the second time
    // nothing is downloaded again - and the permission for the microphone.
    private static void OpenWorld(string url)
    {
        var profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Thalia", "edge");
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("msedge", $"--user-data-dir=\"{profile}\" --no-first-run --no-default-browser-check --start-maximized --app=\"{url}\"") { UseShellExecute = true }); }
        catch (Exception e) { Console.WriteLine("opening the world: " + e.Message); }
    }


    // A visitor's User: the letters of its first and last name (as the page makes it).
    public static string AccountId(string first, string last)
    {
        var s = (first + last).Normalize(NormalizationForm.FormD);
        return new string(s.Where(c => c < 128 && char.IsLetterOrDigit(c)).ToArray());
    }

    // ---- DPAPI: encrypted for this Windows user (CryptProtectData), as Windows keeps the passwords of its users.
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Size; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr mem);

    internal static byte[] Protect(byte[] data) => Crypt(data, true);
    internal static byte[] Unprotect(byte[] data) => Crypt(data, false);
    private static byte[] Crypt(byte[] data, bool protect)
    {
        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            var input = new Blob { Size = data.Length, Data = handle.AddrOfPinnedObject() };
            var ok = protect ? CryptProtectData(ref input, "Thalia MMM", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out var output)
                             : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!ok) throw new InvalidOperationException("Windows could not " + (protect ? "encrypt" : "decrypt") + " the password (DPAPI " + Marshal.GetLastWin32Error() + ")");
            var result = new byte[output.Size];
            Marshal.Copy(output.Data, result, 0, output.Size);
            LocalFree(output.Data);
            return result;
        }
        finally { handle.Free(); }
    }
}
