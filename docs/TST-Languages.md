# Adding and updating languages in MPAI-TST

Reference implementation, MMC-TST-V2.5. August 2026.

## The short answer

Nothing is trained, and nothing is fine-tuned. Adding a language is a download
and one settings line, and only for the **voice**.

| Function | AIM | Per-language work |
|---|---|---|
| Speech recognition | MMC-ASR | none — one multilingual Whisper, ~99 languages, selected at run time by `-l` |
| Translation | MMC-TTT | none — one M2M-100 model, 100 languages, selected by a token |
| Speech synthesis | MMC-TTS | **one voice file per language**, because Piper is one voice per model |

So the system can already **translate** into every language M2M-100 covers, today,
with no download. It can only **speak** the languages for which a Piper voice has
been configured.

That asymmetry is the single most important thing to understand here, and the
cause of most surprises: a language offered in the interface will always
translate, and may or may not be speakable.

## Adding a language

```powershell
& '.\add-languages.ps1' -Languages es,pt,nl
```

For each code the script:

1. asks the Hugging Face API what `rhasspy/piper-voices` contains for that
   language, rather than guessing a filename;
2. picks a speaker by a known quality order, then prefers `medium` quality;
3. downloads `<voice>.onnx` and `<voice>.onnx.json` into the Piper voices folder;
4. adds two lines to `AIMs/aim-settings.json`:

```json
"Voice:es":       "D:\\AI\\Models\\Piper\\voices\\es_ES-...\\es_ES-....onnx",
"VoiceConfig:es": "D:\\AI\\Models\\Piper\\voices\\es_ES-...\\es_ES-....onnx.json"
```

`-Force` replaces a voice already configured. A language with no voice in the
repository is reported and skipped; it still translates.

### By hand

Download the two files from `https://huggingface.co/rhasspy/piper-voices`, put
them anywhere, and add the two settings lines above. There is nothing else.

## Language codes

Two-letter **ISO 639-1**, lower case. This is what the Selector carries, what
M2M-100 uses (`__it__`, `__ja__`), and what Whisper takes for `-l`.

- `ja` is Japanese. `jp` is a country and matches nothing.
- `zh` is Chinese. The voice file may be `zh_CN-...`; the code is still `zh`.
- The voice's own `.onnx.json` declares its language, and `TtsFactory` keys the
  voice map on the **first two characters** of that declaration. A voice
  declaring `es_ES` is therefore reached by `es`.

## How a language travels through the composite

```
Language Selector ──────────────────────────► MMC-TTT
    (input and output codes)                     │
                                                 │ stamps the OUTPUT language
                                                 │ on the Text Qualifier
                                                 ▼
Input Speech ──► MMC-SOA ──► MMC-ASR ──► ... ──► MMC-TTS ──► voice selected by
   Speech Qualifier    reads the input              the Text Qualifier
   carries the input   language from the
   language            Speech Qualifier
```

Two consequences worth knowing:

- **MMC-TST has no Input Language Selector.** The input language rides on the
  Speech Qualifier, which is why a caller sending speech must stamp it there —
  the microphone cannot know what is about to be spoken into it.
- **MMC-TTS never sees the Selector.** It chooses its voice from the Text
  Qualifier that MMC-TTT stamps. A translation with no language stamped would be
  spoken in the default voice, which is how "correct text, wrong accent" happens.

## Testing a newly added language

Type before you speak. Typing exercises TTT and TTS only; speaking adds SOA and
ASR, and a failure then has four possible homes instead of two.

1. **Type** a sentence, `en` → the new language. Correct text means translation
   works. Audible, unaccented speech means the voice works.
2. **Speak** a sentence in the new language → `en`. Watch the `heard:` line: it
   is what MMC-ASR made of your voice, and it separates mis-hearing from
   mis-translating.

In the log:

```
[MMC-TTS-V2.5] voices configured: en, it, fr, de, es, zh, ja
[MMC-ASR-V2.5] heard: ...
[AOA] recorded peak -18.2 dBFS
```

The first line is the definitive list of what can be spoken.

## Known limits

**A voice may be refused by the Piper binary.** The Japanese `hi_fi_captain`
voice uses multi-codepoint phonemes that older Piper builds reject:

```
[MMC-TTS-V2.5] the ja-JA voice failed: "ai" is not a single codepoint
```

The voice and the language are fine; the binary predates the format.
`rhasspy/piper` was archived in October 2025 and development moved to
`OHF-Voice/piper1-gpl`. Until the binary is updated, such a language is
text-only. MMC-TTS reports it and stays silent rather than substituting a voice
that cannot read the script.

**No register control.** German `Sie` becomes Italian `tu`. M2M-100 has no notion
of formality, and nothing in the input tells it which is wanted — the information
is lost when the sentence is parsed into meaning. Fixing this needs a `Formality`
field on the Selector *and* a model that honours it; adding the field alone would
give a Selector that nothing reads.

**Quality varies independently of coverage.** Both models are far stronger on
high-resource languages. Adding a language is trivial; being confident it is good
needs listening.

**Quantised versus full-precision translation.** The quantised M2M-100 weights
cost most on agreement, gender and word order across long sentences. The
full-precision models are about 3.7 GB instead of 940 MB, four times the memory,
and slower on CPU — and noticeably better. `full-precision.ps1` swaps them;
`-Quantised` swaps back. No code changes either way.

## Where the settings live

`AIMs/aim-settings.json`, section `MMC-TTS-V2.5`:

| Key | Meaning |
|---|---|
| `PiperExecutable` | the Piper binary |
| `VoiceModel`, `VoiceConfig` | the **default** voice, used when a language has none |
| `Voice:<code>`, `VoiceConfig:<code>` | one pair per language |

Paths are absolute. A checkout on another machine needs them repointed — see the
architecture note on portability.