using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using AIF.Store;

using Microsoft.ML.OnnxRuntime;

namespace AmqAif.Host;

// Stage 2a of the real MMC-TTT engine: report the shape of the two ONNX graphs
// WITHOUT running them.
//
// The decoder of an encoder-decoder export takes a large, version-dependent set
// of inputs - input_ids, encoder_hidden_states, encoder_attention_mask, a
// use_cache_branch flag, and past_key_values.N.{decoder,encoder}.{key,value}
// across every layer - and returns logits plus the matching present.N.* tensors.
// Getting one name or one rank wrong makes the decoder emit the same token for
// ever, which is indistinguishable from a wrong language token or a wrong id
// mapping. The public discussion threads on this exact model are full of it.
//
// So this stage only reads metadata. Once the table below is known, the decode
// loop can be written against facts rather than assumptions.
//
// Run with:  dotnet run -- --graphtest
internal static class GraphTest
{
    public static void Run()
    {
        var repoRoot = FindRepoRoot();
        if (repoRoot is null) { Console.WriteLine("Could not find AIMs\\AMDs."); return; }

        var settings = AimSettings.Load(Path.Combine(repoRoot, "AIMs", "aim-settings.json"))
                                  .For("MMC-TTT-V2.5");

        // Every ONNX graph the settings name, so the unmerged decoders can be
        // compared with the merged one that failed inside optimum::if.
        var wanted = new (string Key, string Label)[]
        {
            ("TttEncoderModel",      "ENCODER"),
            ("TttDecoderFirstModel", "DECODER, first step (no past)"),
            ("TttDecoderPastModel",  "DECODER, with past"),
            ("TttDecoderModel",      "DECODER, merged")
        };

        var described = 0;
        foreach (var (key, label) in wanted)
        {
            if (!settings.TryGetValue(key, out var path) || string.IsNullOrWhiteSpace(path))
            {
                Console.WriteLine($"  ({key} not set)");
                continue;
            }
            if (!File.Exists(path))
            {
                Console.WriteLine($"  ({key}: missing {path})");
                continue;
            }

            Describe(label, path);
            described++;
        }

        if (described == 0)
        {
            Console.WriteLine("No models configured. Run the download script first.");
            return;
        }

        Console.WriteLine();
        Console.WriteLine("Reading it:");
        Console.WriteLine("  - the decoder's past_key_values.N.* count gives the layer count;");
        Console.WriteLine("    N pairs of decoder key/value plus N pairs of encoder key/value.");
        Console.WriteLine("  - on the FIRST decode step there is no past, so those inputs take");
        Console.WriteLine("    zero-length tensors and use_cache_branch is false; afterwards the");
        Console.WriteLine("    present.* outputs are fed back in as past_key_values.*.");
        Console.WriteLine("  - a dimension shown as -1 or by name is dynamic; anything fixed is a");
        Console.WriteLine("    constraint the loop has to honour.");
        Console.WriteLine();
    }

    private static void Describe(string label, string modelPath)
    {
        var file = new FileInfo(modelPath);

        Console.WriteLine();
        Console.WriteLine(new string('-', 72));
        Console.WriteLine($"{label}");
        Console.WriteLine($"  file: {file.Name}  ({file.Length:N0} bytes)");

        using var options = new SessionOptions();
        using var session = new InferenceSession(modelPath, options);

        Console.WriteLine();
        Console.WriteLine("  INPUTS");
        Report(session.InputMetadata);

        Console.WriteLine();
        Console.WriteLine("  OUTPUTS");
        Report(session.OutputMetadata);

        // The past_key_values family is the part that needs counting.
        var pastInputs = session.InputMetadata.Keys
            .Where(name => name.StartsWith("past_key_values.", StringComparison.Ordinal))
            .ToList();

        if (pastInputs.Count > 0)
        {
            var layers = pastInputs
                .Select(name => name.Split('.').ElementAtOrDefault(1) ?? string.Empty)
                .Where(part => int.TryParse(part, out _))
                .Select(part => int.Parse(part))
                .DefaultIfEmpty(-1)
                .Max() + 1;

            Console.WriteLine();
            Console.WriteLine($"  past_key_values inputs: {pastInputs.Count} " +
                              $"across {layers} layer(s)");

            var kinds = pastInputs
                .Select(name => string.Join(".", name.Split('.').Skip(2)))
                .Distinct()
                .OrderBy(kind => kind, StringComparer.Ordinal);

            Console.WriteLine($"  per layer: {string.Join(", ", kinds)}");
        }

        var hasCacheBranch = session.InputMetadata.ContainsKey("use_cache_branch");
        Console.WriteLine($"  use_cache_branch present: {hasCacheBranch}");
    }

    private static void Report(IReadOnlyDictionary<string, NodeMetadata> metadata)
    {
        foreach (var entry in metadata)
        {
            var dimensions = entry.Value.Dimensions ?? Array.Empty<int>();
            var names      = entry.Value.SymbolicDimensions ?? Array.Empty<string>();

            var shown = dimensions.Select((size, index) =>
                size >= 0
                    ? size.ToString()
                    : (index < names.Length && !string.IsNullOrEmpty(names[index])
                          ? names[index]
                          : "?"));

            Console.WriteLine($"    {entry.Key,-46} {entry.Value.ElementType,-12} " +
                              $"[{string.Join(", ", shown)}]");
        }
    }

    private static string? FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "AIMs", "AMDs")))
                return directory.FullName;
            directory = directory.Parent;
        }
        return null;
    }
}