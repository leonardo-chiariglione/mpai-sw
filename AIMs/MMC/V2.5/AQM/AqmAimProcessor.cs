using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

using AIF.Controller;

using Mpai.Core;
using Mpai.Mmc.Edp;     // OllamaClient

namespace Mpai.Mmc.Aqm;

// MMC-AQM-V2.5 - Answer to Question Module (2026-11-25, N-50 e, for the composite MMC-MQA).
//
// The schema (the L2 of the composite MMC-MQA, slide 16): it answers a question from its Refined Text (OSD-BTO), the Text Descriptors Object (MMC-TDO), the Instance
// Identifier of the visual object the question is about (OSD-IID), where there is one, and the Intention (MMC-INT), and gives the answer as a Text Object (Machine Text).
//
// ENGINE: a language model (llama3.2:3b through Ollama, as Entity Dialogue Processing), told the question, what the object shown is (the label of the Instance
// Identifier and its confidence) and what is asked (the Intention), and asked for a short answer. The model sees no picture: what is known of the object is its identity.
// Where it cannot be reached, the answer says so (a Text Object), so the Module still answers.
public sealed class AqmAimProcessor : IAimProcessor
{
    private readonly string _textIn, _tdoIn, _iidIn, _intentionIn, _answerOut;
    private readonly Func<string, string, Task<string>> _ask;

    public AqmAimProcessor(string instanceId, AimPortReader ports, Func<string, string, Task<string>> ask)
    {
        InstanceId   = instanceId;
        _textIn      = ports.Input("OSD-BTO-V1.5");
        _tdoIn       = ports.InputOrDefault("MMC-TDO-V2.5", "");
        _iidIn       = ports.InputOrDefault("OSD-IID-V1.5", "");
        _intentionIn = ports.InputOrDefault("MMC-INT-V2.5", "");
        _answerOut   = ports.Output("OSD-BTO-V1.5");
        _ask         = ask;
    }

    public string InstanceId { get; }

    public const string Instruction =
        "You answer a question a person asks about an object they show. You do not see the object: you are told what it is. Answer in one or two short sentences, " +
        "in the language of the question, as a person would say it. If what you are told is not enough, say what the object is and that you cannot tell more.";

    public async Task<Message> ProcessAsync(Message message)
    {
        var question = Text(message, _textIn);
        if (string.IsNullOrWhiteSpace(question))
            return Message.Error(message.MessageId, InstanceId, "no question (Text Object) on the input Port");
        var prompt = Prompt(question, Value(message, _iidIn), Value(message, _intentionIn));
        string answer;
        try { answer = (await _ask(Instruction, prompt)).Trim(); }
        catch (Exception e) when (e is System.Net.Http.HttpRequestException or TaskCanceledException)
        { answer = "I cannot answer now: the language model cannot be reached."; AimLog.Write(InstanceId, "the language model cannot be reached: " + e.Message); }
        if (answer.Length == 0) answer = "I do not know.";
        return new Message
        {
            MessageId = message.MessageId, MessageType = message.MessageType,
            Ports = new Dictionary<string, string> { [_answerOut] = MpaiJson.ToJson(BasicTextObject.FromText(answer)) }
        };
    }

    // What the model is told: the question, the object (the label and confidence of the Instance Identifier) and what is asked (the Intention).
    public static string Prompt(string question, string? iidJson, string? intentionJson)
    {
        var lines = new List<string>();
        if (Label(iidJson) is { } label) lines.Add($"The object shown is: {label.Name}" + (label.Confidence is { } c ? $" (identified with confidence {c:0.00})." : "."));
        else lines.Add("No object was identified.");
        if (intentionJson is not null && (JsonNode.Parse(intentionJson)?["Intention"] as JsonArray)?.FirstOrDefault() is JsonObject i)
        {
            var parts = new[] { ("topic", "qtopic"), ("focus", "qfocus"), ("kind of answer", "qSAT"), ("what is asked for", "qLAT") }
                .Select(p => ((string?)i[p.Item2]) is { Length: > 0 } v ? $"{p.Item1}: {v}" : null).Where(p => p is not null);
            lines.Add("What is asked - " + string.Join("; ", parts) + ".");
        }
        lines.Add("Question: " + question.Trim());
        return string.Join("\n", lines);
    }

    // The label of an Instance Identifier (OSD-IID): InstanceLabel and its confidence, wherever the object carries them.
    public static (string Name, double? Confidence)? Label(string? iidJson)
    {
        if (string.IsNullOrWhiteSpace(iidJson)) return null;
        try
        {
            string? name = null; double? confidence = null;
            void Walk(JsonNode? n)
            {
                switch (n)
                {
                    case JsonObject o:
                        foreach (var (k, v) in o)
                        {
                            if (name is null && k == "InstanceLabel" && v is JsonValue s && s.TryGetValue<string>(out var label) && label.Length > 0) name = label;
                            else if (confidence is null && k == "LabelConfidenceLevel" && v is JsonValue d && d.TryGetValue<double>(out var level)) confidence = level;
                            else Walk(v);
                        }
                        break;
                    case JsonArray a: foreach (var x in a) Walk(x); break;
                }
            }
            Walk(JsonNode.Parse(iidJson));
            return name is null ? null : (name, confidence);
        }
        catch (System.Text.Json.JsonException) { return null; }
    }

    private static string? Value(Message m, string port) => port.Length > 0 && m.Ports.TryGetValue(port, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;
    private static string? Text(Message m, string port)
    {
        if (Value(m, port) is not { } json) return null;
        try { return MpaiJson.FromJson<BasicTextObject>(json)?.GetText(); } catch { return null; }
    }
}

public sealed class AqmPlugin : IAimPlugin
{
    public string AimName => "MMC-AQM-V2.5";
    public IAimProcessor Create(AimPortReader ports, IReadOnlyDictionary<string, string> settings)
    {
        var model = settings.TryGetValue("OllamaModel", out var m) && !string.IsNullOrWhiteSpace(m) ? m : "llama3.2:3b";
        var client = settings.TryGetValue("OllamaUrl", out var url) && !string.IsNullOrWhiteSpace(url) ? new OllamaClient(model, url) : new OllamaClient(model);
        return new AqmAimProcessor(AimName, ports, (system, user) => client.ChatAsync(system, user, 0.2));
    }
}
