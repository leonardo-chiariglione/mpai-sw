// NOTICE (third-party models and data). This source file is released under the BSD 3-Clause licence of this repository.
// The models and the databases it uses, and the data derived from them, are not part of it: each has its own licence, and some
// restrict use (for example to non-commercial research). Read THIRD-PARTY-NOTICES.md and the Terms of the AccessSources of the AIM.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Microsoft.ML.OnnxRuntime;

namespace Mpai.Onnx;

// EVERY ONNX MODEL OF AN AIM IS OPENED HERE (the author, 2026/10/06: a server with a
// 16 GB GPU answered more slowly than a desktop with a 4 GB one - nothing but the
// language model used the GPU). A session is opened on the GPU where it can be and on
// the CPU otherwise, and the choice is said once per model:
//
//   MPAI_ONNX_DEVICE = auto  (default) CUDA if this build and this machine have it, else CPU
//                      cuda  CUDA or fail - for a machine that must not run slowly unnoticed
//                      cpu   the CPU, whatever the build
//
// A build has CUDA only with -p:MpaiOnnxGpu=true (Mpai.Onnx.csproj). Operators CUDA does
// not implement run on the CPU within the same session; ONNX Runtime places them itself.
public static class OnnxSessions
{
    private static readonly string Device =
        (Environment.GetEnvironmentVariable("MPAI_ONNX_DEVICE") ?? "auto").Trim().ToLowerInvariant();

    // Once CUDA has been found absent, it is not tried for every further model.
    private static volatile bool cudaAbsent;
    private static readonly ConcurrentDictionary<string, string> said = new(StringComparer.Ordinal);

    public static InferenceSession Create(string modelPath)
    {
        if (Device != "cpu" && !cudaAbsent)
        {
            SessionOptions? options = null;
            try
            {
                options = new SessionOptions();
                options.AppendExecutionProvider_CUDA(0);
                var session = new InferenceSession(modelPath, options);
                Say(modelPath, "CUDA");
                return session;
            }
            catch (Exception e) when (Device != "cuda")
            {
                options?.Dispose();
                cudaAbsent = true;
                Say(modelPath, "CPU (CUDA unavailable: " + e.GetBaseException().Message.Split('\n')[0].Trim() + ")");
            }
        }
        else Say(modelPath, "CPU");
        return new InferenceSession(modelPath);
    }

    // ONE SESSION OF A MODEL FOR EVERY AIM THAT HOLDS IT (N-51, 2026-11-27). On the MAS instance every Module Instance has a Controller of its own and so AIMs of its own; an
    // AIM that opened its model with Create loaded it again for every client. Shared gives the session of that model file already open in this process, or opens it; each
    // holder gives it back with Release, and the last one closes it. ONNX Runtime lets several threads Run one session at once. For a model an AIM only Runs (no state in it).
    private sealed class Held
    {
        public required InferenceSession Session;
        public int Holders;
    }

    private static readonly Dictionary<string, Held> shared = new(StringComparer.Ordinal);

    // THE SAME MODEL IN ANOTHER FILE IS THE SAME MODEL (2026-11-27). Apps that use one model often have it in a folder of their own (the copies in AIF\Access\view): at the
    // start of the MAS instance glintr100 was loaded three times, ECAPA four, w2v2-emotion twice. A model is therefore known by what it is - the SHA-256 of the model file
    // and of its external data files beside it (name.data, name.onnx.data, name_data) - not by where it is. Each file is read for this once while it is unchanged.
    private static readonly ConcurrentDictionary<string, string> fingerprints = new(StringComparer.OrdinalIgnoreCase);

    private static string KeyOf(string modelPath)
    {
        var file = new FileInfo(Path.GetFullPath(modelPath));
        var stamp = $"{file.FullName}|{file.Length}|{file.LastWriteTimeUtc.Ticks}";
        return fingerprints.GetOrAdd(stamp, _ =>
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            var parts = new List<string> { file.FullName };
            var stem = Path.GetFileNameWithoutExtension(file.Name);
            foreach (var data in new[] { file.Name + ".data", stem + ".data", file.Name + "_data", stem + "_data" })
            {
                var p = Path.Combine(file.DirectoryName!, data);
                if (File.Exists(p) && !parts.Contains(p, StringComparer.OrdinalIgnoreCase)) parts.Add(p);
            }
            var all = new System.Text.StringBuilder();
            foreach (var p in parts)
            {
                using var s = File.OpenRead(p);
                all.Append(Path.GetFileName(p) == file.Name ? "model:" : "data:").Append(Convert.ToHexString(sha.ComputeHash(s))).Append(';');
            }
            return all.ToString();
        });
    }

    public static InferenceSession Shared(string modelPath)
    {
        var key = KeyOf(modelPath);
        lock (shared)
        {
            if (!shared.TryGetValue(key, out var held))
                shared[key] = held = new Held { Session = Create(modelPath) };
            held.Holders++;
            return held.Session;
        }
    }

    public static void Release(InferenceSession session)
    {
        lock (shared)
        {
            foreach (var (key, held) in shared)
            {
                if (!ReferenceEquals(held.Session, session)) continue;
                if (--held.Holders == 0)
                {
                    shared.Remove(key);
                    session.Dispose();
                }
                return;
            }
        }
        session.Dispose();      // not a shared session: it is the holder's own
    }

    // How many AIMs hold the shared session of this model file (0: none is open); for the tests.
    public static int HoldersOf(string modelPath)
    {
        var key = KeyOf(modelPath);
        lock (shared) return shared.TryGetValue(key, out var held) ? held.Holders : 0;
    }

    private static void Say(string modelPath, string device)
    {
        if (said.TryAdd(modelPath, device))
            Console.WriteLine($"[ONNX] {Path.GetFileName(modelPath)}: {device}");
    }
}
