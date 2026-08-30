using System;
using System.Linq;

using Mpai.Core.OSD;

// Standalone proof of the FACS core: for each basic emotion, print the EM-FACS
// Action Unit activations the generative face description would produce.
internal static class Program
{
    private static void Main()
    {
        string[] emotions = { "HAPPINESS", "SADNESS", "ANGER", "FEAR", "DISGUST", "SURPRISE", "CALMNESS" };
        Console.WriteLine("EM-FACS: emotion -> FACS Action Units (at intensity 0.8)");
        Console.WriteLine();
        foreach (var e in emotions)
        {
            var aus = EmFacs.ToActionUnits(e, 0.8);
            var active = aus.ActionUnits.Where(kv => kv.Value > 0)
                .Select(kv => $"{kv.Key.Replace("_", " ")}={kv.Value:F2}");
            Console.WriteLine($"  {e,-10} -> {(active.Any() ? string.Join(", ", active) : "(relaxed - no AUs)")}");
        }
    }
}
