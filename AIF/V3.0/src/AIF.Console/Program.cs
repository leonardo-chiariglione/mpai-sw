using Mpai.Aims.Visual;
using Mpai.Aims.Tiq;
using Mpai.Core;

Console.WriteLine();
Console.WriteLine(
    "--------------------------------");
Console.WriteLine(
    "VOA -> TIQ");
Console.WriteLine(
    "--------------------------------");
Console.WriteLine();

Console.Write(
    "Image file: ");

var imageFile =
    Console.ReadLine() ?? "";

var imagePath =
    Path.Combine(
        @"D:\AI\Images",
        imageFile);

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
    TiqFactory.Create();

var answer =
    await tiq.ProcessAsync(
        BasicTextObject.FromText(
            question),
        image);

Console.WriteLine();
Console.WriteLine(
    $"Answer: {answer.GetText()}");
Console.WriteLine();