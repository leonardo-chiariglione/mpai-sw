// THE BROWSER'S PHYSICAL LAYER. What the desktop RCA does with WASAPI, a webcam
// library and WebView2, the browser does: the microphone, the camera and the sound go through the
// Units of the browser (js/mpai-units.js, one file for every page of an App; never a device
// opened here), and the avatar is shown in its own page.
window.rca = (() => {
  const U = MpaiUnits, params = new URLSearchParams(location.search);

  // THE TIMES OF A TURN, in the console (the author, 2026/09/30: the MAS App answers
  // late - where do the seconds go?): the local clock, as the Service's trace has it.
  function stamp(what) {
    const d = new Date(), p = (n, w) => String(n).padStart(w, '0');
    console.log(`${p(d.getHours(), 2)}:${p(d.getMinutes(), 2)}:${p(d.getSeconds(), 2)}.${p(d.getMilliseconds(), 3)}  ${what}`);
  }

  // THE DEVICES THE USER CHOSE IN THALIA (APP-7): named by their label in the address of the App (?mic=..., ?cam=...); the Units find them. The default device when none is named or the one named is not there now.
  const listener = U.microphone.speechListener({ deviceLabel: params.get('mic'), log: stamp });

  // THE CLICK THAT OPENS THE WAY: the microphone may open only after the person has done something.
  const unlock = () => listener.unlock();

  // ONE SPOKEN TURN (Microphone Unit): begins with the second just heard, waits for speech if none has started, ends after a pause. 16 kHz, 16-bit mono PCM, base64 - what Speech Object
  // Acquisition produces; '!blocked' if the browser refuses the microphone, null if nobody spoke.
  const captureSpeech = () => listener.captureSpeech();
  const abandonCapture = () => listener.abandon();

  // A FACE, FROM THE CAMERA (Camera Unit): one frame, as JPEG, base64; null when there is none; a request nobody answers does not hold the conversation (after waitMs, or on Stop, the turn goes on
  // without the face); '!blocked' if the browser refuses the camera.
  const captureFrame = waitMs => U.camera.captureFrame({ waitMs, deviceLabel: params.get('cam'), log: stamp });
  const abandonFrame = () => U.camera.abandon();

  // THE AVATAR: the same message the desktop sends it through WebView2.
  function present(faceDescriptorsJson, speechWavBase64, bodyDescriptorsJson) {
    const frame = document.getElementById('avatar');
    if (!frame || !frame.contentWindow) return;
    stamp(`present: to the avatar${speechWavBase64 ? ', with speech' : ''}`);
    frame.contentWindow.postMessage(
      { Kind: 'render', FaceDescriptors: faceDescriptorsJson || null, SpeechWavBase64: speechWavBase64 || '', BodyDescriptors: bodyDescriptorsJson || null },
      window.location.origin);
  }

  // STOP MUST STOP HER: the avatar's page plays her voice through the Units; it is told to be silent.
  function silence() {
    const frame = document.getElementById('avatar');
    if (!frame || !frame.contentWindow) return;
    frame.contentWindow.postMessage({ Kind: 'silence' }, window.location.origin);      // the avatar's page cuts her voice and lets her mouth and body return to rest
    stamp('silence: the avatar stopped speaking');
  }

  // SPEECH TO HEAR, by itself (not the avatar's): a WAV played to its end (Loudspeaker Unit); the promise is kept when it has ended.
  function playWav(wavBase64) { stamp('playWav: speech to hear'); return U.loudspeaker.playWav(wavBase64); }

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
