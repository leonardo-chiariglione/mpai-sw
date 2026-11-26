using System;
using System.IO;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

// THE PERSON'S PROFILE IN THALIA (2026-11-26). The person registers once, with Thalia: first and last name, organisation, email, and a password - Thalia represents the person, and
// the password is the person's credential where Thalia registers it (the MMM Account), so it works also when the person enters from a browser. Kept encrypted (DPAPI), never in clear. An App that needs the person's registration
// (the MMM first) obtains it from Thalia - the person does not register again with each App. Kept in profile.json in the person's Thalia data.
static class UserProfile
{
    static readonly string[] Fields = { "first", "last", "organisation", "email" };

    public static string? Password(JsonObject p) => (string?)p["password"] is { Length: > 0 } x ? System.Text.Encoding.UTF8.GetString(MmmAccount.Unprotect(Convert.FromBase64String(x))) : null;

    public static JsonObject Read(string thaliaData)
    {
        var file = Path.Combine(thaliaData, "profile.json");
        try { if (File.Exists(file)) return JsonNode.Parse(File.ReadAllText(file))!.AsObject(); } catch { }
        // From before the profile: the details given to the MMM User.
        var p = new JsonObject();
        try
        {
            var mmm = Path.Combine(thaliaData, "mmm-account.json");
            if (File.Exists(mmm) && JsonNode.Parse(File.ReadAllText(mmm)) is JsonObject a)
                foreach (var k in Fields) if ((string?)a[k] is { Length: > 0 } v) p[k] = v;
        }
        catch { }
        return p;
    }

    public static bool Registered(JsonObject p) => (string?)p["first"] is { Length: > 0 } && (string?)p["last"] is { Length: > 0 };

    public static void Map(WebApplication app, string thaliaData)
    {
        app.MapGet("/api/profile", () => { var p = Read(thaliaData); p["registered"] = Registered(p); p["hasPassword"] = p["password"] is not null; p.Remove("password"); return Results.Json(p); });
        app.MapPost("/api/profile", async (HttpContext ctx) =>
        {
            var given = JsonNode.Parse(await new StreamReader(ctx.Request.Body).ReadToEndAsync()) as JsonObject ?? new JsonObject();
            var p = Read(thaliaData);
            foreach (var k in Fields) if (given[k] is not null) p[k] = ((string?)given[k])?.Trim();
            p.Remove("registered"); p.Remove("hasPassword");
            if (!Registered(p)) return Results.Json(new { ok = false, text = "Your first and last name are needed." });
            if ((string?)given["password"] is { Length: > 0 } pw)
            {
                if (pw.Length < 6) return Results.Json(new { ok = false, text = "The password needs at least 6 characters." });
                p["password"] = Convert.ToBase64String(MmmAccount.Protect(System.Text.Encoding.UTF8.GetBytes(pw)));
            }
            if (p["password"] is null) return Results.Json(new { ok = false, text = "A password is needed (at least 6 characters)." });
            Directory.CreateDirectory(thaliaData);
            await File.WriteAllTextAsync(Path.Combine(thaliaData, "profile.json"), p.ToJsonString());
            return Results.Json(new { ok = true });
        });
    }
}
