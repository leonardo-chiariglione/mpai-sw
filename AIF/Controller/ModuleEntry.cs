using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace AIF.Controller;

// THE STANDARD ENTRY OF AN IMPLEMENTATION, AND THIS ENVIRONMENT'S C-COMPATIBLE BINDING OF IT.
//
// WHAT THE STANDARD SAYS. The Implementation an AIM Metadata names (BinaryName, built for one Architecture and one OperatingSystem) has an entry of the type module_t - typedef
// error_t *(module_t)() - which the Controller finds and calls; the AIM reaches its Ports through the abstract MPAI_AIFM_ functions. The standard fixes no binary binding.
//
// WHAT THIS ENVIRONMENT CHOOSES (OUR choice, not the standard's; written for a reader who has not seen this code in docs\AIF-Entry-Binding.md). The binding is plain C: nothing of .NET
// crosses it - no delegate, exception or object reference; only functions given as pointers, bytes, integers, UTF-8 text and structs of fixed layout, with integers as error codes. So an
// AIM may be written in any language that can do that, and another Controller can run the same packages. The entry is Module(const MpaiApi *api): it receives the table of functions
// below (our environment's extension of module_t, which takes nothing) and returns an int32 (0 = OK).
//
//   Table (MpaiApi, version 1, 56 bytes, every pointer 8 bytes):  uint32 size; uint32 version; then the functions
//     register_process(void *user, MpaiProcessFn fn)                      the AIM gives the function the Controller calls for each exchange
//     setting(name, name_len, buf, cap, int32 *needed)                    a setting of the AIM, UTF-8
//     port_count(direction, int32 *count)                                 direction 0 = Input Ports, 1 = Output Ports
//     port_key(direction, index, buf, cap, int32 *needed)                 the key of a Port, "DataType#PortNumber", UTF-8
//     emit(void *call, key, key_len, data, data_len)                      give a datum at an Output Port (during a call of the process function only)
//     set_reason(void *call, text, text_len)                              say why the call failed (during a call only)
//   MpaiProcessFn: int32 fn(void *user, void *call, const MpaiDatum *inputs, int32 input_count);  MpaiDatum { const uint8_t *key; const uint8_t *data; int32 key_len; int32 data_len; }
//
// For a .NET Implementation the entry is a public static method named Module that returns an int and takes one IntPtr - the address of that table - and the functions are called through
// the pointers (unmanaged, C calling convention). An entry that takes nothing (as module_t) is also started: having no table, it cannot be given data, and says so.
public static class ModuleEntry
{
    public const string Name = "Module";
    public const int Ok = 0, Failed = 1, BadArgument = 2, BufferTooSmall = 3, NotFound = 4, NotInCall = 5;

    // The entry of the assembly, or null where it has none of the shape above.
    public static MethodInfo? Find(Assembly implementation)
    {
        foreach (var type in Types(implementation).Where(t => t.IsVisible && !t.IsGenericTypeDefinition))
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
                if (method.Name == Name && HasShape(method)) return method;
        return null;
    }

    private static bool HasShape(MethodInfo method)
    {
        if (method.ReturnType != typeof(int)) return false;
        var parameters = method.GetParameters();
        return parameters.Length == 0 || (parameters.Length == 1 && (parameters[0].ParameterType == typeof(IntPtr) || parameters[0].ParameterType == typeof(nint)));
    }

    // The types of the assembly that can be loaded: an assembly one of whose references is missing still shows the others.
    public static IEnumerable<Type> Types(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t is not null)!; }
    }

    // The AIM built from the entry: the table is made, the entry is called with its address, and what the AIM registered is what runs.
    public static IAimProcessor Start(string instanceId, MethodInfo entry, AimPortReader ports, IReadOnlyDictionary<string, string> settings)
    {
        var processor = new EntryProcessor(instanceId, entry.DeclaringType?.Assembly.GetName().Name ?? instanceId, ports, settings);
        var takesTable = entry.GetParameters().Length == 1;
        processor.GotTable = takesTable;
        object? code;
        try { code = entry.Invoke(null, takesTable ? new object[] { processor.Table } : Array.Empty<object>()); }
        catch (TargetInvocationException e) { throw new InvalidOperationException($"{instanceId}: the entry {Name}() of {processor.Binary} failed: {e.InnerException?.Message ?? e.Message}"); }
        var error = Convert.ToInt32(code);
        if (error != 0) throw new InvalidOperationException($"{instanceId}: the entry {Name}() of {processor.Binary} returned the error {error}.");
        return processor;
    }

    // ---- the functions given to the AIM: C calling convention, no .NET type in the signature ----------------------------------------------------------------------------------------

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int RegisterProcessFn(IntPtr user, IntPtr process);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SettingFn(IntPtr name, int nameLen, IntPtr buffer, int capacity, IntPtr needed);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int PortCountFn(int direction, IntPtr count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int PortKeyFn(int direction, int index, IntPtr buffer, int capacity, IntPtr needed);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int EmitFn(IntPtr call, IntPtr key, int keyLen, IntPtr data, int dataLen);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetReasonFn(IntPtr call, IntPtr text, int textLen);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ProcessFn(IntPtr user, IntPtr call, IntPtr inputs, int inputCount);

    private sealed class Call { public List<(string Key, string Json)> Outputs { get; } = new(); public string? Reason { get; set; } }

    private sealed class EntryProcessor : IAimProcessor
    {
        private readonly string instanceId;
        private readonly AimPortReader ports;
        private readonly IReadOnlyDictionary<string, string> settings;
        private readonly object gate = new();
        private readonly Dictionary<long, Call> calls = new();
        private long nextCall;
        private ProcessFn? process; private IntPtr user;
        private readonly List<Delegate> keep = new();      // the functions given to the AIM live as long as the AIM does

        public EntryProcessor(string instanceId, string binary, AimPortReader ports, IReadOnlyDictionary<string, string> settings)
        {
            this.instanceId = instanceId; Binary = binary; this.ports = ports; this.settings = settings;
            // The table: uint32 size, uint32 version, then six function pointers (56 bytes), in the order the document gives.
            var table = Marshal.AllocHGlobal(56);
            Marshal.WriteInt32(table, 0, 56); Marshal.WriteInt32(table, 4, 1);
            Put(table, 8, (RegisterProcessFn)RegisterProcess); Put(table, 16, (SettingFn)Setting); Put(table, 24, (PortCountFn)PortCount);
            Put(table, 32, (PortKeyFn)PortKey); Put(table, 40, (EmitFn)Emit); Put(table, 48, (SetReasonFn)SetReason);
            Table = table;
        }

        private void Put(IntPtr table, int offset, Delegate function) { keep.Add(function); Marshal.WriteIntPtr(table, offset, Marshal.GetFunctionPointerForDelegate(function)); }

        public string InstanceId => instanceId;
        public string Binary { get; }
        public IntPtr Table { get; }
        public bool GotTable { get; set; }

        private int RegisterProcess(IntPtr userData, IntPtr function)
        {
            if (function == IntPtr.Zero) return BadArgument;
            user = userData; process = Marshal.GetDelegateForFunctionPointer<ProcessFn>(function);
            return Ok;
        }

        // UTF-8 into the AIM's buffer; the length needed is always given, and a buffer too small is said.
        private static int Give(string text, IntPtr buffer, int capacity, IntPtr needed)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            if (needed != IntPtr.Zero) Marshal.WriteInt32(needed, bytes.Length);
            if (bytes.Length > capacity || (bytes.Length > 0 && buffer == IntPtr.Zero)) return BufferTooSmall;
            if (bytes.Length > 0) Marshal.Copy(bytes, 0, buffer, bytes.Length);
            return Ok;
        }

        private static string Text(IntPtr data, int length)
        {
            if (length <= 0 || data == IntPtr.Zero) return "";
            var bytes = new byte[length]; Marshal.Copy(data, bytes, 0, length);
            return Encoding.UTF8.GetString(bytes);
        }

        private int Setting(IntPtr name, int nameLen, IntPtr buffer, int capacity, IntPtr needed) =>
            settings.TryGetValue(Text(name, nameLen), out var value) ? Give(value, buffer, capacity, needed) : NotFound;

        private int PortCount(int direction, IntPtr count)
        {
            if (count == IntPtr.Zero || direction is not (0 or 1)) return BadArgument;
            Marshal.WriteInt32(count, (direction == 0 ? ports.InputKeys : ports.OutputKeys).Count);
            return Ok;
        }

        private int PortKey(int direction, int index, IntPtr buffer, int capacity, IntPtr needed)
        {
            if (direction is not (0 or 1)) return BadArgument;
            var keys = direction == 0 ? ports.InputKeys : ports.OutputKeys;
            return index < 0 || index >= keys.Count ? BadArgument : Give(keys[index], buffer, capacity, needed);
        }

        private int Emit(IntPtr call, IntPtr key, int keyLen, IntPtr data, int dataLen)
        {
            lock (gate)
            {
                if (!calls.TryGetValue(call.ToInt64(), out var c)) return NotInCall;
                if (keyLen <= 0 || dataLen < 0) return BadArgument;
                c.Outputs.Add((Text(key, keyLen), Text(data, dataLen)));          // copied: the memory is the AIM's again when this returns
                return Ok;
            }
        }

        private int SetReason(IntPtr call, IntPtr text, int textLen)
        {
            lock (gate)
            {
                if (!calls.TryGetValue(call.ToInt64(), out var c)) return NotInCall;
                c.Reason = Text(text, textLen);
                return Ok;
            }
        }

        public Task<Message> ProcessAsync(Message message)
        {
            if (process is null)
                return Task.FromResult(Message.Error(message.MessageId, instanceId, GotTable
                    ? $"{instanceId}: the entry {Name}() of {Binary} registered no function to process data (register_process)."
                    : $"{instanceId}: the entry {Name}() of {Binary} takes no table of functions, so it cannot be given data in this environment."));
            var inputs = message.Ports.Where(p => !string.IsNullOrWhiteSpace(p.Value)).ToList();
            var allocated = new List<IntPtr>();
            var array = Marshal.AllocHGlobal(Math.Max(24 * inputs.Count, 24));
            long id; var call = new Call();
            try
            {
                // MpaiDatum, 24 bytes: key pointer, data pointer, key_len, data_len - the memory is the Controller's and is valid for this call only.
                for (var i = 0; i < inputs.Count; i++)
                {
                    var key = Encoding.UTF8.GetBytes(inputs[i].Key); var data = Encoding.UTF8.GetBytes(inputs[i].Value);
                    var k = Marshal.AllocHGlobal(Math.Max(key.Length, 1)); var d = Marshal.AllocHGlobal(Math.Max(data.Length, 1));
                    allocated.Add(k); allocated.Add(d);
                    Marshal.Copy(key, 0, k, key.Length); Marshal.Copy(data, 0, d, data.Length);
                    var at = array + 24 * i;
                    Marshal.WriteIntPtr(at, 0, k); Marshal.WriteIntPtr(at, 8, d); Marshal.WriteInt32(at, 16, key.Length); Marshal.WriteInt32(at, 20, data.Length);
                }
                lock (gate) { id = ++nextCall; calls[id] = call; }
                int code;
                lock (this)                                                          // one exchange at a time for one AIM
                {
                    try { code = process(user, new IntPtr(id), array, inputs.Count); }
                    finally { lock (gate) calls.Remove(id); }
                }
                if (code != 0)
                    return Task.FromResult(Message.Error(message.MessageId, instanceId, $"{instanceId}: {(string.IsNullOrEmpty(call.Reason) ? $"{Binary} returned the error {code}" : call.Reason + $" (error {code})")}."));
                var outputs = call.Outputs.GroupBy(o => o.Key).ToDictionary(g => g.Key, g => g.Last().Json);
                return Task.FromResult(new Message { MessageId = message.MessageId, MessageType = "Result", Payload = outputs.Values.FirstOrDefault() ?? "", Ports = outputs });
            }
            catch (Exception e) { return Task.FromResult(Message.Error(message.MessageId, instanceId, $"{instanceId}: {e.Message}")); }
            finally { foreach (var p in allocated) Marshal.FreeHGlobal(p); Marshal.FreeHGlobal(array); }
        }
    }
}
