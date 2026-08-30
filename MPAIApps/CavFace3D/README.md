# CavFace3D â€” the CAV's 2D/3D visual delivery (FACS-driven)

`cav-face.html` renders the CAV's face and drives it with our FACS pipeline:
the machine's Emotion (from its generated Personal Status) â†’ EM-FACS â†’ Action
Units â†’ ARKit blendshapes on a realistic 3D avatar, plus lip-sync. This is the
visual half of the Response and Scene Rendering composite (PAF-RSR), box 11.

## Running it

The avatar GLB and the studio HDR are large binaries (gitignored, like the ONNX
models). Fetch them once into this folder, then serve it locally:

1. Avatar (realistic ReadyPlayerMe head with ARKit blendshapes + Oculus visemes,
   from the open-source TalkingHead project):
   ```
   Invoke-WebRequest -Uri "https://raw.githubusercontent.com/met4citizen/TalkingHead/main/avatars/brunette.glb" -OutFile "cav-avatar.glb"
   ```
2. Studio HDR (image-based lighting for realistic skin; three.js pinned commit):
   ```
   Invoke-WebRequest -Uri "https://raw.githubusercontent.com/mrdoob/three.js/3a7b71e0f47fb105e1ecd63b152f1c09fac6d015/examples/textures/equirectangular/royal_esplanade_1k.hdr" -OutFile "studio.hdr"
   ```
3. Serve and open:
   ```
   python -m http.server 8000
   ```
   then browse to http://localhost:8000/cav-face.html

## Rendering notes

- three.js (from jsdelivr CDN), ACES Filmic tone mapping, RoomEnvironment
  fallback replaced by the real studio HDR (image-based lighting).
- The FACS AU â†’ ARKit-blendshape mapping is renderer-agnostic: the same AU
  descriptor drives a 2D face, this 3D avatar, or a future MetaHuman unchanged.
