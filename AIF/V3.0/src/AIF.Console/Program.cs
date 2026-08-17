using AIF.Store;

using Mpai.Aims.Visual;
using Mpai.Aims.Tiq;
using Mpai.Core;

// A direct VOA -> TIQ smoke test. Model and folder locations come from
// AIMs/aim-settings.json, located by walking up from the executable, so
// nothing here is tied to one machine.

var settingsPath =
    FindSettingsFile();

if (settingsPath is null)
{
    Console.WriteLine(
        $"Could not find AIMs\\aim-settings.json above {AppContext.BaseDirectory}");
    return;
}

var settings =
    AimSettings.Load(
        settingsPath);

var tiqSettings =
    settings.For(
        "MMC-TIQ-V2.5");

if (tiqSettings.Count == 0)
{
    Console.WriteLine(
        $"No MMC-TIQ-V2.5 section in {settingsPath}.");
    return;
}

Console.WriteLine();
Console.WriteLine(
    "--------------------------------");
Console.WriteLine(
    "VOA -> TIQ");
Console.WriteLine(
    "--------------------------------");
Console.WriteLine();
Console.WriteLine(
    $"settings: {settingsPath}");
Console.WriteLine();

// The image folder: CVE-VOA's SourceHint if it names one, else the caller
// can type an absolute path.
var imageFolder =
    settings.For("CVE-VOA-V1.0")
            .TryGetValue("SourceHint", out var hint) &&
    !string.IsNullOrWhiteSpace(hint)
        ? hint
        : string.Empty;

if (imageFolder.Length > 0)
{
    Console.WriteLine(
        $"image folder: {imageFolder}");
    Console.WriteLine();
}

Console.Write(
    "Image file: ");

var imageFile =
    Console.ReadLine() ?? "";

var imagePath =
    Path.IsPathRooted(imageFile) || imageFolder.Length == 0
        ? imageFile
        : Path.Combine(
              imageFolder,
              imageFile);

if (!File.Exists(imagePath))
{
    Console.WriteLine();
    Console.WriteLine(
        $"No such file: {imagePath}");
    return;
}

Console.WriteLine();

Console.Write(
    "Question: ");

var question =
    Console.ReadLine() ?? "";

var voa =
    new FileVisualAcquisition();

var image =
    await voa.AcquireAsync(
        new VisualAcquisitionRequest
        {
            SourcePath = imagePath
        });

var tiq =
    TiqFactory.Create(
        tiqSettings);

var answer =
    await tiq.ProcessAsync(
        BasicTextObject.FromText(
            question),
        image);

Console.WriteLine();
Console.WriteLine(
    $"Answer: {answer.GetText()}");
Console.WriteLine();

// Walk up from the executable looking for AIMs\aim-settings.json.
static string? FindSettingsFile()
{
    var directory =
        new DirectoryInfo(
            AppContext.BaseDirectory);

    while (directory is not null)
    {
        var candidate =
            Path.Combine(
                directory.FullName,
                "AIMs",
                "aim-settings.json");

        if (File.Exists(candidate))
        {
            return candidate;
        }

        directory = directory.Parent;
    }

    return null;
}