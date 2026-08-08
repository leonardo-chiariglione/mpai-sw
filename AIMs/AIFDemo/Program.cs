using AIF.Store;

Console.WriteLine();
Console.WriteLine("========================================");
Console.WriteLine("MPAI AIM Catalog");
Console.WriteLine("========================================");
Console.WriteLine();

var store =
    new AmdStore(
        @"D:\AI\AIMs\AMDs");

store.Scan();

Console.WriteLine(
    $"AMDs Loaded: {store.Count}");

Console.WriteLine();

foreach (var item in store.GetCatalog())
{
    Console.WriteLine(item.AIMName);

    Console.WriteLine(
        $"    {item.Description}");

    Console.WriteLine();
}

Console.WriteLine("========================================");
Console.WriteLine();
Console.WriteLine("Press any key to exit...");
Console.ReadKey();