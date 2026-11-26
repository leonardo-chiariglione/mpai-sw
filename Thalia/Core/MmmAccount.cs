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
            var a = Read();
            return Results.Json(new JsonObject
            {
                ["address"] = (string?)a["address"] ?? defaultAddress, ["first"] = (string?)a["first"], ["last"] = (string?)a["last"], ["organisation"] = (string?)a["organisation"],
                ["email"] = (string?)a["email"], ["level"] = (string?)a["level"] ?? "Visitor", ["hasPassword"] = a["password"] is not null, ["user"] = (string?)a["user"]
            });
        });

        app.MapPost("/api/mmm/account", async (HttpContext ctx) =>
        {
            var given = JsonNode.Parse(await new StreamReader(ctx.Request.Body).ReadToEndAsync()) as JsonObject ?? new JsonObject();
            var a = Read();
            foreach (var k in new[] { "address", "first", "last", "organisation", "email", "level" }) if (given[k] is not null) a[k] = ((string?)given[k])?.Trim();
            if ((string?)given["password"] is { Length: > 0 } pw) a["password"] = Convert.ToBase64String(Protect(Encoding.UTF8.GetBytes(pw)));
            Directory.CreateDirectory(thaliaData);
            await File.WriteAllTextAsync(file, a.ToJsonString());
            return Results.Json(new { ok = true });
        });

        // REGISTER, for the person: the Account is opened (201), or the person has one already (200); nothing is opened. ENTER: the same, then the address that opens the world, entered.
        app.MapPost("/api/mmm/register", () => RegisterOrEnter(false));
        app.MapPost("/api/mmm/enter", (string? launch) => RegisterOrEnter(true, launch == "1"));      // not (HttpContext ctx) =>: that is taken for a raw handler, and the answer is lost
        // ENTERING BY ITSELF (the participants' Thalia, 2026-11-26): once the person has registered, Thalia enters the MMM when it starts and opens the world - nothing to press.
        if (autoEnter)
            app.Lifetime.ApplicationStarted.Register(() => _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(3));
                var a = Read();
                if (a["user"] is not null && a["password"] is not null && (bool?)a["autoEnter"] != false)
                    try { await RegisterOrEnter(true, true); } catch (Exception e) { Console.WriteLine("entering the MMM: " + e.Message); }
            }));
        async Task<IResult> RegisterOrEnter(bool open, bool launch = false)
        {
            var a = Read();
            if (((string?)a["address"] is { Length: > 0 } given ? given : defaultAddress) is not { Length: > 0 } address || !Uri.TryCreate(address, UriKind.Absolute, out var link)) return Results.Json(new { ok = false, text = "The address of the M-Instance is not set." });
            // THE JOIN PAGE (a stable address, AllApps\MMM\Join): it names the address of the meeting now (meeting.json), which changes from one meeting to the next.
            if (link.AbsolutePath.EndsWith("/join.html", StringComparison.OrdinalIgnoreCase) || link.AbsolutePath.TrimEnd('/').EndsWith("/mmm", StringComparison.OrdinalIgnoreCase))
            {
                var folder = link.AbsolutePath.EndsWith("/join.html", StringComparison.OrdinalIgnoreCase) ? link.AbsolutePath[..^"join.html".Length] : link.AbsolutePath.TrimEnd('/') + "/";
                try
                {
                    using var j = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                    var meeting = JsonNode.Parse(await j.GetStringAsync(new Uri(link, folder + "meeting.json"))) as JsonObject;
                    if ((string?)meeting?["Address"] is not { Length: > 0 } now) return Results.Json(new { ok = false, text = $"The meeting is not on now{((string?)meeting?["When"] is { Length: > 0 } w ? ": it is " + w : "")}." });
                    link = new Uri(now.TrimEnd('/') + "/viewer/?world=1&mic=1");
                }
                catch (Exception e) { return Results.Json(new { ok = false, text = "The meeting is not on now (its join page does not answer: " + e.Message + ")." }); }
            }
            if (a["password"] is null) return Results.Json(new { ok = false, text = "The password is not set." });
            var password = Encoding.UTF8.GetString(Unprotect(Convert.FromBase64String((string)a["password"]!)));
            var invite = System.Web.HttpUtility.ParseQueryString(link.Query)["invite"];
            var origin = link.GetLeftPart(UriPartial.Authority);
            using var http = new HttpClient { BaseAddress = new Uri(origin), Timeout = TimeSpan.FromSeconds(30) };
            if (invite is { Length: > 0 }) http.DefaultRequestHeaders.Add("Cookie", "mmm-id=" + invite);          // the invitation names the person, as it does in the browser
            JsonObject? me;
            try { me = JsonNode.Parse(await http.GetStringAsync("/viewer/whoami")) as JsonObject; }
            catch (Exception e) { return Results.Json(new { ok = false, text = "The M-Instance does not answer: " + e.Message }); }
            if (me is null || me["accounts"] is null) return Results.Json(new { ok = false, text = "This M-Instance has no Accounts: open it in the browser." });
            var visitor = (bool?)me["byAccount"] == true || (string?)me["kind"] == "visitor";      // the person is the Account it registers with
            var user = visitor ? AccountId((string?)a["first"] ?? "", (string?)a["last"] ?? "") : (string?)me["name"] ?? "";
            if (user.Length == 0) return Results.Json(new { ok = false, text = "Your first and last name are not set." });
            var data = new JsonObject { ["FirstName"] = (string?)a["first"], ["LastName"] = (string?)a["last"] };
            if ((string?)a["organisation"] is { Length: > 0 } org) data["Organisation"] = org;
            if ((string?)a["email"] is { Length: > 0 } mail) data["Email"] = mail;
            var profile = new JsonObject
            {
                ["Header"] = "MMM-PPR-V2.2", ["MInstanceID"] = "MI1", ["humanID"] = user + "-human", ["PersonalProfileID"] = $"Profile-{user}-human",
                ["PersonalProfile"] = new JsonObject
                {
                    ["AccountLevel"] = (string?)a["level"] ?? "Visitor", ["Credential"] = password, ["Data"] = new JsonArray(data),
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

    private static byte[] Protect(byte[] data) => Crypt(data, true);
    private static byte[] Unprotect(byte[] data) => Crypt(data, false);
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
