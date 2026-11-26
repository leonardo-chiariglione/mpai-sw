using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;

using AIF.Controller;
using AIF.Store;

using Mpai.Aif.Api;
using Mpai.Mas.Server;

namespace Mpai.Mas.Service;

// Adapts the Controller API to what the MAS server needs.
//
// THIS IS THE ONLY PLACE THE TWO MEET. ControllerApi speaks Datum(DataType,
// PortNumber, Json); the MAS server speaks the boundary key "DataType#Number".
// Neither had to change: the translation is here, in one class, and it is the
// only code in the server half that knows an application exists.
internal sealed class ControllerApiRunner : IModuleRunner, IContinuousModuleRunner, IPausableModuleRunner, IInstanceModuleRunner
{
    // ONE MODULE FOR EACH OF ITS USERS (2026-11-26). A Module Instance a client starts (MODULE/Start) runs in a Controller of its own, made by makeController as the Service's
    // own is made: its own AIMs, its own sessions and its own storage, so two people using one App do not read each other's outputs. Every call of this runner names either a
    // Module (the Service's own Modules, started at load and warmed up) or a Module Instance (the key the server gave it); Of() says which Controller and which Module it is.
    private readonly Func<ControllerApi>? makeController;
    private readonly ConcurrentDictionary<string, (ControllerApi Api, int Id, string Name)> instances = new(StringComparer.Ordinal);

    // What a new Controller of a Module Instance must hold before it is used (the Shared Storage of its Module, where the Service keeps it).
    public Action<ControllerApi, int, string>? Prepare { get; set; }

    private (ControllerApi Api, int Id, string Name) Of(string key) =>
        instances.TryGetValue(key, out var own) ? own : (north, IdOf(key), key);

    public string? StartInstance(string moduleName, string instance)
    {
        if (makeController is null) return Start(moduleName);
        ControllerApi? api = null;
        try
        {
            api = makeController();
            var error = api.StartFlow(moduleName, out var moduleId);
            if (error != AifError.OK)
            {
                var said = api.Controller.LastRefusal is { Length: > 0 } why ? $"{error}: {why}" : error.ToString();
                Console.WriteLine($"[Controller] {moduleName} not started for {instance}: {said}");
                api.Dispose();
                return said;
            }
            Prepare?.Invoke(api, moduleId, moduleName);
            instances[instance] = (api, moduleId, moduleName);
            Started?.Invoke(moduleName);
            return null;
        }
        catch (Exception failure)
        {
            Console.WriteLine($"[Controller] {moduleName} not started for {instance}: {failure}");
            api?.Dispose();
            return failure.Message;
        }
    }

    // A Module whose L3 says Execution Continuous is written to and read from as the Controller API has it, not run an exchange at a time.
    private readonly ConcurrentDictionary<string, bool> continuous = new(StringComparer.Ordinal);

    public bool IsContinuous(string moduleName) => continuous.GetOrAdd(Of(moduleName).Name, name =>
    {
        var identifier = store.FindByAimName(name);
        if (identifier is null) return false;
        var amd = store.GetAMD(identifier);
        return amd.RootElement.TryGetProperty("Execution", out var execution) && string.Equals(execution.GetString(), "Continuous", StringComparison.OrdinalIgnoreCase);
    });

    private static (string Type, int Number) Split(string key)
    {
        var hash = key.LastIndexOf('#');
        return (hash > 0 ? key[..hash] : key, hash > 0 && int.TryParse(key[(hash + 1)..], out var n) ? n : 1);
    }

    public string? Write(string moduleName, string key, string json)
    {
        var (type, number) = Split(key);
        var (api, id, _) = Of(moduleName);
        var error = api.InputWrite(id, type, number, json, 30_000);
        return error == AifError.OK ? null : error.ToString();
    }

    public (string? Json, string Outcome) Read(string moduleName, string key, int timeoutMs)
    {
        var (type, number) = Split(key);
        var (api, id, _) = Of(moduleName);
        var read = api.OutputRead(id, type, number, timeoutMs);
        return read.Error == AifError.OK ? (read.Json, "OK") : (null, read.Error.ToString());
    }

    private readonly ControllerApi north;
    private readonly AmdStore store;

    // The Ports of each Module, read once from its AMD.
    private readonly ConcurrentDictionary<string, IReadOnlyList<BoundaryPort>> ports =
        new(StringComparer.Ordinal);

    // ONE MODULE, MANY PEOPLE. The Controller API runs one instance of each Module,
    // and every client that starts it shares that instance. So a Module is
    // stopped only when the last of those who started it has stopped it - the
    // Service's own start at load time counts as one, and is never undone, so a
    // person pressing Stop ends their own App and nobody else's.
    private readonly Dictionary<string, int> holders = new(StringComparer.Ordinal);

    // THE MODULE_ID THE CONTROLLER RETURNED for each Module started (Basic API 2.2): every call after Start addresses the Module by it. -1 for one not started.
    private readonly ConcurrentDictionary<string, int> ids = new(StringComparer.Ordinal);
    public string? Pause(string moduleName)
    {
        var (api, id, _) = Of(moduleName);
        if (id < 0) return $"{moduleName} is not running.";
        var error = api.Pause(id);
        return error == AifError.OK ? null : error.ToString();
    }

    public string? Resume(string moduleName)
    {
        var (api, id, _) = Of(moduleName);
        if (id < 0) return $"{moduleName} is not running.";
        var error = api.Resume(id);
        return error == AifError.OK ? null : error.ToString();
    }

    public int IdOf(string moduleName) => ids.TryGetValue(moduleName, out var id) ? id : -1;

    // And the shared instance runs one exchange at a time: its AIMs' lifecycles
    // are reset at each run, so two runs of one Module overlapping would cancel
    // each other. Different Modules still run side by side.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> turns =
        new(StringComparer.Ordinal);

    public ControllerApiRunner(
        ControllerApi north,
        AmdStore store,
        Func<ControllerApi>? makeController = null)
    {
        this.north = north;
        this.store = store;
        this.makeController = makeController;
    }

    public IReadOnlyList<BoundaryPort> PortsOf(
        string moduleName)
    {
        if (ports.TryGetValue(moduleName, out var known)) return known;

        var identifier = store.FindByAimName(moduleName)
            ?? throw new InvalidOperationException(
                   $"No AMD for Module '{moduleName}'.");

        // Not disposed: the document belongs to the store, which keeps it.
        var amd = store.GetAMD(identifier);

        var read = BoundaryPorts.FromAmd(amd.RootElement);
        ports[moduleName] = read;
        return read;
    }

    // Said once a Module has been started (by whoever asked): what the Controller itself does then, such as binding the Module's Shared Storage where it keeps it.
    public event Action<string>? Started;

    public string? Start(
        string moduleName)
    {
        // A MODULE'S OWN FAULT MUST NOT STOP THE OTHERS. Building an AIM can throw -
        // a missing model file, for instance - and that exception is this one
        // Module's problem alone: the Service still has four other Modules to try,
        // and a machine that hosts only some AIMs (a remote Sub-AIM's own machine,
        // say) should say which failed rather than never start at all.
        try
        {
            var error = north.StartFlow(moduleName, out var moduleId);
            if (error != AifError.OK)
            {
                var said = north.Controller.LastRefusal is { Length: > 0 } why ? $"{error}: {why}" : error.ToString();
                Console.WriteLine($"[Controller] {moduleName} not started: {said}");
                return said;
            }
            ids[moduleName] = moduleId;

            lock (holders)
                holders[moduleName] = holders.GetValueOrDefault(moduleName) + 1;
            Started?.Invoke(moduleName);
            return null;
        }
        catch (Exception failure)
        {
            Console.WriteLine($"[Controller] {moduleName} not started: {failure}");
            return failure.Message;
        }
    }

    public void Stop(
        string moduleName)
    {
        // A Module Instance with a Controller of its own: its Module stopped and its Controller ended.
        if (instances.TryRemove(moduleName, out var own))
        {
            var ownTurn = turns.GetOrAdd(moduleName, _ => new SemaphoreSlim(1, 1));
            ownTurn.Wait();
            try { own.Api.StopFlow(own.Id); own.Api.Dispose(); }
            catch (Exception e) { Console.WriteLine($"[Controller] stopping {own.Name} ({moduleName}): {e.Message}"); }
            finally { ownTurn.Release(); turns.TryRemove(moduleName, out _); }
            return;
        }
        lock (holders)
        {
            if (!holders.TryGetValue(moduleName, out var count)) return;
            if (count > 1) { holders[moduleName] = count - 1; return; }
            holders.Remove(moduleName);
        }

        // The last holder: no run of it can be in progress for anyone else.
        var turn = turns.GetOrAdd(moduleName, _ => new SemaphoreSlim(1, 1));
        turn.Wait();
        try { north.StopFlow(IdOf(moduleName)); ids.TryRemove(moduleName, out _); }
        finally { turn.Release(); }
    }

    public RunResult Run(
        string moduleName,
        IReadOnlyDictionary<string, string> inputs) => Run(moduleName, inputs, null);

    public void SessionEnded(string session) => north.SessionEnded(session);

    public RunResult Run(
        string moduleName,
        IReadOnlyDictionary<string, string> inputs,
        string? session)
    {
        var data = new List<ControllerApi.Datum>();
        foreach (var pair in inputs)
        {
            var hash = pair.Key.LastIndexOf('#');
            var type = hash > 0 ? pair.Key.Substring(0, hash) : pair.Key;
            var num  = hash > 0 && int.TryParse(pair.Key.Substring(hash + 1), out var n) ? n : 1;
            data.Add(new ControllerApi.Datum(type, num, pair.Value));
        }

        var turn = turns.GetOrAdd(moduleName, _ => new SemaphoreSlim(1, 1));
        ControllerApi.Result result;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        turn.Wait();
        var waited = clock.ElapsedMilliseconds;
        var (api, id, _) = Of(moduleName);
        try { result = session is null ? api.Advance(id, data) : api.Advance(id, data, session); }
        finally
        {
            turn.Release();
            AIF.Controller.TurnTrace.Line($"turn {moduleName} ({string.Join(", ", inputs.Keys)}): waited {waited} ms for the Module, ran {clock.ElapsedMilliseconds - waited} ms");
        }

        if (result.Error != AifError.OK)
            return new RunResult { Error = result.Error.ToString() };

        var outputs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var datum in result.Outputs)
            outputs[datum.DataType + "#" + datum.PortNumber] = datum.Json;

        return new RunResult { Outputs = outputs };
    }
}