# Track-Change Log â€” Cross-Cutting Code-Base Changes

**Purpose.** A cumulative record of changes that affect the whole code base, decisions
about framework behaviour, deferred refactors, and per-AIM build status. This is the
durable memory of *why* the code is shaped the way it is.

**How to use.** This file is **append-only**. Each entry carries its own `yyyy-mm-dd`
date. Do **not** overwrite earlier entries when circumstances change â€” add a new dated
note (or a dated "RESOLVED"/"SUPERSEDED" line) beneath the original, so the history is
preserved. New sessions append; they do not rewrite.

---

## 1. Cross-cutting code-base changes

Changes that touch the whole repo or the framework, as opposed to one AIM.

### 2026-08-29 â€” Text ports accept BOTH OSD-BTO and OSD-TXO (multi-type ports)
Rather than migrate ~40 files from `OSD-TXO` to `OSD-BTO` (the whole text subsystem
across MMC/HMC/PAF/OSD/CAV2 and the framework use `OSD-TXO` as the text port type), an
AIM's text ports declare `DataType` as the **array** `["OSD-BTO-V1.5","OSD-TXO-V1.5"]`
and thus accept either. This is the framework's existing multi-type-port mechanism
(`AimPortReader.DataTypesOf` indexes a port under each declared type; the audio subsystem
already declares `["OSD-BAO","OSD-AUO"]` the same way). *"AIMs should be able to get
and/or produce both BTO and TXO."* No router change, no migration, existing OSD-TXO AIMs
untouched. Applied to MMC-NLU (InputText, RecognisedText, RefinedText).
Supersedes the earlier idea (same day) of converting single-Basic-Text ports OSD-TXOâ†’OSD-BTO,
and the older observation A10 (BasicTextObject typed OSD-TXO vs canonical OSD-BTO).

### 2026-08-29 â€” Existing AIM Metadata L2 stubs: replace with ours (newer)
The repo already contains L2 AIM Metadata (JSON) for many AIMs, often stub/older versions.
When our freshly-authored L2 is newer/correct, **replace** the existing one â€” a git "M" on
an L2 schema is expected; ours supersedes. Do not second-guess the marker or reconcile.
(Seen with OSD-VII and PAF-EBD's EntityBodyDescription.json.)

### 2026-08-29 â€” Schema Header rule: const + standard code, not regex
Author schema `Header` fields as `{"type":"string","const":"PAF-BDO-V1.6"}` (const + the
standard code), not a regex `pattern`. Matches the TFA-BDQ style. Existing regex-pattern
Headers should migrate to const as they are touched (done for BodyDescriptorsObject.json,
GestureDescriptorsObject.json).

---

## 2. Framework rules & principles

How the AIF framework actually behaves, learned by building against it.

### Routing is by DataType; PortNumber disambiguates same-type ports
`MachineExecutor` routes by DataType, not port-name string. When an AIM declares several
ports of the same Direction and DataType, they are told apart by `PortNumber` (an optional
field on the port; the Topology endpoint may carry `#n`; ordinal 1 default). A port whose
`DataType` is an **array** is indexed under each type (multi-type ports; see Â§1).

### 2026-08-29 â€” Either/or boundary inputs MUST be marked IsOptional
An AIM with alternative (either/or) boundary inputs must mark them `"IsOptional": true`.
Otherwise, supplying only one input causes `MachineExecutor.MissingBoundaryInput` to report
the other as missing â†’ the composite **suspends** â†’ `ExecuteAsync` throws on suspend â†’ the
UA swallows it into `(error=OK, Completed=null)`. The AIM never runs, no `[AIF]` line â€” a
silent failure. (Root-caused on MMC-NLU; IDR worked only because its inputs are optional.)

### 2026-08-29 â€” Descriptors DESCRIBE; they do not INTERPRET
A Description AIM's Descriptors must represent the modality richly enough that a downstream
Interpretation AIM can extract Personal Status from them â€” but the Descriptors themselves do
not interpret. Describing and interpreting are cleanly separated (the MMC-PSE reference model:
Description AIMs feed Interpretation AIMs). For the body: PAF-EBD **describes** (3D pose â†’
BVH); PS-Gesture Interpretation (PAF-PGI) **interprets** (reads emotion/attitude). So a rich
3D pose is the right descriptor content, and the geometry/mocap `BodyDescriptorsContentFormats`
enum (BVH/SMPL/glTF/â€¦) is correct as-is; affect is not in the descriptor format â€” it emerges
in interpretation.

### 2026-08-29 â€” Body vs Gesture: one shared Qualifier and one formats enum
Gesture is a **subset** of Body. Gesture Descriptors share the `PAF-BDO` Header and use the
**same single** Qualifier (`TFA-BDQ`) and the **same single** `BodyDescriptorsContentFormats`
enumeration as Body â€” not a separate gesture qualifier. (GestureDescriptorsObject.json now
`$ref`s the shared BodyDescriptorsQualifier.) The formats enum lists all formats, including
proprietary (Mixamo FBX, Reallusion CC3/CC4, â€¦), so users can declare their technology choice;
an implementation produces one (we emit BVH).

### Media-independent identification is homed in OSD
Description â†’ OSD. Identification by media domain (visual â†’ CVE, audio â†’ CAE, body/face â†’ PAF,
speech â†’ MMC) â€” but **media-independent** identification is homed in OSD (e.g. OSD-VII, the
Visual Instance Identification AIM, produces OSD-IID, the media-independent identity type).

### Host / run mechanics
6-step host: `AmdStore.Scan()` â†’ `AimSettings.Load()` â†’ provider â†’ `UserAgent` â†’
`Controller_Initialize` â†’ `AIW_Start` â†’ `RunAsync`. `AimLog.ToConsole()` enables `[AIF]`
logging; `[AIF] <aim>: Ports=N, Inputs=M` prints when an AIM RUNS â€” its absence means the AIM
never instantiated (graph-build/suspend failure). Bare AIMs can't run as AIWs â€” they must be
wrapped in a single-SubAIM UAG-* composite. Shared UA is not concurrency-safe â€” serialise; and
speech must be sequenced (awaited), not fire-and-forget.

---

## 3. Resolved bugs & decisions

### 2026-08-29 â€” RESOLVED â€” B1: UAG wrapper Topology omitted PortNumber â†’ two same-type inputs collapsed
UAG-IDR has two OSD-IID inputs; without `PortNumber` both defaulted to ordinal 1 and routed to
the first port, so SpeakerID received nothing and reconciliation fell back to a coarse "person"
grant. Fix: `PortNumber:2` on the SpeakerID endpoint (AIM-Input side) in UAG-IDR's Topology.
IDR now receives Ports=2 and reconciles to the real subject.

### 2026-08-29 â€” RESOLVED â€” B2: access-control grant too permissive (fail-open)
`CheckAuthorised` let the coarse "person" fallback through as a grant. Fix: fail **closed** â€”
grant only if the reconciled InstanceLabel is an actually-enrolled subject (present in
`gallery.FaceSubjectIds()/SpeechSubjectIds()`); coarse markers and unknown labels DENY.

### 2026-08-29 â€” RESOLVED â€” B3: console voice path was a file-read tautology
The console access host read a fixed `leonardo.wav` as the "probe" (speakerMatchâ‰ˆ1.000 with the
user silent). Superseded by CAV-MAC V2.0 (UI app with live WASAPI mic capture).

### 2026-08-29 â€” DONE â€” A-YoloxProbe retired
`VisualScene.YoloxProbe` was a throwaway diagnostic that dumped the YOLOX ONNX I/O signature so
we could build `YoloxObjectDetector` against the true shape. Its job done, it was retired.

### 2026-08-29 â€” WITHDRAWN â€” A3: "OSD-BMS â†’ OSD-MBS" was wrong
Canonical MPAI-OSD V1.5 Table 1: Basic Audio-Visual Object = **OSD-BMO**, Basic Audio-Visual
Scene Descriptors = **OSD-BMS**. BMS is correct; the earlier "MBS" note was mistaken. No action
unless MBS is found in code (that would be the bug).

---

## 4. Deferred refactors (open)

Non-blocking; each to be done as its own commit with a green-build check afterwards.

- **A1 â€” Extract shared biometric primitives.** Move ArcFaceRecogniser+FaceCrop (Mpai.Paf.Fir)
  and SpeakerEmbedder+WavReader (Mpai.Mmc.Sir) into a shared project. Correct layering is
  primitives â†’ description â†’ recognition; currently EFD refs FIR and ESD refs SIR (backwards)
  purely to reuse embedders. Touches committed FIR/SIR (namespace renames).
- **A2 â€” Rename text `BoundingBox` â†’ `TextBoundingBox`** (Core/RecognisedText.cs) to stop the
  spirit-collision with the OSD visual BoundingBox. Used only by RapidOcrAim.cs + OCR test.
- **A4 â€” Audit AIM processors' hard-coded DataType strings vs current L2/L3 codes.** e.g.
  SoaAimProcessor uses OSD-SPO-V1.5 but metadata is now OSD-BSO-V1.5 â†’ mis-wire risk; likely
  same for Aoa/Voa + delivery processors. Medium risk (silent mis-routing).
- **A5 â€” Migrate FIR/SIR recognition onto descriptor objects + SubjectRepository.** FIR/SIR
  still embed internally and use the old JSON SubjectGallery; the new path is EFD/ESD (describe)
  + SubjectRepository (descriptor objects). Enrolment already writes the descriptor gallery;
  recognition should read the same source.
- **A6 â€” Gallery-write should be AIM-mediated** (M3124 provenance: the Top AIM does the Put).
  Currently the host constructs FileGlobalStorage directly. Follow-up when storage is
  Controller-plumbed to AIMs.
- **A7 â€” Placement + .bak cleanup review** (basic/shared vs app-specific; *.bak already
  gitignored â€” consider an explicit line).
- **A8 â€” OSD-BBX VisualData fidelity** (BBX schema wants a full VisualObject; FIR used
  BasicVisualObject for the crop). Revisit.
- **A9 â€” Delete dead FaceDatabase.cs / SpeakerDatabase.cs** (unused).
- **A11 â€” Delete orphaned old-name schema files (dead ends).** *(2026-08-29)* When newer
  Data/Qualifier/Formats were authored under a different name than a pre-existing schema
  (e.g. the old "Meaning"/MMC-MEA output superseded by TextDescriptorsObject/MMC-TDO), the
  old-name file was left in the tree. These are **not referenced** by any current code or
  schema â€” dead ends, not live conflicts, so nothing breaks. Cleanup only. Do a proper
  orphan sweep across the whole schema tree (find *.json not $ref'd or named by any AIM
  L2/L3 or other schema) and delete the unreferenced old-name duplicates, so there is one
  canonical Data + Qualifier + Formats per concept. Batch with A7 (placement review).

### Tracked follow-ups (feature, not refactor)
- **PAF-FDO expression descriptor for PS-Face.** PAF-FDO today carries only an ArcFace identity
  embedding; PS-Face interpretation will also need an expression descriptor (facial affective
  configuration), distinct from identity. Same describe-vs-interpret split.
- **NLU tagger depth.** The first-pass POS/NE tagger mistags a sentence-initial capitalised word
  as PROPN/NE; deepen (real POS/NER/dependency/SRL) without changing the interface.
- **EBD 3D fidelity.** BVH currently carries posture in the OFFSETs from one frame of 3D joint
  positions (zero rotations); a rotation-native BVH (IK Euler angles) or an SMPL export is the
  future refinement, without changing the PAF-BDO interface.
- **CAV-MAC deny-test.** Only the grant path is proven; prove DENY of a non-enrolled subject
  (second person / empty gallery / imposter fixture).

---

### 2026-08-30 â€” NLU emits Text Personal Status (PSE text branch live)
- MMC-NLU now emits THREE outputs: TextDescriptors (MMC-TDO, the Basic Text Descriptors),
  RefinedText (OSD-BTO), and TextPersonalStatus (MMC-TPS - the three factors Cognitive State/
  Emotion/Social Attitude, each Value 0..1). The word "Meaning" is purged everywhere (retired
  MMC-MEA); the output is the Text Descriptors Object. PSE consumes only the TPS.
- First-pass affect engine: a small lexicon (valence -> Emotion, certainty -> Cognitive State,
  polite/aggressive -> Social Attitude). Deepen to a contextual model (e.g. GoEmotions) later
  without interface change. PROVEN: "...please" -> Social Attitude 1.00, others 0.50 neutral.
- Core PersonalStatus.cs added: PersonalStatusFactor + TextPersonalStatus/SpeechPersonalStatus/
  FacePersonalStatus/GesturePersonalStatus (MMC-TPS/SPS/FPS/GPS) + EntityPersonalStatus (MMC-EPS).
  These mirror the data schemas and are used by NLU (TPS) and, next, PSM (assemble EPS).
- Data-type note: the gesture/body modality PS is GesturePersonalStatus (MMC-GPS), NOT Body/
  MMC-BPS (an earlier wrong name, now dropped). EPS refs Text/Speech/Face/Gesture PS.

### 2026-08-30 â€” PSE Phase A PROVEN end-to-end (box 9 interpretation + multiplexing)
- The full Personal Status Extraction pipeline runs through the Controller: media -> per-modality
  Personal Status -> assembled Entity Personal Status, with labelled factors + degrees.
- AIMs built + proven: MMC-ESI (Entity Speech Interpretation, OSD-BSO -> MMC-SPS, first-pass
  prosodic RMS via WavReader), MMC-EFI (Entity Face Interpretation, OSD-BVO -> MMC-FPS, Phase A
  neutral placeholder), MMC-PSM (Personal Status Multiplexing, TPS+SPS+FPS+GPS -> MMC-EPS, pure
  assembly). Plus NLU already emits MMC-TPS. Each wrapped in a UAG (UAG-ESI/EFI/PSM) + a Pse.Host
  that runs NLU/ESI/EFI then PSM. PROVEN: PSM Ports=3 -> EntityPersonalStatus with Text (SOCIAL
  RANK/respectful 0.70), Speech (CALMNESS/calm 0.90), Face (CALMNESS/calm 0.50).
- DATA MODEL (corrected + cross-checked vs web specs, in Leonardo's spatial arrangement):
  factor = LABEL (three-level Category/GeneralAdjectival/SpecificAdjectival from the standard set)
  + optional Degree [0,1]. Emotion (MMC-EEM), CognitiveState (MMC-ECS), SocialAttitude (MMC-ESA)
  schemas fixed: SpaceTime (actual) vs SimpleTime (creation), ESA V1.0->V2.5, mojibake risquÃ©,
  commanding/domineering, plain object uniform, jealous own general, Entity-prefixed IDs. SOCIAL
  RANK split polite/courteous/respectful into 3 distinct general adjectivals (synonym clusters
  kept). Modality PS (TPS/SPS/FPS/GPS) = 3 factor $refs, anyOf >=1. EPS = modality container
  (Text/Speech/Face/Gesture PS). C# PersonalStatus.cs mirrors all.
- PHASE B (next): stage HSEmotion (ONNX) for a real Face PS (EFI), then wav2vec2 for Speech PS
  (ESI) - deepen the engines without interface change. Body/Gesture (EGI) still omitted (immature).

### 2026-08-30 â€” PSE Phase B: EFI effective Face PS via HSEmotion (FaceEmotion real)
- MMC-EFI now reads real facial affect: SCRFD detects+crops the face (reusing the proven
  ScrfdFaceDetector + FaceCrop), then HSEmotion (enet_b0_8_va_mtl, EfficientNet-B0 multi-task,
  AffectNet, 16MB ONNX, staged in D:\AI\Models) predicts 8 emotion probabilities + valence +
  arousal. Signature (probe-confirmed): input [1,3,224,224] NCHW ImageNet-normalised, output
  [1,10] = 8 emotion logits + valence + arousal. HSEmotionEstimator.cs wraps it (mirrors
  BlazePose/YOLOX). PROVEN on leonardo.jpg: Happiness 0.838, valence +0.63 (he is smiling).
- Mapping HSEmotion -> MMC factors: emotions -> FaceEmotion (MMC-EEM: Anger/Disgust/Fear/
  Happiness/Sadness -> their categories, Neutral -> CALMNESS/calm, Contempt -> HURT/hurt);
  Surprise -> FaceCognitiveState (MMC-ECS SURPRISE/surprised), since MPAI classes Surprise as
  a Cognitive State not an Emotion. Degree = softmax confidence of the chosen label.
- PROVEN through the Controller in the full PSE: EntityPersonalStatus = Text (SOCIAL RANK/
  respectful 0.70) + Speech (CALMNESS/calm 0.90) + Face (HAPPINESS/happy 0.84) - a genuine
  multi-modal read (respectful words, calm voice, happy face). Two real engines now (ESI
  prosodic, EFI HSEmotion) + NLU labelled text affect.
- REMAINING Phase B: ESI could deepen to wav2vec2 (valence/arousal/dominance) - optional, the
  prosodic first-pass is functional. Body/Gesture (EGI) still omitted (immature). Then toward
  end of HCI MW: EDP (LLM dialogue -> response + machine EPS) and PAF-PDR (avatar synthesis).

### 2026-08-30 â€” PSE Phase B closed: ESI effective Speech PS via wav2vec2 (dimensional)
- MMC-ESI now reads real dimensional speech affect: wav2vec2 (audeering w2v2-L-robust-12,
  wav2vec2-large-robust pruned to 12 layers, MSP-Podcast, 661MB ONNX at D:\AI\Models\
  w2v2-emotion\model.onnx). Signature (probe-confirmed): input 'signal' [1,-1] raw mono 16kHz;
  outputs 'hidden_states' [1,1024] + 'logits' [1,3] = arousal, dominance, valence (~0..1).
  Wav2Vec2EmotionEstimator.cs wraps it; ESI reads mono-16k via WavReader (same path as ESD).
  PROVEN on leonardo.wav (17.6s): arousal 0.43, dominance 0.50, valence 0.59 - distinct values.
- Mapping dimensional -> MMC factors: (valence,arousal) circumplex quadrant -> Emotion
  (high-val/high-aro HAPPINESS, low-val/high-aro ANGER, low-val/low-aro SADNESS, high-val/
  low-aro CALMNESS); Degree = distance off the neutral (0.5,0.5) centre. Dominance -> Social
  Attitude (high SOCIAL DOMINANCE/CONFIDENCE/confident, low AGGRESSION/submissive, mid none).
- PHASE B COMPLETE: three real affect engines now feed the PSE - NLU (labelled text affect),
  ESI (wav2vec2 dimensional speech), EFI (HSEmotion face). PROVEN through the Controller:
  EntityPersonalStatus = Text SOCIAL RANK/respectful 0.70 + Speech CALMNESS/calm 0.23
  (valence 0.59, arousal 0.43) + Face HAPPINESS/happy 0.84 - a fully real multi-modal read.
- Body/Gesture (EGI) omitted (immature). NEXT toward end of HCI MW: EDP (LLM dialogue ->
  CAV response + machine EPS) and PAF-PDR (de-multiplex machine EPS -> speaking avatar).

### 2026-08-30 â€” EDP (Entity Dialogue Processing, box 10) BUILT + PROVEN
- MMC-EDP produces the Machine's response Text + Machine Personal Status + updated Summary from
  the human's Text (+ Text Descriptors), Personal Status, identity, scene object IIDs and BMS,
  using a local LLM (Ollama) as the dialogue engine. PROVEN end-to-end through the Controller:
  User "Hello, can you open the door for me?" -> CAV "Of course, right this way.", machine PS
  CALMNESS/calm + SOCIAL RANK/respectful, Summary updated.
- Engine: Ollama (local, llama3.1, served from D:\AI\Models\ollama). OllamaClient POSTs to
  localhost:11434/api/chat; no external API/key. EdpAimProcessor verbalises the situational
  picture into the canonical prompt ("respond to the Text provided by the user with ID X, who
  is BELIEVED to hold Personal Status ..., located in a scene populated by the following visual
  objects (IID) at their spatial attitudes ... and audio objects ..."), calls the LLM, parses a
  JSON reply into Machine text + Machine EPS (structured, feeds PAF-PDR) + EditedSummary.
- I/O corrected vs the stale committed L2: Meaning/MMC-MEA -> TextDescriptors/MMC-TDO (retired);
  TextObject/MachineTextObject OSD-TXO -> OSD-BTO; AVSceneGeometry/OSD-MSG -> BMS/OSD-BMS (MSG a
  subset of BMS); SpeakerID+FaceID -> one reconciled UserID (from IDR); ObjectIDs split into
  VisualObjectIDs (VII) + AudioObjectIDs (ASI = our AII, YAMNet) - three OSD-IID inputs
  PortNumber 1/2/3. Scene verbaliser hedges the object type when the IID is uncertain (low top
  confidence or close top-2 candidates: "possibly a person, or perhaps a mannequin"), the
  epistemic parallel to "believed to hold". The object scanner that feeds VII/ASI is APP logic
  in the HCI app, not an AIM (would be overkill).
- ARCHITECTURE closed: perceive (NLU+PSE, 3 real affect engines) -> EntityPersonalStatus -> EDP
  verbalise -> LLM -> CAV response + machine EPS. The machine perceives how the human is and
  responds appropriately with its own feeling. NEXT: PAF-PDR (de-multiplex machine EPS ->
  affective TTS + generative face/body -> speaking avatar) completes the MW output side.

## 5. Per-AIM build status (MMC-HCI chain)

Boxes of the MMC-HCI reference model, and adjacent AIMs.

- **CAV-MAC V2.0** â€” multimodal access-control UI (Avalonia). Authenticate â†’ face (EFD+match)
  â†’ live voice (ESD+match) â†’ IDR fuse â†’ fail-closed GRANT/DENY (spoken). PROVEN grant path
  (Leonardo, face 0.63, live voice 0.64, all prompts spoken). *(2026-08-29)*
- **OSD-VII** Visual Instance Identification (box 4) â€” BVO â†’ OSD-IID, YOLOX engine. PROVEN
  (zebra 0.95) through the Controller. Media-independent identification homed in OSD. *(2026-08-29)*
- **HCI-IDR** ID Reconciliation (box 7) â€” two OSD-IID inputs â†’ reconciled OSD-IID. PROVEN. *(prior)*
- **MMC-NLU** Natural Language Understanding (box 8) â€” text â†’ Meaning (MMC-TDO) + Refined Text.
  Meaning is a Text Descriptors Object mirroring the speech descriptor family Sâ†’T (MMC-SDOâ†’MMC-TDO,
  MMC-SPDâ†’MMC-TPD, TFA-SDQâ†’TFA-TDQ, + TextDescriptorsFormats). First-pass tagger (POS+NE). PROVEN
  through the Controller. *(2026-08-29)*
- **Description AIMs (media â†’ descriptors)** â€” the layer PSE (box 9) sits on:
  - **MMC-ETD** (text) â€” text descriptors produced via NLU/MMC-TDO.
  - **MMC-ESD** (speech) â€” MMC-SDO. Built + proven. *(prior)*
  - **PAF-EFD** (face) â€” PAF-FDO (ArcFace). Built + proven. *(prior)*
  - **PAF-EBD** (body) â€” PAF-BDO (BVH). Body Visual Object â†’ YOLOX person box â†’ BlazePose GHUM
    3D pose â†’ BVH skeleton â†’ PAF-BDO. Open engine (BlazePose GHUM, Apache-2.0), 3D world-space,
    describe-only. PROVEN through the Controller. *(2026-08-29)*
- **Personal Status Extraction (box 9)** â€” NEXT. Composite over the four Description AIMs +
  Interpretation AIMs (MMC-PTI/PSI, PAF-PFI/**PGI**) + multiplexer (MMC-PMX) + the Personal
  Status type (Objects.cs has only an empty placeholder). **PS-Gesture Interpretation (PAF-PGI)**
  is the immediate next build â€” it interprets the Body Descriptors EBD now produces. NOTE: the
  PSE spec page's I/O table is stale (names/substance changed); confirm current inputs + the
  Personal Status type before building.
- **Entity Dialogue Processing (box 10)**, **Response & Scene Rendering (box 11, speaking avatar)**
  â€” later.
