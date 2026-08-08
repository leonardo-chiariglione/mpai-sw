var asm = new ASM.Core.ASM();

Console.WriteLine("ASM V0.1");
Console.WriteLine("Type 'exit' to quit.");
Console.WriteLine();

while (true)
{
    Console.Write("ASM> ");

    var instruction = Console.ReadLine();

    if (instruction == null)
    {
        continue;
    }

    if (instruction.Equals(
        "exit",
        StringComparison.OrdinalIgnoreCase))
    {
        break;
    }

    Console.WriteLine();
    Console.WriteLine(asm.Execute(instruction));
    Console.WriteLine();
}