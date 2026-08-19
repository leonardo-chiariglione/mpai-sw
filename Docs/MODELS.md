# Models used by MPAI-TST

None of these are distributed with the repository: about 6 GB in total, all
third party, each under its own licence. This is the complete list, with the
exact URLs the setup scripts use.

Every path below is appended to a Hugging Face repository as
`https://huggingface.co/<repo>/resolve/main/<path>`.

## 1. Speech recognition — MMC-ASR

**Binary.** whisper.cpp, MIT: <https://github.com/ggerganov/whisper.cpp>
Windows builds are on the Releases page; the executable used is `whisper-cli.exe`.

**Model**, from `ggerganov/whisper.cpp`:

| File | Size | Note |
|---|---|---|
| `ggml-small.bin` | 487,601,967 | **in use** — multilingual, copes with a real voice |
| `ggml-base.bin` | 147,951,465 | smaller, adequate for clean synthetic speech only |
| `ggml-medium.bin` | ~1.5 GB | better again, noticeably slower on CPU |

Direct: <https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin>

Do **not** use a `*.en.bin` model: those are English-only and the `-l` flag has
no effect on them.

Settings: `MMC-ASR-V2.5.ExecutablePath`, `MMC-ASR-V2.5.ModelPath`.

## 2. Translation — MMC-TTT

**Model.** M2M-100 418M, **MIT**, 100 languages. ONNX export from
`Xenova/m2m100_418M`, under `onnx/`:

| File | Size | |
|---|---|---|
| `onnx/encoder_model.onnx` | 1,133,698,852 | **in use**, full precision |
| `onnx/decoder_model.onnx` | 1,335,596,378 | **in use**, first decode step |
| `onnx/decoder_with_past_model.onnx` | 1,234,754,694 | **in use**, every later step |
| `onnx/encoder_model_quantized.onnx` | 287,856,370 | quantised alternative |
| `onnx/decoder_model_quantized.onnx` | 339,181,945 | quantised alternative |
| `onnx/decoder_with_past_model_quantized.onnx` | 313,662,487 | quantised alternative |

Browse: <https://huggingface.co/Xenova/m2m100_418M/tree/main/onnx>

Take the **unmerged** decoder pair, not `decoder_model_merged*`. The merged
export fails on the first decode step inside its own `optimum::if` cache-branch
switch; the pair has no such node.

**Tokeniser**, from `facebook/m2m100_418M`:

| File | Size |
|---|---|
| `sentencepiece.bpe.model` | 2,423,393 |
| `vocab.json` | 3,708,092 |
| `special_tokens_map.json` | 1,140 |

Browse: <https://huggingface.co/facebook/m2m100_418M/tree/main>

`special_tokens_map.json` matters more than its size suggests: it carries the
ordered language list from which `id("__it__") = vocabulary size + index` is
derived. A wrong order gives fluent output in the wrong language.

Settings: `TttEncoderModel`, `TttDecoderFirstModel`, `TttDecoderPastModel`,
`TttSpmModel`, `TttVocab`, `TttSpecialTokens`.

**Why not NLLB-200.** Better coverage, but **CC-BY-NC** — non-commercial, so it
cannot be the engine of BSD-3-Clause Reference Software. M2M-100 is MIT and names
languages by plain ISO 639-1 codes, which the Selector already carries.

## 3. Speech synthesis — MMC-TTS

**Binary.** Piper. `rhasspy/piper` was archived in October 2025; development
moved to <https://github.com/OHF-Voice/piper1-gpl>. GPL.

An older binary cannot read voices whose phoneme maps contain multi-codepoint
entries, which is why the Japanese voice fails with
`"ai" is not a single codepoint`.

**Voices**, from `rhasspy/piper-voices`. Each voice is TWO files: `<name>.onnx`
and `<name>.onnx.json`, both required.

| Language | Path under the repository |
|---|---|
| English | `en/en_US/...` — <https://huggingface.co/rhasspy/piper-voices/tree/main/en/en_US> |
| Italian | `it/it_IT/riccardo/x_low/it_IT-riccardo-x_low.onnx` |
| French | `fr/fr_FR/siwis/medium/fr_FR-siwis-medium.onnx` |
| German | `de/de_DE/thorsten/medium/de_DE-thorsten-medium.onnx` |
| Spanish | `es/es_ES/...` — <https://huggingface.co/rhasspy/piper-voices/tree/main/es/es_ES> |
| Chinese | `zh/zh_CN/huayan/medium/zh_CN-huayan-medium.onnx` |
| Japanese | `ja/ja_JA/hi_fi_captain/medium/ja_JA-hi_fi_captain-medium.onnx` |

Browse: <https://huggingface.co/rhasspy/piper-voices/tree/main>

Japanese is only on `main`; the `v1.0.0` tag lists 34 languages and does not
include it. Avoid the `mls` voices — they are trained on audiobook corpora and
are rough; `siwis`, `upmc`, `thorsten` and `huayan` are the good ones.

Settings: `PiperExecutable`, `VoiceModel`, `VoiceConfig`, then `Voice:<code>` and
`VoiceConfig:<code>` per language.

## 4. Getting them

`add-languages.ps1` fetches voices by asking the repository what it contains
rather than guessing filenames, and writes the settings lines. `full-precision.ps1`
swaps the translation models either way. Both are in the repository history.

## 5. Licences, together

| Component | Licence |
|---|---|
| whisper.cpp and its models | MIT |
| M2M-100 418M | MIT |
| Piper voices | MIT |
| Piper binary | GPL |
| MPAI reference software | BSD-3-Clause |

The GPL binary is invoked as a **separate process**, not linked, which is what
keeps it clear of the reference software's licence. NLLB-200 was rejected on
licence grounds alone.