using System;
using System.Text;

namespace Foreign;

// The whole AIM: the entry of the standard, Module, and what it does with the table of C functions the Controller of this environment hands it (docs\AIF-Entry-Binding.md). It echoes what arrives
// at its first Input Port at its first Output Port, inside {"echo": ..., "setting": ...}. It uses only pointers, bytes and integers: nothing of the Controller's, nothing of .NET crosses.
public static unsafe class Aim
{
#if NO_TABLE
    public static int Module() => 0;
#else
    private static byte** table;       // the MpaiApi: size, version, then the function pointers (8 bytes each from offset 8)

    // MpaiDatum: const uint8_t *key; const uint8_t *data; int32 key_len; int32 data_len
    private struct Datum { public byte* Key; public byte* Data; public int KeyLen; public int DataLen; }

    public static int Module(IntPtr api)
    {
        table = (byte**)api;
        var registerProcess = (delegate* unmanaged[Cdecl]<void*, void*, int>)table[1];
        return registerProcess(null, (void*)(delegate* unmanaged[Cdecl]<void*, void*, Datum*, int, int>)&Process);
    }

    private static string Key(int direction)
    {
        var portKey = (delegate* unmanaged[Cdecl]<int, int, byte*, int, int*, int>)table[4];
        var buffer = stackalloc byte[256]; int needed;
        return portKey(direction, 0, buffer, 256, &needed) == 0 ? Encoding.UTF8.GetString(buffer, needed) : "";
    }

    [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    private static int Process(void* user, void* call, Datum* inputs, int count)
    {
        var inputKey = Key(0); var outputKey = Key(1);
        string? given = null;
        for (var i = 0; i < count; i++)
            if (Encoding.UTF8.GetString(inputs[i].Key, inputs[i].KeyLen) == inputKey) given = Encoding.UTF8.GetString(inputs[i].Data, inputs[i].DataLen);
        if (given is null)
        {
            var why = Encoding.UTF8.GetBytes("nothing at the Input Port " + inputKey);
            fixed (byte* w = why) ((delegate* unmanaged[Cdecl]<void*, byte*, int, int>)table[6])(call, w, why.Length);
            return 7;
        }
        var setting = (delegate* unmanaged[Cdecl]<byte*, int, byte*, int, int*, int>)table[2];
        var name = Encoding.UTF8.GetBytes("Greeting"); var value = stackalloc byte[64]; int needed = 0; var greeting = "";
        fixed (byte* n = name) if (setting(n, name.Length, value, 64, &needed) == 0) greeting = Encoding.UTF8.GetString(value, needed);
        var output = Encoding.UTF8.GetBytes("{\"echo\":" + given + ",\"setting\":\"" + greeting + "\"}");
        var key = Encoding.UTF8.GetBytes(outputKey);
        fixed (byte* k = key) fixed (byte* d = output) return ((delegate* unmanaged[Cdecl]<void*, byte*, int, byte*, int, int>)table[5])(call, k, key.Length, d, output.Length);
    }
#endif
}
