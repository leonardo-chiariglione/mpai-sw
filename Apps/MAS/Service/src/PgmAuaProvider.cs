using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

using AIF.Controller;
using AIF.Store;

using Mpai.Aims.Tts;
using Mpai.Core;
using Mpai.Core.OSD;
using Mpai.Pgm;

namespace Mpai.Mas.Service;

// THE A-USER MODULE OF PGM-AUA ON THE INSTANCE (2026-11-26): 1PGM-ATU-V1.0-I01 and its Sub-AIMs, as the door App (AllApps\MMM\Server\DoorUseCase.cs) builds them when it runs
// its own Controller - the PGM-AUA AIMs with a language model (Ollama, kept loaded), the voice of A-User Formation (Piper, the Text-To-Speech of MMC, as a Speech Object whose
// data is a 16 kHz, 16 bit, mono WAV file), and PAF-PDX. The settings of the App (the door, the Persona, the Levels) are those of the instance's settings file.
public sealed class PgmAuaProvider(AmdStore store, string root, string llm = "llama3.2:3b") : IAimProvider
{
    private readonly PgmProvider pgm = new(store, new OllamaModel(llm) { KeepAlive = "3h" }, null, new PiperVoice(root));

    public bool CanCreate(string aimName) => pgm.CanCreate(aimName) || aimName == "1PAF-PDX-V1.6-I01";

    public IAimProcessor Create(string aimName, IReadOnlyDictionary<string, string> settings, AIF.SharedStorage.ISharedStorage? storage) =>
        aimName == "1PAF-PDX-V1.6-I01" ? new Mpai.Paf.Pdx.PdxAimProcessor(aimName, AimPortReader.Load(store, aimName)) : pgm.Make(aimName, settings);

    private sealed class PiperVoice(string root) : ISpeechSynthesiser
    {
        private readonly Lazy<PiperTtsAim> tts = new(() => TtsFactory.Create(ModelFiles.SettingsOf(root, "1MMC-TTS-V2.5-I01", new Dictionary<string, string>())));

        public async Task<JsonObject> SynthesiseAsync(string text)
        {
            var qualifier = new TextQualifier
            {
                TextQualifierID = Guid.NewGuid().ToString(),
                Attributes = new TextAttributes { Language = new Language { LanguageCode = "en", LanguageFormat = LanguageFormat.Iso639_1 } }
            };
            var wav = (await tts.Value.ProcessAsync(BasicTextObject.FromText(text, qualifier))).Data;
            if (wav.Length < 44) throw new InvalidOperationException("no voice spoke the words");
            var speech = BasicSpeechObject.FromData(Wav16k(wav), new SpeechQualifier
            {
                SpeechQualifierID = PgmAim.NewId("sq"),
                Format = new SpeechFormat
                {
                    ContentFormats = new SpeechContentFormats { RawData = new Pcm { SamplingFrequency = 16000, Precision = 16 } },
                    TransportFormats = new SpeechTransportFormats { FileFormat = SpeechFileFormat.Wav }
                }
            });
            return (JsonObject)JsonNode.Parse(MpaiJson.ToJson(speech))!;
        }

        // Piper's WAV as 16 kHz, 16 bit, mono (linear interpolation: enough for speech).
        private static byte[] Wav16k(byte[] wav)
        {
            var rate = BitConverter.ToInt32(wav, 24);
            var at = 12;
            while (at + 8 <= wav.Length && !(wav[at] == 'd' && wav[at + 1] == 'a' && wav[at + 2] == 't' && wav[at + 3] == 'a')) at += 8 + BitConverter.ToInt32(wav, at + 4);
            var pcm = at + 8 <= wav.Length ? wav[(at + 8)..Math.Min(wav.Length, at + 8 + BitConverter.ToInt32(wav, at + 4))] : [];
            if (rate != 16000)
            {
                var n = pcm.Length / 2; var m = (int)((long)n * 16000 / rate); var r = new byte[m * 2];
                for (var i = 0; i < m; i++)
                {
                    var x = (double)i * rate / 16000; var k = (int)x; var f = x - k;
                    double a = BitConverter.ToInt16(pcm, Math.Min(k, n - 1) * 2), b = BitConverter.ToInt16(pcm, Math.Min(k + 1, n - 1) * 2);
                    BitConverter.GetBytes((short)Math.Round(a + (b - a) * f)).CopyTo(r, i * 2);
                }
                pcm = r;
            }
            using var ms = new MemoryStream(); using var w = new BinaryWriter(ms);
            w.Write("RIFF"u8); w.Write(36 + pcm.Length); w.Write("WAVEfmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(16000); w.Write(32000); w.Write((short)2); w.Write((short)16);
            w.Write("data"u8); w.Write(pcm.Length); w.Write(pcm); w.Flush();
            return ms.ToArray();
        }
    }
}
