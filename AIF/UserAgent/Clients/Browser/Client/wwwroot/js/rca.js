// THE BROWSER'S PHYSICAL LAYER. What the desktop RCA does with WASAPI, a webcam
// library and WebView2, the browser does itself: the microphone and camera
// through getUserMedia, sound through Web Audio, the avatar in its own page.
window.rca = (() => {
  let ctx = null;        // one AudioContext, opened by the Start click
  let mic = null;        // the microphone stream, asked for once
  let capture = null;    // the capture in progress, so it can be abandoned
  let hear = null;       // where the running microphone sends each block, while listening

  // THE MICROPHONE RUNS FROM START ON, keeping the last second heard. A person
  // who answers the moment the avatar stops speaks before listening has begun;
  // with that second kept, the first word ("yes") is not lost.
  const RING_MS = 1000;
  const ring = [];

  const START = 0.015, QUIET = 0.012, PAUSE_MS = 900, MAX_MS = 20000, PREROLL_MS = 1000;

  // THE TIMES OF A TURN, in the console (the author, 2026/09/30: the MAS App answers
  // late - where do the seconds go?): the local clock, as the Service's trace has it.
  function stamp(what) {
    const d = new Date(), p = (n, w) => String(n).padStart(w, '0');
    console.log(`${p(d.getHours(), 2)}:${p(d.getMinutes(), 2)}:${p(d.getSeconds(), 2)}.${p(d.getMilliseconds(), 3)}  ${what}`);
  }

  function level(x) {
    let sum = 0;
    for (let i = 0; i < x.length; i++) sum += x[i] * x[i];
    return Math.sqrt(sum / x.length);
  }

  function trim(list, ms) {
    let total = list.reduce((a, c) => a + c.length, 0);
    while (list.length > 1 && total - list[0].length >= ctx.sampleRate * ms / 1000) {
      total -= list[0].length;
      list.shift();
    }
  }

  // THE DEVICE THE USER CHOSE IN THALIA (APP-7): named by its label in the address of the App (?mic=..., ?cam=...). A device identifier belongs to one origin, a label does not; labels are
  // shown once the browser has let the page use a device. The default device when none is named or the one named is not there now.
  async function chosenDevice(kind, param) {
    const label = new URLSearchParams(location.search).get(param);
    if (!label || !navigator.mediaDevices?.enumerateDevices) return null;
    try {
      let list = await navigator.mediaDevices.enumerateDevices();
      if (!list.some(d => d.kind === kind && d.label)) {
        const t = await navigator.mediaDevices.getUserMedia(kind === 'audioinput' ? { audio: true } : { video: true });
        t.getTracks().forEach(x => x.stop());
        list = await navigator.mediaDevices.enumerateDevices();
      }
      return list.find(d => d.kind === kind && d.label === label)?.deviceId || null;
    } catch { return null; }
  }

  // THE CLICK THAT OPENS THE WAY: sound may play and the microphone may open
  // only after the person has done something.
  async function unlock() {
    ctx = ctx || new (window.AudioContext || window.webkitAudioContext)();
    if (ctx.state === 'suspended') await ctx.resume();
    if (!mic) {
      const micId = await chosenDevice('audioinput', 'mic');
      mic = await navigator.mediaDevices.getUserMedia(
        { audio: { channelCount: 1, echoCancellation: true, noiseSuppression: true, autoGainControl: true, ...(micId ? { deviceId: { exact: micId } } : {}) } });
      const source = ctx.createMediaStreamSource(mic);
      const node = ctx.createScriptProcessor(4096, 1, 1);
      node.onaudioprocess = e => {
        const block = new Float32Array(e.inputBuffer.getChannelData(0));
        ring.push(block);
        trim(ring, RING_MS);
        if (hear) hear(block);
      };
      source.connect(node);
      node.connect(ctx.destination);   // a processor runs only when connected; its output is silence
    }
  }

  // ONE SPOKEN TURN. Begins with the second just heard, waits for speech if none
  // has started, keeps a moment from before it so the first syllable is not lost,
  // and ends after a pause. Returned as 16 kHz, 16-bit mono PCM, base64 - what
  // Speech Object Acquisition produces.
  function captureSpeech() {
    return new Promise(async resolve => {
      // A microphone the browser refuses: said so, so that the person is told what to do.
      try { await unlock(); } catch (e) { resolve(e && e.name === 'NotAllowedError' ? '!blocked' : null); return; }
      const rate = ctx.sampleRate;
      const before = ring.slice(), kept = [];
      let speaking = before.some(b => level(b) > START), quietMs = 0, spokenMs = 0, done = false;
      if (speaking) { kept.push(...before); stamp('speech: already started when listening began'); }

      function finish(keep) {
        if (done) return;
        done = true;
        hear = null;
        capture = null;
        if (keep && speaking) stamp(`speech: ended, ${Math.round(spokenMs)} ms heard, the pause of ${PAUSE_MS} ms confirmed`);
        resolve(keep && speaking ? toPcm16k(kept, rate) : null);
      }
      capture = { abandon: () => finish(false) };

      hear = block => {
        const rms = level(block), ms = block.length * 1000 / rate;
        if (!speaking) {
          before.push(block);
          trim(before, PREROLL_MS);
          if (rms > START) { speaking = true; kept.push(...before); stamp('speech: started'); }
          return;
        }
        kept.push(block);
        spokenMs += ms;
        quietMs = rms < QUIET ? quietMs + ms : 0;
        if (quietMs >= PAUSE_MS || spokenMs >= MAX_MS) finish(true);
      };
    });
  }

  function abandonCapture() { if (capture) capture.abandon(); }

  function toPcm16k(chunks, rate) {
    const n = chunks.reduce((a, c) => a + c.length, 0);
    const all = new Float32Array(n);
    let o = 0;
    for (const c of chunks) { all.set(c, o); o += c.length; }
    const ratio = rate / 16000, m = Math.floor(n / ratio);
    const out = new Int16Array(m);
    for (let i = 0; i < m; i++) {                 // average over each step: a plain low-pass
      const a = Math.floor(i * ratio), b = Math.min(n, Math.floor((i + 1) * ratio));
      let s = 0;
      for (let j = a; j < b; j++) s += all[j];
      const v = b > a ? s / (b - a) : all[a];
      out[i] = Math.max(-1, Math.min(1, v)) * 32767;
    }
    return base64(new Uint8Array(out.buffer));
  }

  function base64(bytes) {
    let bin = '';
    for (let i = 0; i < bytes.length; i += 0x8000)
      bin += String.fromCharCode.apply(null, bytes.subarray(i, i + 0x8000));
    return btoa(bin);
  }

  // A FACE, FROM THE CAMERA: one frame, as JPEG, base64; null when there is none.
  // The browser asks the person for the camera the first time; a request nobody
  // answers must not hold the conversation: after waitMs, or on Stop
  // (abandonFrame), the turn goes on without the face, and a camera granted
  // afterwards is closed again at once.
  let frameWait = null;
  async function captureFrame(waitMs) {
    let timer = null;
    const camId = await chosenDevice('videoinput', 'cam');
    const ask = navigator.mediaDevices.getUserMedia({ video: camId ? { deviceId: { exact: camId } } : true });
    const gaveUp = new Promise(r => {
      timer = setTimeout(() => r(null), waitMs || 8000);
      frameWait = () => r(null);
    });
    try {
      const s = await Promise.race([ask, gaveUp]);
      clearTimeout(timer); frameWait = null;
      if (!s) {
        stamp('camera: no answer, the turn goes on without the face');
        ask.then(late => late.getTracks().forEach(t => t.stop())).catch(() => {});
        return null;
      }
      const v = document.createElement('video');
      v.srcObject = s; v.muted = true; v.playsInline = true;
      await v.play();
      await new Promise(r => setTimeout(r, 400));   // let exposure settle
      const c = document.createElement('canvas');
      c.width = v.videoWidth; c.height = v.videoHeight;
      c.getContext('2d').drawImage(v, 0, 0);
      s.getTracks().forEach(t => t.stop());
      const url = c.toDataURL('image/jpeg', 0.9);
      return url.substring(url.indexOf(',') + 1);
    } catch (e) {
      clearTimeout(timer); frameWait = null; console.error(e);
      return e && e.name === 'NotAllowedError' ? '!blocked' : null;   // refused: said so
    }
  }

  function abandonFrame() { if (frameWait) frameWait(); }

  // THE AVATAR: the same message the desktop sends it through WebView2.
  function present(faceDescriptorsJson, speechWavBase64, bodyDescriptorsJson) {
    const frame = document.getElementById('avatar');
    if (!frame || !frame.contentWindow) return;
    stamp(`present: to the avatar${speechWavBase64 ? ', with speech' : ''}`);
    frame.contentWindow.postMessage(
      { Kind: 'render', FaceDescriptors: faceDescriptorsJson || null, SpeechWavBase64: speechWavBase64 || '', BodyDescriptors: bodyDescriptorsJson || null },
      window.location.origin);
  }

  // STOP MUST STOP HER: the avatar's page plays the speech in its own audio element (the page is served from this origin, so it can be
  // reached). The sound is paused and the page told it has ended, which lets her mouth and body return to rest.
  function silence() {
    try {
      const frame = document.getElementById('avatar');
      const audio = frame && frame.contentDocument && frame.contentDocument.getElementById('responseAudio');
      if (!audio) return;
      audio.pause();
      audio.currentTime = 0;
      audio.dispatchEvent(new Event('ended'));
      stamp('silence: the avatar stopped speaking');
    } catch (e) { console.error(e); }
  }

  // SPEECH TO HEAR, by itself (not the avatar's): a WAV played to its end; the promise is kept when it has ended.
  let heard = null;
  function playWav(wavBase64) {
    return new Promise(resolve => {
      try {
        if (heard) { heard.pause(); heard = null; }
        const audio = new Audio('data:audio/wav;base64,' + wavBase64);
        heard = audio;
        audio.onended = () => resolve();
        audio.onerror = () => resolve();
        stamp('playWav: speech to hear');
        audio.play().catch(() => resolve());
      } catch (e) { console.error(e); resolve(); }
    });
  }

  function focus(id) { const e = document.getElementById(id); if (e) e.focus(); }

  // THE PIXEL OF A PICTURE UNDER A CLICK: the picture's own pixels (X to the right, Y down), whatever size it is shown at; null outside it.
  function imagePoint(id, clientX, clientY) {
    const img = document.getElementById(id);
    if (!img || !img.naturalWidth) return null;
    const r = img.getBoundingClientRect();
    if (clientX < r.left || clientX > r.right || clientY < r.top || clientY > r.bottom) return null;
    return [(clientX - r.left) * img.naturalWidth / r.width, (clientY - r.top) * img.naturalHeight / r.height];
  }

  // PRESENT WHILE OPEN, GONE WHEN CLOSED. The Service counts this client while it
  // hears from it: a request every 30 seconds keeps it counted, and closing or
  // reloading the page says goodbye - with keepalive, so the request outlives the page.
  let presenceTimer = null;
  function presence(clientId) {
    const headers = { 'MPAI-Client': clientId };
    if (presenceTimer) clearInterval(presenceTimer);
    presenceTimer = setInterval(() => fetch('MPAI/AIFU/Status', { headers }).catch(() => {}), 30000);
    window.addEventListener('pagehide', () => {
      try { fetch('MPAI/AIFU/Leave', { method: 'POST', headers, keepalive: true }); } catch (e) {}
    });
  }

  // THE VIDEO ON THE STAGE tells how it ended through its frame's postMessage (stage-video.html); passed on to the client.
  let videoListener = null;
  function watchVideo(dotnet) {
    if (videoListener) window.removeEventListener('message', videoListener);
    videoListener = e => {
      if (e.origin === window.location.origin && e.data && e.data.Kind === 'stage-video') dotnet.invokeMethodAsync('VideoMessage', String(e.data.State));
    };
    window.addEventListener('message', videoListener);
  }

  // THE ELEMENT FILLS THE SCREEN (Esc leaves it).
  function fullScreen(id) {
    const e = document.getElementById(id);
    if (e && e.requestFullscreen) e.requestFullscreen().catch(err => console.error(err));
  }

  return { unlock, captureSpeech, abandonCapture, captureFrame, abandonFrame, present, playWav, silence, focus, imagePoint, watchVideo, fullScreen, presence, stamp };
})();
