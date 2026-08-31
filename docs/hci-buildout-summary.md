# HCI Middleware Build-out â€” Consolidated Record

*A catalogue of the additions made in the HCI middleware build-out: the AIMs (Modules),
data types, schema changes, applications, and the HCI API. Grouped by category; each entry
notes what it is and its status. Companion to `track-changes.md` (which holds the
chronological, decision-level detail).*

---

## 1. The pipeline, end to end

A person is captured, perceived, understood, felt, answered, and answered *back* â€” audibly
and visibly â€” on local hardware:

```
camera + microphone
  â†’ perceive : AV scene, identity (FIR/SIR/IDR), speechâ†’text (ASR), NLU
  â†’ FEEL     : Personal Status Extraction â€” wav2vec2 (voice) + HSEmotion (face) + NLU (text)
               â†’ EntityPersonalStatus
  â†’ RESPOND  : Entity Dialogue Processing â€” local LLM (Ollama) â†’ machine text + machine Personal Status
  â†’ PRODUCE  : Response and Scene Rendering â€” Machine Speech + Machine Face Descriptors (lip-sync)
  â†’ DELIVER  : the User Agent â€” SOD (speech â†’ loudspeaker) + 3OD (avatar â†’ screen)
```

Exposed to applications through the **HCI API** (M3152); the first application, the In-Cabin
conversational CAV, is a thin client of it.

---

## 2. AIMs (Modules) â€” new / substantially changed

| Code | Name | What it does | Status |
|---|---|---|---|
| MMC-EDP-V2.5 | Entity Dialogue Processing | Composes the turn (text, Personal Status, identity, scene) into a prompt for a local LLM (Ollama); produces the machine's response text, Personal Status, and updated Summary. | Built, proven |
| PAF-PSD-V1.6 | Personal Status De-multiplexing | Splits the machine's EntityPersonalStatus into Speech/Face/Gesture Personal Status (inverse of PSM). | Built |
| MMC-TTS-V2.5 | Text-To-Speech | Text â†’ Speech (Piper). Dual-typed I/O: text `[OSD-BTO, OSD-TXO]`, speech `[OSD-BSO, OSD-SPO]`. Fixed a latent bug (output was OSD-AUO/audio â†’ speech). | Updated |
| MMC-SOD-V2.5 | Speech Object Delivery | Delivers a Speech Object *as speech* via its own `ISpeechDeliveryAim` (no demotion to audio; independent of AOD). Dual-typed. | Refactored |
| PAF-GFD-V1.6 | Generative Face Description | Face Personal Status â†’ expression Action Units (EM-FACS); Text â†’ espeak-ng phonemes â†’ visemes; Machine Speech â†’ timing/envelope â†’ the **Face Descriptors animation timeline** (expression + lip-sync). | Built, proven |
| OSD-3OD-V1.5 | 3D Model Object Delivery | Delivers the 3D scene to a renderer: the model (ModelObject) + the animation streams (FaceAnimation/BodyAnimation), each on its own port. | Built, proven |
| PAF-GBD-V1.6 | Generative Body Description | Body counterpart of PAF-GFD (Gesture PS + Text + Speech â†’ Body Descriptors / BVH). | **Deferred** (named; scaffolding partly in place) |

*Analysis siblings already in the repo: PAF-EFD (Entity Face Description), PAF-EBD (Entity Body
Description, BVH). PAF-GFD/PAF-GBD are the generative counterparts.*

---

## 3. Data types & schema changes

| Type | What | Status |
|---|---|---|
| OSD-B3O â€” Basic 3D Model Object | The static 3D model (glTF/GLB) as an Object; sibling of BasicSpeechObject/BasicAudioObject. C# type authored in `Mpai.Core.OSD`; the empty misfiled stub removed. | Real (was a stub) |
| OSD-3DO â€” 3D Model Object | The composite 3D Model Object (contains Basic 3D Model Objects + children). | Real |
| PAF-FDO â€” Face Descriptors Object | **Enhanced to an interoperable animation timeline**: each `FaceDescriptorsData` item now carries an optional `SimpleTime`, so the array is a sequence of face descriptors over time â€” a first-class, format-independent timing field *in* the FDO (not buried in the data, not qualifier-dependent). The same type serves analysis (embedding, one item) and generation (AU/viseme timeline, many items). | Schema changed |
| FaceActionUnits / EM-FACS | FACS Action Unit descriptor + the EM-FACS emotionâ†’AU mapping (the six basic emotions) + a viseme (phonemeâ†’mouth-AU) mapping; AU18 (lip pucker) added. | Built |
| PersonalStatus family (Emotion/CognitiveState/SocialAttitude, TPS/SPS/FPS/GPS, EPS) | Label+degree factor model; the analysis and generative sides both use it. | (Prior/settled) |

**3D data architecture (settled by reasoning + the qualifier test):** a 3D avatar splits into
a **static model** (OSD-B3O/3DO â€” glTF/FBX/USD formats) and its **animation** (the existing
first-class PAF-FDO/PAF-BDO â€” FACS-AU / BVH formats). Model and animation qualifiers carry
disjoint content-format sets, confirming they are distinct types â€” so **no new "3DA" type was
created**; FDO/BDO *are* the animation. Posing = rendering (real-time engines apply the
animation at render time), so the renderer combines model + animation â†’ frames.

---

## 4. Delivery family (the User Agent's real-world side)

| AIM | Delivers | Device |
|---|---|---|
| AOD | Audio Object | loudspeaker |
| SOD | Speech Object (speech-typed, own device) | loudspeaker |
| 3OD | 3D Model Object + Face/Body animation | 3D renderer (screen) |

Portable core vs. isolated device driver throughout (e.g. `Mpai.Mmc.Sod` portable +
`Mpai.Mmc.Sod.Windows` device). Per M3152, SOD + 3OD are the UA's untrusted-real-world
delivery; the trusted Modules *produce*, the UA *delivers*.

---

## 5. Rendering (the avatar)

- **FACS â†’ ARKit blendshapes** mapping (renderer-agnostic AU descriptor â†’ morph targets).
- **Realistic 3D avatar** â€” a ReadyPlayerMe head (ARKit blendshapes + Oculus visemes) rendered
  in **three.js** with ACES tone mapping + a studio HDR environment. (The sanctioned
  "headless glTF/WebGL driven from C#" 3OD path per M3154.)
- **Text-driven lip-sync, rendered** â€” the renderer applies the FDO timeline
  (`{SimpleTime, Data}` frames â†’ AU â†’ blendshapes) synced to the speech audio, so the mouth
  moves through the words.
- `WebView3DModelDelivery` â€” 3OD's device wrapping a WebView (poster-delegate, so the portable
  TOD project carries no WebView2 dependency).

---

## 6. Applications â€” `MPAIApps/HCIApp` (the collection)

| App | What | Status |
|---|---|---|
| CavApp (In-Cabin conversational CAV) | A standalone WPF app with an embedded WebView2 avatar. A **thin client of the HCI API**: on Say â†’ `SubmitDialogueIntent` (EDP) â†’ `ReceiveSpeakingAvatar` (RSR) â†’ present the Speaking Avatar (speech + lip-sync) in one window. | Built, proven |
| Access Control (CAV-MAC) | Multimodal access control; a sibling HCI application. | Prior (candidate to move onto the API) |

`MPAIApps/HCIApp` is the **collection** of HCI applications, sharing the HCI API rather than
each re-wiring the AIF.

---

## 7. The HCI API â€” `Mpai.Hci.Api` (implements M3152 Â§5)

A thin faÃ§ade over the HCI Modules (holds the AIF UserAgent/Controller + a combined provider;
hides the AIF wiring). Two operations built (the dialogue slice):

| Operation | Runs | Returns |
|---|---|---|
| `SubmitDialogueIntent(humanText)` | UAG-EDP (local LLM) | Machine Text + Machine Personal Status |
| `ReceiveSpeakingAvatar(text, PS)` | UAG-RSR | Machine Speech + Machine Face Descriptors (the Speaking Avatar) |

*(M3152 also specifies further consume-product operations â€” ReadAVSceneDescriptors,
ReadReconciledIdentity, ReadPersonalStatus, ObserveWorldModel â€” and the IDR/SAR seams; the
dialogue slice is the first cut.)*

---

## 8. Principles held throughout

- **Represent, don't compute, at the type level** â€” the FDO represents movement (expression +
  lips) regardless of how each channel is generated.
- **Interoperability over convenience** â€” the FDO timing is a structured, format-independent
  field, not a private encoding.
- **No silent invention** â€” every new AIM / data type name was surfaced and confirmed
  (e.g. PAF-GFD "Generative Face Description"; the "3DA" idea *considered and rejected*).
- **Objects and scenes live in OSD** â€” new object types placed accordingly.
- **Trust boundaries (PTF as guiding notion)** â€” the UA is trusted with the Controller and
  untrusted with the real world; SOD/3OD are its real-world deliveries.
- **Reference software validates the standard** â€” building surfaced and corrected real
  discrepancies (OSD-AUO vs speech, the empty 3D-model stub, the SOD/AOD coupling, misplaced
  types), feeding fixes back into the schemas.
- **Terminology** â€” "AIW" is now "Module".

---

## 9. Deferred / future

- **PAF-GBD** (Generative Body Description) + the renderer's skeletal (BVH) animation + a
  body-showing view â€” the body/gesture channel. Scaffolding partly ready (PSD emits Gesture PS;
  3OD has the BodyAnimation port; PAF-EBD is the analysis sibling; the RPM avatar is full-body).
- **Capture front-end** wired into the app (camera/mic â†’ real Personal Status into the conversation).
- **Affective TTS** (use the Speech Personal Status to inflect prosody).
- **Further HCI API operations + a second collection app** (Access Control on the API).
- **Full PTF implementation** (credentials, evidence, Trust Anchor, Verification Pipeline) â€”
  currently a guiding notion, not implemented.
