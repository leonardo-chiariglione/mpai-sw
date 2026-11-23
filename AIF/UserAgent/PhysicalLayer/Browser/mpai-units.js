// THE PHYSICAL LAYER UNITS IN A BROWSER (MPAI-AIF V3.0, User Agent 4).
//
// A page captures media from the real world and delivers media to it ONLY through these
// Units, never by calling the browser's own device functions: the microphone is opened
// here, the sound is played here. The Units are the same in what they give and take as the
// C# Units of AIF\UserAgent\PhysicalLayer (MicrophoneUnit, LoudspeakerUnit): what they
// acquire is audio of a stated rate, 16-bit PCM at 16 kHz mono when speech is asked for;
// where what is asked cannot be had, they say so and substitute nothing silently.
//
// This file is the only place of a page where getUserMedia, AudioContext or an audio element
// may appear (test: BrowserUnitsOnlyTests). Pages that do not yet go through it are named in
// that test and are to be converted.
window.MpaiUnits = (() => {
  // ---------------------------------------------------------------- the loudspeaker's sound context
  let ctx = null;
  function context() {
    if (!ctx) ctx = new (window.AudioContext || window.webkitAudioContext)();
    if (ctx.state === 'suspended') ctx.resume().catch(() => {});
    return ctx;
  }
  // THE MASTER OUTPUT: everything the Loudspeaker Unit plays goes through one gain, so that a page can lower or silence the room's sound (a spoken summary takes its place).
  let masterNode = null;
  function master() { const c = context(); if (!masterNode) { masterNode = c.createGain(); masterNode.connect(c.destination); } return masterNode; }
  // A browser keeps a page silent until the person has done something on it: the first touch turns the sound on.
  for (const ev of ['pointerdown', 'keydown', 'touchstart'])
    addEventListener(ev, () => { if (ctx && ctx.state === 'suspended') ctx.resume().catch(() => {}); }, { passive: true });

  // ---------------------------------------------------------------- MICROPHONE
  // Blocks of 100 ms (1600 samples), mono, 16 kHz, as Float32 in -1..1, resampled by linear interpolation from the rate of the device.
  const WORKLET = `class P extends AudioWorkletProcessor{constructor(){super();this.b=new Float32Array(1600);this.n=0;this.pos=0;this.last=0;}
process(inp){const c=inp[0]&&inp[0][0];if(!c)return true;const r=sampleRate/16000;
for(;;){const i=Math.floor(this.pos);if(i+1>=c.length)break;const f=this.pos-i;const a=i<0?this.last:c[i];this.b[this.n++]=a*(1-f)+c[i+1]*f;this.pos+=r;
if(this.n===1600){this.port.postMessage(this.b.slice());this.n=0;}}
this.pos-=c.length;this.last=c[c.length-1];return true;}}
registerProcessor('mpai-p16',P);`;
  let workletLoaded = false;

  // Opens the microphone. Returns { label, onBlock(callback), close() }. Throws what the browser throws if the person refuses or there is none.
  async function openMicrophone(o = {}) {
    const stream = await navigator.mediaDevices.getUserMedia({ audio: { channelCount: 1, echoCancellation: o.echoCancellation ?? true, noiseSuppression: o.noiseSuppression ?? true } });
    const c = context();
    const src = c.createMediaStreamSource(stream);
    const listeners = [];
    let cleanup;
    if (c.audioWorklet) {
      if (!workletLoaded) { await c.audioWorklet.addModule(URL.createObjectURL(new Blob([WORKLET], { type: 'text/javascript' }))); workletLoaded = true; }
      const node = new AudioWorkletNode(c, 'mpai-p16', { numberOfOutputs: 0 });
      node.port.onmessage = e => { for (const f of listeners) f(e.data); };
      src.connect(node);
      cleanup = () => { try { src.disconnect(); node.disconnect(); } catch { } };
    } else {                                                  // a browser without worklets
      const sp = c.createScriptProcessor(4096, 1, 1), ratio = c.sampleRate / 16000; let pos = 0, buf = [];
      sp.onaudioprocess = e => {
        const d = e.inputBuffer.getChannelData(0);
        for (; pos + 1 < d.length; pos += ratio) { const i = Math.floor(pos), f = pos - i; buf.push(d[i] * (1 - f) + d[i + 1] * f); }
        pos -= d.length;
        while (buf.length >= 1600) { const blk = Float32Array.from(buf.splice(0, 1600)); for (const f of listeners) f(blk); }
      };
      src.connect(sp); sp.connect(c.destination);
      cleanup = () => { try { src.disconnect(); sp.disconnect(); } catch { } };
    }
    return {
      label: stream.getAudioTracks()[0]?.label || 'device',
      sampleRate: c.sampleRate,
      onBlock: f => { listeners.push(f); },
      close: () => { cleanup(); stream.getTracks().forEach(t => t.stop()); listeners.length = 0; }
    };
  }

  function toInt16(chunks) {
    const n = chunks.reduce((a, b) => a + b.length, 0), pcm = new Int16Array(n); let o = 0;
    for (const ch of chunks) for (const v of ch) pcm[o++] = Math.max(-1, Math.min(1, v)) * 32767;
    return pcm;
  }

  // AUDIO until stop() is called (a button held or pressed twice). Returns { stop() } and stop() gives Int16Array PCM, 16 kHz mono.
  async function record() {
    const mic = await openMicrophone(), chunks = [];
    mic.onBlock(b => chunks.push(b));
    return { stop: () => { mic.close(); return toInt16(chunks); } };
  }

  // SPEECH, ending when the speaker stops: waits for speech to start (giving up after noSpeechMs), ends after pauseMs of quiet, or after maxMs.
  // Returns Int16Array PCM 16 kHz mono, or null if nobody spoke. The level is the RMS of a block.
  async function acquireSpeech(o = {}) {
    const startLevel = o.startLevel ?? 0.02, pauseMs = o.pauseMs ?? 1000, maxMs = o.maxMs ?? 10000, noSpeechMs = o.noSpeechMs ?? 7000;
    const mic = await openMicrophone(), chunks = [], t0 = Date.now();
    let spoke = false, quietMs = 0;
    await new Promise(done => {
      mic.onBlock(b => {
        chunks.push(b);
        let s = 0; for (let i = 0; i < b.length; i++) s += b[i] * b[i];
        const rms = Math.sqrt(s / b.length);
        if (rms > startLevel) { spoke = true; quietMs = 0; } else if (spoke) quietMs += b.length / 16;
        if ((spoke && quietMs >= pauseMs) || Date.now() - t0 > maxMs || (!spoke && Date.now() - t0 > noSpeechMs)) done();
      });
    });
    mic.close();
    return spoke ? toInt16(chunks) : null;
  }

  // ---------------------------------------------------------------- LOUDSPEAKER
  // Decoded audio from the bytes of a sound file.
  const decode = async bytes => await context().decodeAudioData(bytes);

  // A sound played, panned left (-1) to right (1); returns { src, t0 } - src.onended tells when it has ended, t0 the time it began on the clock of the context.
  function playBuffer(buf, o = {}) {
    const c = context(), src = c.createBufferSource(); src.buffer = buf;
    const pan = c.createStereoPanner(); pan.pan.value = Math.max(-1, Math.min(1, o.pan ?? 0));
    src.connect(pan); pan.connect(master());
    const t0 = c.currentTime; src.start(t0);
    return { src, t0, now: () => c.currentTime - t0 };
  }

  // VOICES in a room: each has its position in space (HRTF) and a level meter; speech arrives in chunks of 16-bit PCM at 16 kHz, played one after another with a small jitter buffer.
  const voices = new Map();
  function voice(id, o = {}) {
    let v = voices.get(id); if (v) return v;
    const c = context();
    const panner = c.createPanner(); panner.panningModel = 'HRTF'; panner.distanceModel = 'inverse'; panner.refDistance = 1; panner.rolloffFactor = o.rolloff ?? 0.6;
    const analyser = c.createAnalyser(); analyser.fftSize = 512;
    const gain = c.createGain(); gain.gain.value = 1;
    gain.connect(analyser); gain.connect(panner); panner.connect(master());
    v = { panner, analyser, gain, next: 0, td: new Float32Array(analyser.fftSize), lastData: 0 };
    voices.set(id, v);
    return v;
  }
  function placeVoice(id, x, y, z) {
    const v = voices.get(id); if (!v) return;
    if (v.panner.positionX) { v.panner.positionX.value = x; v.panner.positionY.value = y; v.panner.positionZ.value = z; }
    else v.panner.setPosition(x, y, z);
  }
  function placeListener(p, f) {
    if (!ctx) return;
    const L = ctx.listener;
    if (L.positionX) {
      L.positionX.value = p.x; L.positionY.value = p.y; L.positionZ.value = p.z;
      L.forwardX.value = f.x; L.forwardY.value = 0; L.forwardZ.value = f.z; L.upX.value = 0; L.upY.value = 1; L.upZ.value = 0;
    } else { L.setPosition(p.x, p.y, p.z); L.setOrientation(f.x, 0, f.z, 0, 1, 0); }
  }
  // The speech of a voice: n 16-bit samples in a DataView from the offset. Returns the seconds it adds.
  function playPcm16(id, dv, off, jitter = 0.12) {
    const c = context(), v = voice(id);
    const n = (dv.byteLength - off) >> 1; if (n <= 0) return 0;
    const buf = c.createBuffer(1, n, 16000), ch = buf.getChannelData(0);
    for (let i = 0; i < n; i++) ch[i] = dv.getInt16(off + 2 * i, true) / 32768;
    const src = c.createBufferSource(); src.buffer = buf; src.connect(v.gain);
    const now = c.currentTime;
    if (v.next < now + 0.01) v.next = now + jitter;           // jitter buffer
    src.start(v.next); v.next += buf.duration; v.lastData = performance.now();
    return buf.duration;
  }
  // The level (RMS) of what a voice is now giving, 0 when the sound is not running.
  function level(id) {
    const v = voices.get(id); if (!v || !ctx || ctx.state !== 'running') return 0;
    v.analyser.getFloatTimeDomainData(v.td);
    let s = 0; for (let i = 0; i < v.td.length; i++) s += v.td[i] * v.td[i];
    return Math.sqrt(s / v.td.length);
  }


  // ---------------------------------------------------------------- MICROPHONE, CONTINUOUS
  // A microphone that stays open: blocks of blockSize samples at sampleRate (the context itself runs at that rate), Float32 -1..1. It can be muted (nothing flows from a muted track), it opens
  // again by itself when its track ends (the device was taken by another program or changed), and it can be a test tone in bursts instead of a device (tone: true).
  // Returns { onBlock(cb), onNotice(cb), setMuted(bool), state(), close() }; state() = { audio, track: { label, readyState, enabled, systemMuted } | null }.
  async function streamMicrophone(o = {}) {
    const blockSize = o.blockSize ?? 1024, rate = o.sampleRate ?? 16000, tone = !!o.tone;
    const mctx = new (window.AudioContext || window.webkitAudioContext)({ sampleRate: rate });
    const listeners = [], notices = o.onNotice ? [o.onNotice] : [], timers = [];
    let stream = null, src = null, muted = false, reopening = false;
    const notice = t => { for (const f of notices) f(t); };
    const node = mctx.createScriptProcessor(blockSize, 1, 1);
    node.onaudioprocess = e => { const d = e.inputBuffer.getChannelData(0); for (const f of listeners) f(d); };
    // A page opened or reloaded already seated starts the microphone with no click: the browser keeps its audio stopped ("suspended") until the person touches the page. The first click or key starts it.
    const wake = () => { if (mctx.state !== 'running') mctx.resume().then(() => { if (mctx.state === 'running') notice('Microphone on.'); }); };
    for (const ev of ['pointerdown', 'keydown']) document.addEventListener(ev, wake, true);
    if (mctx.state !== 'running') notice('Click anywhere on the page to start your microphone.');
    if (tone) {
      src = mctx.createGain(); const osc = mctx.createOscillator(); osc.frequency.value = 220; const g = mctx.createGain(); osc.connect(g); g.connect(src); osc.start();
      timers.push(setInterval(() => { g.gain.value = (Date.now() / 1000 | 0) % 6 < 3 ? 0.3 : 0; }, 100));
    } else {
      stream = await navigator.mediaDevices.getUserMedia({ audio: { echoCancellation: true, noiseSuppression: true } });
      src = mctx.createMediaStreamSource(stream);
    }
    src.connect(node); node.connect(mctx.destination);
    const live = () => !!stream && stream.getAudioTracks().some(t => t.readyState === 'live');
    // THE MICROPHONE ENDED: it is opened again, at once and every 2 s for a minute, and the new track takes the place of the old one; the mute is kept.
    const reopen = async () => {
      if (reopening) return; reopening = true;
      for (let k = 0; k < 30; k++) {
        try {
          const s2 = await navigator.mediaDevices.getUserMedia({ audio: { echoCancellation: true, noiseSuppression: true } });
          try { src.disconnect(); } catch { }
          stream = s2; src = mctx.createMediaStreamSource(s2); src.connect(node);
          s2.getAudioTracks().forEach(t => { t.enabled = !muted; t.onended = reopen; });
          notice('Microphone back.'); reopening = false; return;
        } catch { await new Promise(r => setTimeout(r, 2000)); }
      }
      reopening = false; notice('The microphone cannot be opened: check that no other program uses it, then reload the page.');
    };
    if (!tone) {
      stream.getAudioTracks().forEach(t => { t.onended = reopen; });
      if (!live()) reopen();
      navigator.mediaDevices.addEventListener?.('devicechange', () => { if (!live()) reopen(); });
      timers.push(setInterval(() => { if (!live()) reopen(); }, 3000));
    }
    return {
      onBlock: f => { listeners.push(f); },
      onNotice: f => { notices.push(f); },
      setMuted: b => { muted = !!b; stream?.getAudioTracks().forEach(t => { t.enabled = !muted; }); },
      state: () => { const t = stream?.getAudioTracks()[0]; return { audio: mctx.state, track: t ? { label: t.label, readyState: t.readyState, enabled: t.enabled, systemMuted: t.muted } : null }; },
      close: () => { timers.forEach(clearInterval); listeners.length = 0; try { src.disconnect(); node.disconnect(); } catch { } stream?.getTracks().forEach(t => t.stop()); mctx.close().catch(() => {}); }
    };
  }

  // ---------------------------------------------------------------- SCREEN: a shared screen as compressed video and sound
  // A screen is captured by the browser and compressed here: VIDEO (VP9, else VP8, else H.264) at up to 10 frames a second, a key frame at least every 2 s, a frame at least once a second while
  // nothing changes; SOUND (Opus, 24 kHz mono, 32 kbit/s) if the person shares the sound. What leaves the Unit are messages, the first byte of which says what they are:
  //   3 video configuration: JSON { codec, width, height }          4 video chunk: flags (bit 0: key frame), the time in microseconds (float64, little endian), the compressed data
  //   5 sound configuration: JSON { codec, sampleRate, numberOfChannels }   6 sound chunk: the time in microseconds (float64), the compressed data
  //   1 a picture as JPEG (what the Screen Helper, a program outside the browser, sends), 2 raw 16-bit PCM at 24 kHz (older).
  // The player below does the opposite.
  const CODECS = ['vp09.00.10.08', 'vp8', 'avc1.42001f'];
  const screenAvailable = () => !!(window.VideoEncoder && window.AudioEncoder && window.VideoDecoder && window.AudioDecoder && navigator.mediaDevices?.getDisplayMedia);
  const json = (type, obj) => { const t = new TextEncoder().encode(JSON.stringify(obj)), m = new Uint8Array(1 + t.length); m[0] = type; m.set(t, 1); return m; };

  // Returns { surface, hasAudio, onMessage(cb), onEnded(cb), start(), close() }. Throws what the browser throws if the person does not allow it, or says the browser cannot compress video.
  async function shareScreen(o = {}) {
    if (!(window.VideoEncoder && window.AudioEncoder)) throw new Error('This browser cannot compress video: use Chrome or Edge to share a screen.');
    const surface = o.surface ?? 'monitor', maxW = o.maxWidth ?? 1280, fps = o.fps ?? 10;
    const controller = typeof CaptureController !== 'undefined' ? new CaptureController() : null;       // Chrome: lets the page say where the focus goes
    const stream = await navigator.mediaDevices.getDisplayMedia({ video: { frameRate: fps, displaySurface: surface }, audio: o.audio ?? true, ...(controller ? { controller } : {}) });
    try { controller?.setFocusBehavior(surface === 'window' ? 'focus-captured-surface' : 'no-focus-change'); } catch { }
    const vt = stream.getVideoTracks()[0], hasAudio = stream.getAudioTracks().length > 0;
    const video = document.createElement('video'); video.srcObject = stream; video.muted = true; await video.play().catch(() => {});
    const out = [], ended = [];
    const emit = m => { for (const f of out) f(m); };
    vt.onended = () => { for (const f of ended) f(); };
    const canvas = document.createElement('canvas'), g = canvas.getContext('2d'), thumb = document.createElement('canvas'); thumb.width = 48; thumb.height = 27;
    const tg = thumb.getContext('2d', { willReadFrequently: true });
    let enc = null, w = 0, h = 0, timer = null, aenc = null, actx = null, sources = [];
    let previous = null, lastEncoded = 0, lastKey = 0, busy = false;
    const dead = e => { console.error('screen', e); };
    async function configure(nw, nh) {
      w = nw; h = nh; canvas.width = w; canvas.height = h;
      let codec = null;
      for (const c of CODECS) { try { if ((await VideoEncoder.isConfigSupported({ codec: c, width: w, height: h, bitrate: 1e6, framerate: fps, latencyMode: 'realtime' })).supported) { codec = c; break; } } catch { } }
      if (!codec) throw new Error('No video compression is available in this browser.');
      try { enc?.close(); } catch { }
      enc = new VideoEncoder({
        output: chunk => {
          const data = new Uint8Array(chunk.byteLength); chunk.copyTo(data);
          const m = new Uint8Array(10 + data.length); m[0] = 4; m[1] = chunk.type === 'key' ? 1 : 0; new DataView(m.buffer).setFloat64(2, chunk.timestamp, true); m.set(data, 10); emit(m);
        }, error: dead });
      enc.configure({ codec, width: w, height: h, bitrate: Math.round(w * h * 1.1), framerate: fps, latencyMode: 'realtime' });
      emit(json(3, { codec, width: w, height: h }));
      lastKey = 0;
    }
    async function tick() {
      if (busy || !video.videoWidth || (enc && enc.encodeQueueSize > 6)) return;
      busy = true;
      try {
        const nw = Math.min(maxW, video.videoWidth) & ~1, nh = Math.round(video.videoHeight * nw / video.videoWidth) & ~1;
        if (!enc || nw !== w || nh !== h) await configure(nw, nh);
        g.drawImage(video, 0, 0, w, h); tg.drawImage(canvas, 0, 0, 48, 27);
        const now = tg.getImageData(0, 0, 48, 27).data; let diff = 0;
        if (previous) for (let i = 0; i < now.length; i += 4) diff += Math.abs(now[i] - previous[i]) + Math.abs(now[i + 1] - previous[i + 1]) + Math.abs(now[i + 2] - previous[i + 2]);
        previous = now;
        const t = Date.now();
        if (diff / (48 * 27) < 1.5 && t - lastEncoded < 1000) return;      // nothing changed: a frame now and then
        const key = t - lastKey >= 2000;
        const f = new VideoFrame(canvas, { timestamp: Math.round(performance.now() * 1000) });
        enc.encode(f, { keyFrame: key }); f.close(); lastEncoded = t; if (key) lastKey = t;
      } catch (e) { dead(e); } finally { busy = false; }
    }
    return {
      surface: vt.getSettings?.().displaySurface, hasAudio,
      onMessage: f => { out.push(f); }, onEnded: f => { ended.push(f); },
      start: () => {
        timer = setInterval(tick, 1000 / fps);
        if (hasAudio) {
          actx = new (window.AudioContext || window.webkitAudioContext)({ sampleRate: 24000 });
          const src = actx.createMediaStreamSource(new MediaStream(stream.getAudioTracks())), proc = actx.createScriptProcessor(2048, 1, 1);
          aenc = new AudioEncoder({ output: chunk => { const d = new Uint8Array(chunk.byteLength); chunk.copyTo(d); const m = new Uint8Array(9 + d.length); m[0] = 6; new DataView(m.buffer).setFloat64(1, chunk.timestamp, true); m.set(d, 9); emit(m); }, error: dead });
          aenc.configure({ codec: 'opus', sampleRate: 24000, numberOfChannels: 1, bitrate: 32000 });
          emit(json(5, { codec: 'opus', sampleRate: 24000, numberOfChannels: 1 }));
          proc.onaudioprocess = e => {
            if (!aenc || aenc.state !== 'configured') return;
            const f = Float32Array.from(e.inputBuffer.getChannelData(0));
            const ad = new AudioData({ format: 'f32-planar', sampleRate: 24000, numberOfFrames: f.length, numberOfChannels: 1, timestamp: Math.round(actx.currentTime * 1e6), data: f });
            aenc.encode(ad); ad.close();
          };
          src.connect(proc); proc.connect(actx.destination); sources = [src, proc];
        }
      },
      close: () => {
        clearInterval(timer);
        try { enc?.close(); } catch { } try { aenc?.close(); } catch { }
        sources.forEach(n => { try { n.disconnect(); } catch { } }); actx?.close().catch(() => {});
        stream.getTracks().forEach(t => t.stop());
      }
    };
  }

  // The other side. screenPlayer({ onFrame(VideoFrame), onJpeg(Blob), skipSound }) returns { message(Uint8Array), close() }. onFrame gets each decoded picture, which is closed when it returns:
  // draw it at once. The sound is played through the Loudspeaker Unit (the master output), a little behind. The first picture shown is a key frame: pictures before one are dropped.
  function screenPlayer(o = {}) {
    let vdec = null, adec = null, needKey = true, gain = null, next = 0;
    const play = (ch, n, rate) => {
      const c = context(), buf = c.createBuffer(1, n, rate); buf.copyToChannel(ch, 0);
      if (!gain) { gain = c.createGain(); gain.gain.value = 0.9; gain.connect(master()); }
      const src = c.createBufferSource(); src.buffer = buf; src.connect(gain);
      const at = Math.max(next, c.currentTime + 0.2); src.start(at); next = at + n / rate;
    };
    return {
      message(b) {
        switch (b[0]) {
          case 1: o.onJpeg?.(new Blob([b.subarray(1)], { type: 'image/jpeg' })); break;
          case 2: {
            if (o.skipSound || b.length <= 2) break;
            const n = (b.length - 1) >> 1, ch = new Float32Array(n), dv = new DataView(b.buffer, b.byteOffset + 1);
            for (let i = 0; i < n; i++) ch[i] = dv.getInt16(2 * i, true) / 32768;
            play(ch, n, 24000); break;
          }
          case 3: {
            if (!window.VideoDecoder) break;
            const cfg = JSON.parse(new TextDecoder().decode(b.subarray(1)));
            try { vdec?.close(); } catch { }
            vdec = new VideoDecoder({ output: f => { try { o.onFrame?.(f); } finally { f.close(); } }, error: e => console.error('screen decode', e) });
            vdec.configure({ codec: cfg.codec, codedWidth: cfg.width, codedHeight: cfg.height }); needKey = true; break;
          }
          case 4: {
            if (!vdec || vdec.state !== 'configured') break;
            const key = (b[1] & 1) === 1; if (needKey && !key) break; needKey = false;
            vdec.decode(new EncodedVideoChunk({ type: key ? 'key' : 'delta', timestamp: new DataView(b.buffer, b.byteOffset + 2, 8).getFloat64(0, true), data: b.subarray(10) })); break;
          }
          case 5: {
            if (o.skipSound || !window.AudioDecoder) break;
            const cfg = JSON.parse(new TextDecoder().decode(b.subarray(1)));
            try { adec?.close(); } catch { }
            adec = new AudioDecoder({ output: a => { try { const n = a.numberOfFrames, ch = new Float32Array(n); a.copyTo(ch, { planeIndex: 0, format: 'f32-planar' }); play(ch, n, a.sampleRate); } finally { a.close(); } }, error: e => console.error('sound decode', e) });
            adec.configure({ codec: cfg.codec, sampleRate: cfg.sampleRate, numberOfChannels: cfg.numberOfChannels }); break;
          }
          case 6: {
            if (o.skipSound || !adec || adec.state !== 'configured') break;
            adec.decode(new EncodedAudioChunk({ type: 'key', timestamp: new DataView(b.buffer, b.byteOffset + 1, 8).getFloat64(0, true), data: b.subarray(9) })); break;
          }
        }
      },
      close() { try { vdec?.close(); } catch { } try { adec?.close(); } catch { } }
    };
  }


  // ---------------------------------------------------------------- DEVICE BY LABEL, SPEECH LISTENER, CAMERA, WAV
  // THE DEVICE THE USER CHOSE (in Thalia, APP-7): named by its label, because a device identifier belongs to one origin and a label does not; labels are shown once the browser has let the page
  // use a device. The default device when none is named or the one named is not there now.
  async function deviceByLabel(kind, label) {
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
  function base64Of(bytes) {
    let bin = '';
    for (let i = 0; i < bytes.length; i += 0x8000) bin += String.fromCharCode.apply(null, bytes.subarray(i, i + 0x8000));
    return btoa(bin);
  }

  // A SPEECH LISTENER: the microphone runs from the first unlock() on, keeping the last second heard, so that a person who answers the moment the avatar stops speaking - before listening has begun -
  // is not lost. captureSpeech() is one spoken turn: it begins with the second just heard, waits for speech if none has started, keeps a moment from before it so the first syllable is not lost, and
  // ends after a pause. It returns 16 kHz, 16-bit mono PCM as base64 (what Speech Object Acquisition produces), null when nobody spoke, '!blocked' when the browser refuses the microphone.
  // options: deviceLabel, startLevel, quietLevel, pauseMs, maxMs, prerollMs, log(text).
  function speechListener(o = {}) {
    const RING_MS = 1000, START = o.startLevel ?? 0.015, QUIET = o.quietLevel ?? 0.012, PAUSE_MS = o.pauseMs ?? 900, MAX_MS = o.maxMs ?? 20000, PREROLL_MS = o.prerollMs ?? 1000;
    const log = o.log || (() => {});
    let lctx = null, mic = null, capture = null, hear = null;
    const ring = [];
    const level = x => { let sum = 0; for (let i = 0; i < x.length; i++) sum += x[i] * x[i]; return Math.sqrt(sum / x.length); };
    const trim = (list, ms) => {
      let total = list.reduce((a, c) => a + c.length, 0);
      while (list.length > 1 && total - list[0].length >= lctx.sampleRate * ms / 1000) { total -= list[0].length; list.shift(); }
    };
    function toPcm16k(chunks, rate) {
      const n = chunks.reduce((a, c) => a + c.length, 0), all = new Float32Array(n);
      let p = 0; for (const c of chunks) { all.set(c, p); p += c.length; }
      const ratio = rate / 16000, m = Math.floor(n / ratio), out = new Int16Array(m);
      for (let i = 0; i < m; i++) {                                           // average over each step: a plain low-pass
        const a = Math.floor(i * ratio), b = Math.min(n, Math.floor((i + 1) * ratio));
        let s = 0; for (let j = a; j < b; j++) s += all[j];
        const v = b > a ? s / (b - a) : all[a];
        out[i] = Math.max(-1, Math.min(1, v)) * 32767;
      }
      return base64Of(new Uint8Array(out.buffer));
    }
    // THE CLICK THAT OPENS THE WAY: the microphone may open only after the person has done something.
    async function unlock() {
      lctx = lctx || new (window.AudioContext || window.webkitAudioContext)();
      if (lctx.state === 'suspended') await lctx.resume();
      if (!mic) {
        const id = await deviceByLabel('audioinput', o.deviceLabel);
        mic = await navigator.mediaDevices.getUserMedia({ audio: { channelCount: 1, echoCancellation: true, noiseSuppression: true, autoGainControl: true, ...(id ? { deviceId: { exact: id } } : {}) } });
        const source = lctx.createMediaStreamSource(mic), node = lctx.createScriptProcessor(4096, 1, 1);
        node.onaudioprocess = e => {
          const block = new Float32Array(e.inputBuffer.getChannelData(0));
          ring.push(block); trim(ring, RING_MS);
          if (hear) hear(block);
        };
        source.connect(node); node.connect(lctx.destination);                // a processor runs only when connected; its output is silence
      }
    }
    function captureSpeech() {
      return new Promise(async resolve => {
        try { await unlock(); } catch (e) { resolve(e && e.name === 'NotAllowedError' ? '!blocked' : null); return; }    // a microphone the browser refuses: said so, so that the person is told what to do
        const rate = lctx.sampleRate, before = ring.slice(), kept = [];
        let speaking = before.some(b => level(b) > START), quietMs = 0, spokenMs = 0, done = false;
        if (speaking) { kept.push(...before); log('speech: already started when listening began'); }
        function finish(keep) {
          if (done) return; done = true; hear = null; capture = null;
          if (keep && speaking) log(`speech: ended, ${Math.round(spokenMs)} ms heard, the pause of ${PAUSE_MS} ms confirmed`);
          resolve(keep && speaking ? toPcm16k(kept, rate) : null);
        }
        capture = { abandon: () => finish(false) };
        hear = block => {
          const rms = level(block), ms = block.length * 1000 / rate;
          if (!speaking) {
            before.push(block); trim(before, PREROLL_MS);
            if (rms > START) { speaking = true; kept.push(...before); log('speech: started'); }
            return;
          }
          kept.push(block); spokenMs += ms;
          quietMs = rms < QUIET ? quietMs + ms : 0;
          if (quietMs >= PAUSE_MS || spokenMs >= MAX_MS) finish(true);
        };
      });
    }
    return { unlock, captureSpeech, abandon: () => { if (capture) capture.abandon(); } };
  }

  // THE CAMERA: a face, from one frame, as JPEG, base64; null when there is none. The browser asks the person for the camera the first time; a request nobody answers must not hold the
  // conversation: after waitMs, or when abandon() is called, the turn goes on without the face, and a camera granted afterwards is closed again at once. '!blocked' when it is refused.
  let frameWait = null;
  async function captureFrame(o = {}) {
    const log = o.log || (() => {});
    let timer = null;
    const camId = await deviceByLabel('videoinput', o.deviceLabel);
    const ask = navigator.mediaDevices.getUserMedia({ video: camId ? { deviceId: { exact: camId } } : true });
    const gaveUp = new Promise(r => { timer = setTimeout(() => r(null), o.waitMs || 8000); frameWait = () => r(null); });
    try {
      const s = await Promise.race([ask, gaveUp]);
      clearTimeout(timer); frameWait = null;
      if (!s) { log('camera: no answer, the turn goes on without the face'); ask.then(late => late.getTracks().forEach(t => t.stop())).catch(() => {}); return null; }
      const v = document.createElement('video'); v.srcObject = s; v.muted = true; v.playsInline = true; await v.play();
      await new Promise(r => setTimeout(r, 400));                             // let exposure settle
      const c = document.createElement('canvas'); c.width = v.videoWidth; c.height = v.videoHeight; c.getContext('2d').drawImage(v, 0, 0);
      s.getTracks().forEach(t => t.stop());
      const url = c.toDataURL('image/jpeg', 0.9);
      return url.substring(url.indexOf(',') + 1);
    } catch (e) { clearTimeout(timer); frameWait = null; console.error(e); return e && e.name === 'NotAllowedError' ? '!blocked' : null; }
  }

  // SPEECH TO HEAR, by itself: a WAV played to its end; the promise is kept when it has ended. A WAV playing is stopped by the next one.
  let wavNow = null;
  function playWav(wavBase64) {
    return new Promise(resolve => {
      try {
        if (wavNow) { wavNow.pause(); wavNow = null; }
        const audio = new Audio('data:audio/wav;base64,' + wavBase64);
        wavNow = audio; audio.onended = () => resolve(); audio.onerror = () => resolve();
        audio.play().catch(() => resolve());
      } catch (e) { console.error(e); resolve(); }
    });
  }


  // ---------------------------------------------------------------- MICROPHONE, AN ARRAY
  // The channels of a microphone array, one device with many channels, brought to `rate` by averaging, as interleaved 16-bit PCM in chunks of 100 ms (1600 samples of each channel at 16 kHz). No echo
  // cancellation, no noise suppression, no gain control: the room is heard as it is. Returns { channels, label, onChunk(cb(ArrayBuffer)), close() }; channels is what the device gives, which may be less
  // than asked (then nothing flows: the page says so and closes it).
  const arrayModules = new Set();
  async function arrayMicrophone(channels, rate) {
    const stream = await navigator.mediaDevices.getUserMedia({ audio: { channelCount: { ideal: channels }, echoCancellation: false, noiseSuppression: false, autoGainControl: false } });
    const track = stream.getAudioTracks()[0], got = track.getSettings().channelCount || 1;
    const stop = () => stream.getTracks().forEach(t => t.stop());
    if (got !== channels) return { channels: got, label: track.label, onChunk() { }, close: stop };
    const c = context(), name = `mpai-array-${channels}-${rate}`;
    if (!arrayModules.has(name)) {
      const code = `class P extends AudioWorkletProcessor {
        constructor() { super(); this.n = ${channels}; this.ratio = sampleRate / ${rate}; this.pos = 0; this.acc = new Float32Array(${channels}); this.cnt = 0; this.out = []; }
        process(inputs) {
          const inp = inputs[0]; if (!inp || inp.length < this.n) return true;
          for (let i = 0; i < inp[0].length; i++) {
            for (let c = 0; c < this.n; c++) this.acc[c] += inp[c][i];
            this.cnt++; this.pos++;
            if (this.pos >= this.ratio) { this.pos -= this.ratio; for (let c = 0; c < this.n; c++) this.out.push(Math.max(-1, Math.min(1, this.acc[c] / this.cnt)) * 32767); this.acc.fill(0); this.cnt = 0; }
          }
          if (this.out.length >= 1600 * this.n) { const a = Int16Array.from(this.out.splice(0, 1600 * this.n)); this.port.postMessage(a.buffer, [a.buffer]); }
          return true;
        } }
        registerProcessor("${name}", P);`;
      await c.audioWorklet.addModule(URL.createObjectURL(new Blob([code], { type: 'text/javascript' })));
      arrayModules.add(name);
    }
    const node = new AudioWorkletNode(c, name, { channelCount: channels, channelCountMode: 'explicit', numberOfOutputs: 0 }), src = c.createMediaStreamSource(stream), listeners = [];
    node.port.onmessage = e => { for (const f of listeners) f(e.data); };
    src.connect(node);
    return { channels: got, label: track.label, onChunk: f => { listeners.push(f); }, close: () => { listeners.length = 0; try { src.disconnect(); node.disconnect(); } catch { } stop(); } };
  }


  // A SOUND FILE PLAYED BY ITSELF (an effect, a loop, at its own volume): { play() -> Promise (it fails while the browser has not yet allowed sound), pause(), volume (read and write, 0..1) }.
  function sound(url, o = {}) {
    const a = new Audio(url); a.loop = !!o.loop; if (o.volume !== undefined) a.volume = o.volume;
    return { play: () => a.play(), pause: () => a.pause(), get volume() { return a.volume; }, set volume(v) { a.volume = v; } };
  }
  // The browser plays sound only after a click: a silent sound played once, in the click, allows it from then on.
  function unlockSound(url) { const t = new Audio(url); t.volume = 0; return t.play().then(() => t.pause()).catch(() => {}); }


  // ---------------------------------------------------------------- THE DEVICES OF THIS COMPUTER
  // What the browser tells of them: { kind, label, deviceId }. The labels are empty until the page has been let to use a device.
  async function listDevices() {
    try { return (await navigator.mediaDevices.enumerateDevices()).map(d => ({ kind: d.kind, label: d.label, deviceId: d.deviceId })); } catch { return []; }
  }
  // The browser tells the names of the devices only to a page that has been let to use them: the microphone and the camera are each opened for an instant, only to read their names.
  async function revealDevices() {
    for (const c of [{ audio: true }, { video: true }]) { try { (await navigator.mediaDevices.getUserMedia(c)).getTracks().forEach(t => t.stop()); } catch { } }
  }
  // Three seconds (ms) of a microphone (deviceId, or the default one): onLevel(m) is given the loudest sample of each look; the loudest heard is returned.
  async function testMicrophone(id, onLevel, ms = 3000) {
    const stream = await navigator.mediaDevices.getUserMedia({ audio: id ? { deviceId: { exact: id } } : true });
    const c = new (window.AudioContext || window.webkitAudioContext)(), src = c.createMediaStreamSource(stream), an = c.createAnalyser(); an.fftSize = 1024; src.connect(an);
    if (c.state === 'suspended') await c.resume().catch(() => {});
    const buf = new Float32Array(an.fftSize); let peak = 0; const t0 = performance.now();
    await new Promise(res => { const tick = () => { an.getFloatTimeDomainData(buf); let m = 0; for (const x of buf) m = Math.max(m, Math.abs(x)); peak = Math.max(peak, m); if (onLevel) onLevel(m); if (performance.now() - t0 < ms) requestAnimationFrame(tick); else res(); }; tick(); });
    stream.getTracks().forEach(t => t.stop()); c.close();
    return peak;
  }
  // A camera (deviceId, or the default one) as a video element that the page puts where it likes: { video, settings, start() -> Promise (begins to play), close() }.
  async function previewCamera(id) {
    const stream = await navigator.mediaDevices.getUserMedia({ video: id ? { deviceId: { exact: id } } : true });
    const video = document.createElement('video'); video.srcObject = stream; video.muted = true; video.playsInline = true;
    return { video, settings: stream.getVideoTracks()[0].getSettings(), start: () => video.play(), close: () => { stream.getTracks().forEach(t => t.stop()); video.remove(); } };
  }


  // SPEECH WITH A CLOCK (the voice of an avatar): a WAV that plays from its start, with the time it has reached (the mouth and the body follow it) and a way to cut it off at once. Without a WAV it is a
  // silent speech: its time is 0 and it never ends by itself. { currentTime, play() -> Promise, onended = f, stop() }. stop() pauses, goes back to the start and says it has ended.
  function speech(wavBase64) {
    const a = wavBase64 ? new Audio('data:audio/wav;base64,' + wavBase64) : null;
    return {
      get currentTime() { return a ? a.currentTime : 0; },
      play: async () => { if (a) { a.currentTime = 0; await a.play(); } },
      set onended(f) { if (a) a.onended = f; },
      stop: () => { if (a) { a.pause(); a.currentTime = 0; a.dispatchEvent(new Event('ended')); } }
    };
  }


  // A VIDEO PLAYED ON THE SCREEN, with its sound, in a container of the page, with its controls: { video, stop() }. options: controls (true), autoplay (true), onEnded(), onError().
  function playVideo(container, src, o = {}) {
    const v = document.createElement('video'); v.controls = o.controls ?? true; v.autoplay = o.autoplay ?? true; v.src = src;
    v.onended = () => { if (o.onEnded) o.onEnded(); }; v.onerror = () => { if (o.onError) o.onError(); };
    container.appendChild(v);
    return { video: v, stop: () => { v.pause(); v.remove(); } };
  }

  return {
    microphone: { available: () => !!navigator.mediaDevices?.getUserMedia, open: openMicrophone, record, acquireSpeech, stream: streamMicrophone, speechListener, deviceByLabel, array: arrayMicrophone, test: testMicrophone },
    devices: { list: listDevices, reveal: revealDevices },
    video: { play: playVideo },
    camera: { preview: previewCamera, captureFrame, abandon: () => { if (frameWait) frameWait(); } },
    screen: { available: screenAvailable, share: shareScreen, player: screenPlayer },
    loudspeaker: { speech, sound, unlockSound, state: () => ctx ? ctx.state : 'none', peek: () => ctx, master, context, playWav, stopWav: () => { if (wavNow) { wavNow.pause(); wavNow = null; } }, decode, playBuffer, voice, voices, placeVoice, placeListener, playPcm16, level }
  };
})();
