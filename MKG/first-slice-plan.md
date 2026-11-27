# PGM-AUA V1.0 - first slice: plan (night 2026-11-04/05)

Scenario: the responsible human says or types "go and open the doors"; the A-User answers.

## Findings that shape the plan (read from D:\AI before writing code)
- AIMs are IAimProcessor + IAimPlugin (AimName), ports resolved by Data Type and PortNumber through AimPortReader from the AMD (D:\AI\AIMs\AMDs). Pattern: AIMs\PAF\V1.6\PDX.
- The A-User Module is 1PGM-ATU-V1.0-I01 (schemas PGM1 AutonomousUser): Sub-AIMs AUC, CXT, PRC, BKN, DAC, USR, PAL, AUF, PAF-PDX; 47 connections by Data Type. CXT, PRC and AUF are themselves composites (their own Sub-AIMs). Each is implemented as ONE AIM against its own ports (an AIM is an AIM); what is inside is the implementer's.
- The Module has request/response loops (AUC <-> CXT, PRC, BKN ...). The Continuous executor fires an AIM when every REQUIRED connected input has a pending message. The PGM AMDs declare no IsOptional, so AUC (12 inputs) could never fire on the Controller. To be checked on a copy of the AMDs and reported (standard decision).

## Design
- One assembly, AIMs\PGM\V1.0 (Mpai.Pgm), one class per AIM plus a plug-in per AIM (AimName = PGM-xxx-V1.0). Base class PgmAim records the ports it reads/writes.
- Real: AUC (state machine of the slice), CXT (text -> context; speech through an injected recogniser), PRC, BKN (consults PRC, DAC, USR, PAL, then asks the LLM; Ollama model from settings, never in the AIM), AUF (Personal Status handling, Motion Object Generation, speech synthesis, Face/Body descriptors, Portable Avatar multiplexing as internal steps of the one AIM), DAC/USR/PAL (simple).
- Stubs (valid minimal Data): visual side, Enhanced Audio/Visual Scene Descriptors content, salience, emotion, Domain knowledge, history.
- Portable Avatar Demultiplexing: existing PAF-PDX (checked against its AMD).
- Tests (xunit in Test\Mpai.Aif.Tests, PgmSliceTests*): per AIM (a) ports of the implementation = ports of the AMD (set equality), (b) every Data received and produced validates against its JSON Schema (resolved from the Header); end to end through the processors with a router that applies the topology of the ATU AMD; then Controller attempt.
- Results to D:\AI\Test\Results\2026-11-05; report to Downloads.
