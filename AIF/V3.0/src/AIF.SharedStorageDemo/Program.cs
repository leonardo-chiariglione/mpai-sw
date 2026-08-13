using System.Text;
using AIF.SharedStorage;

// Interactive REPL for hands-on exercise of the six Global Storage
// primitives (Put/Get/Delete/List/Exists/GetKeyInfo). File-backed and
// persistent across runs, exactly like AssetRepository elsewhere in
// CAE-ASM - close this, reopen it later, and everything you stored is
// still there.

var root = args.Length > 0 ? args[0] : @"D:\AI\GlobalStorageDemo";
var topAim = args.Length > 1 ? args[1] : "CAE-ASM-Demo";
var requestedBy = args.Length > 2 ? args[2] : "Local-UA";

var storage = new FileSharedStorage(root, topAim, requestedBy);

Console.WriteLine("=== AIF Global Storage - interactive demo ===");
Console.WriteLine($"Root: {root}");
Console.WriteLine($"Top AIM (stamped into every Put): {topAim}");
Console.WriteLine($"Requested by (UA/RCA identity, stamped into every Put): {requestedBy}");
Console.WriteLine();
PrintHelp();

while (true)
{
    Console.Write("> ");
    var line = Console.ReadLine();
    if (line is null) break;

    var parts = line.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length == 0) continue;

    var command = parts[0].ToLowerInvariant();
    var rest = parts.Length > 1 ? parts[1] : "";

    try
    {
        switch (command)
        {
            case "put":
            {
                var putParts = rest.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                if (putParts.Length < 2) { Console.WriteLine("Usage: put <key> <value...>"); break; }
                storage.Put(putParts[0], Encoding.UTF8.GetBytes(putParts[1]));
                Console.WriteLine($"OK - stored {Encoding.UTF8.GetByteCount(putParts[1])} byte(s) at '{putParts[0]}'.");
                break;
            }

            case "get":
            {
                if (string.IsNullOrWhiteSpace(rest)) { Console.WriteLine("Usage: get <key>"); break; }
                var value = storage.Get(rest.Trim());
                Console.WriteLine($"'{rest.Trim()}' = {Encoding.UTF8.GetString(value)}");
                break;
            }

            case "delete":
            case "del":
            {
                if (string.IsNullOrWhiteSpace(rest)) { Console.WriteLine("Usage: delete <key>"); break; }
                storage.Delete(rest.Trim());
                Console.WriteLine($"OK - '{rest.Trim()}' deleted (or was already absent).");
                break;
            }

            case "list":
            case "ls":
            {
                var keys = storage.List(rest.Trim());
                if (keys.Count == 0)
                {
                    Console.WriteLine(string.IsNullOrEmpty(rest) ? "(no keys stored yet)" : $"(no keys match prefix '{rest.Trim()}')");
                }
                else
                {
                    foreach (var k in keys) Console.WriteLine($"  {k}");
                    Console.WriteLine($"({keys.Count} key(s))");
                }
                break;
            }

            case "exists":
            {
                if (string.IsNullOrWhiteSpace(rest)) { Console.WriteLine("Usage: exists <key>"); break; }
                Console.WriteLine(storage.Exists(rest.Trim()) ? "true" : "false");
                break;
            }

            case "info":
            {
                if (string.IsNullOrWhiteSpace(rest)) { Console.WriteLine("Usage: info <key>"); break; }
                var info = storage.GetKeyInfo(rest.Trim());
                Console.WriteLine($"StoredBy: {info.StoredBy}");
                Console.WriteLine($"RequestedBy: {info.RequestedBy}");
                Console.WriteLine($"StoredAt: {info.StoredAt:O}");
                break;
            }

            case "help":
            case "?":
                PrintHelp();
                break;

            case "exit":
            case "quit":
                return;

            default:
                Console.WriteLine($"Unknown command '{command}'. Type 'help' for the list of commands.");
                break;
        }
    }
    catch (KeyNotFoundException failure)
    {
        Console.WriteLine($"ERROR: {failure.Message}");
    }
    catch (Exception failure)
    {
        Console.WriteLine($"ERROR: {failure.GetType().Name}: {failure.Message}");
    }
}

static void PrintHelp()
{
    Console.WriteLine("Commands:");
    Console.WriteLine("  put <key> <value...>   Store text at key (overwrites if it already exists)");
    Console.WriteLine("  get <key>              Retrieve and print the value at key");
    Console.WriteLine("  delete <key>           Remove key (not an error if it doesn't exist)");
    Console.WriteLine("  list [prefix]          List keys, optionally only those starting with prefix");
    Console.WriteLine("  exists <key>           Print true/false");
    Console.WriteLine("  info <key>             Print StoredBy/RequestedBy/StoredAt (framework-stamped)");
    Console.WriteLine("  help                   Show this list again");
    Console.WriteLine("  exit                   Quit");
    Console.WriteLine();
    Console.WriteLine("Try, for example:");
    Console.WriteLine("  put AudioObject:AUO000001 hello world");
    Console.WriteLine("  info AudioObject:AUO000001");
    Console.WriteLine("  put AudioObject:AUO000002 a second object");
    Console.WriteLine("  list AudioObject:");
    Console.WriteLine();
}