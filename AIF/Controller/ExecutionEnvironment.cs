using System.Runtime.InteropServices;

namespace AIF.Controller;

// THE ENVIRONMENT AN AIM EXECUTES IN, as the AIM Metadata names it: the Architecture and the OperatingSystem of an Implementation (L1, Implementations), and
// of an entry of AccessSources, which is then for that environment only (a program of Access for Windows, another for Linux). Named nothing, it is for all.
public static class ExecutionEnvironment
{
    private static string Arch(string a) => a.ToLowerInvariant().Replace("-", "").Replace("_", "") switch
    {
        "x8664" or "x64" or "amd64" => "x64",
        "arm64" or "aarch64"        => "arm64",
        "x86" or "i386"             => "x86",
        _                            => a.ToLowerInvariant()
    };

    // Whether what is named is this machine: the one that executes.
    public static bool IsHere(string architecture, string operatingSystem)
    {
        var here = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
        var ok = architecture.Length == 0 || Arch(architecture) == Arch(here);
        if (operatingSystem.Length > 0)
        {
            var os = operatingSystem.ToLowerInvariant();
            ok &= os.Contains("window") ? RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                : os.Contains("linux")  ? RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                : os.Contains("mac") || os.Contains("osx") ? RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                : true;
        }
        return ok;
    }

    // Whether two entries could both apply somewhere: one of them names no environment, or both name the same.
    public static bool Overlap(string architectureA, string operatingSystemA, string architectureB, string operatingSystemB) =>
        (architectureA.Length == 0 || architectureB.Length == 0 || Arch(architectureA) == Arch(architectureB)) &&
        (operatingSystemA.Length == 0 || operatingSystemB.Length == 0 || string.Equals(operatingSystemA, operatingSystemB, StringComparison.OrdinalIgnoreCase));

    // Whether two entries could both be given to the AIM, as to their Accelerator: the CUDA entry is given where the other is not,
    // and an entry with no Accelerator is given where the CPU entry would be.
    public static bool OverlapAccelerator(string acceleratorA, string acceleratorB) => IsCuda(acceleratorA) == IsCuda(acceleratorB);

    public static bool IsCuda(string accelerator) => accelerator.Equals("CUDA", StringComparison.OrdinalIgnoreCase);

    // ---- THE GRAPHICS CARD OF THE MACHINE (Accelerator of an entry of AccessSources) -------------------------------------------

    // The NVIDIA graphics card of the machine where the AIM executes, as its driver reports it: compute capability 6.1 is SM61.
    public sealed record CudaCard(int ComputeCapability, long MemoryBytes)
    {
        public string Class => "SM" + ComputeCapability;
    }

    private static readonly Lazy<CudaCard?> Probed = new(Probe);

    // Set by a test: what it pretends the machine has (the function returns null: no card). Unset: MPAI_GPU, then the driver.
    public static Func<CudaCard?>? CardProbe { get; set; }

    // THE CARD OF THIS MACHINE: MPAI_GPU=none says there is none, MPAI_GPU=SM61 that there is one of that class (for a test, or
    // where the driver is not to be asked); otherwise the driver is asked once and the answer kept. No driver, or an error: no card.
    public static CudaCard? Card
    {
        get
        {
            if (CardProbe is { } probe) return probe();
            var setting = Environment.GetEnvironmentVariable("MPAI_GPU")?.Trim();
            if (string.Equals(setting, "none", StringComparison.OrdinalIgnoreCase)) return null;
            if (setting is { Length: > 0 } && SmNumber(setting) is { } sm) return new CudaCard(sm, 0);
            return Probed.Value;
        }
    }

    private static CudaCard? Probe()
    {
        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("nvidia-smi", "--query-gpu=compute_cap,memory.total --format=csv,noheader")
            { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true });
            if (p is null) return null;
            var output = p.StandardOutput.ReadToEndAsync();
            if (!p.WaitForExit(10000)) { try { p.Kill(); } catch { } return null; }
            return p.ExitCode == 0 ? ParseQuery(output.GetAwaiter().GetResult()) : null;
        }
        catch { return null; }
    }

    // The driver's answer, "6.1, 4096 MiB": the first card listed. Null where it is not understood.
    public static CudaCard? ParseQuery(string output)
    {
        var line = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        if (line is null) return null;
        var parts = line.Split(',', StringSplitOptions.TrimEntries);
        var cap = parts[0].Split('.');
        if (cap.Length != 2 || !int.TryParse(cap[0], out var major) || !int.TryParse(cap[1], out var minor)) return null;
        long memory = 0;
        if (parts.Length > 1 && parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries) is [var n, "MiB"] && long.TryParse(n, out var mib)) memory = mib * 1024 * 1024;
        return new CudaCard(major * 10 + minor, memory);
    }

    // "SM61" -> 61; null where it is not a class as the AIM Metadata schema writes it.
    public static int? SmNumber(string value) =>
        value.StartsWith("SM", StringComparison.OrdinalIgnoreCase) && int.TryParse(value[2..], out var n) ? n : null;

    // WHETHER THE AIM IS GIVEN ITS CUDA ENTRIES: its ResourcePolicies request a GPU (GPU:Number Request 1 or more) and the machine
    // has an NVIDIA card that meets GPU:CUDA:Class Minimum (where the AIM states no Minimum, any card meets it).
    public static bool UsesCuda(IReadOnlyList<ResourcePolicy> policies, CudaCard? card)
    {
        if (card is null) return false;
        var number = policies.FirstOrDefault(p => p.Name == "GPU:Number");
        if (number is null || !int.TryParse(number.Request, out var request) || request < 1) return false;
        var minimum = policies.FirstOrDefault(p => p.Name == "GPU:CUDA:Class")?.Minimum;
        return string.IsNullOrEmpty(minimum) || (SmNumber(minimum) is { } needed && card.ComputeCapability >= needed);
    }
}
