(() => {
  'use strict';
  const $ = (id) => document.getElementById(id);
  const lobby = $('lobby'), viewer = $('viewer'), video = $('video');
  const status = $('status'), meta = $('meta'), btn = $('connect'), nameInput = $('name');
  const hud = $('hud'), hudText = $('hudText');

  const kind = (() => {
    const ua = navigator.userAgent;
    if (/iPhone/.test(ua)) return 'iPhone';
    if (/iPad/.test(ua) || (/Macintosh/.test(ua) && navigator.maxTouchPoints > 1)) return 'iPad';
    if (/Android/.test(ua)) return 'Android';
    if (/Macintosh/.test(ua)) return 'Mac';
    return 'Navegador';
  })();

  let saved = '';
  try { saved = localStorage.getItem('duplixon.name') || ''; } catch { /* private mode */ }
  nameInput.value = saved || kind;

  let ws = null, pc = null, hudTimer = 0, hasAudio = false, channel = null;
  const soundBtn = $('sound'), keyboardBtn = $('keyboard'), handle = $('handle');

  // Remote mouse & keyboard over the "input" data channel (when the PC allows control).
  const canControl = () => channel && channel.readyState === 'open';
  const remote = window.DupliXonInput.attach({
    surface: viewer,
    video,
    keyboardInput: $('kbd'),
    send: (msg) => { if (canControl()) channel.send(JSON.stringify(msg)); },
  });

  // Try to play with sound; Safari may require a tap, so fall back to muted + a sound button.
  function startPlayback() {
    video.muted = !hasAudio;
    video.play().then(updateSound).catch(() => {
      video.muted = true;
      video.play().catch(() => {});
      updateSound();
    });
  }

  function updateSound() {
    soundBtn.hidden = !hasAudio;
    soundBtn.textContent = video.muted ? '🔇' : '🔊';
    soundBtn.classList.toggle('attention', hasAudio && video.muted);
  }

  const setStatus = (text) => { status.textContent = text; };

  function reset(message) {
    try { pc && pc.close(); } catch {}
    try { ws && ws.close(); } catch {}
    remote.releaseAll();
    pc = null; ws = null; channel = null;
    keyboardBtn.hidden = true;
    viewer.hidden = true; lobby.hidden = false;
    btn.disabled = false; btn.classList.remove('waiting'); btn.textContent = 'Conectar';
    if (message) setStatus(message);
    if (document.fullscreenElement) document.exitFullscreen().catch(() => {});
  }

  function connect() {
    if (!('RTCPeerConnection' in window)) {
      setStatus('Este navegador no soporta WebRTC. Usa Safari actualizado.');
      return;
    }
    const name = nameInput.value.trim() || kind;
    try { localStorage.setItem('duplixon.name', name); } catch {}

    btn.disabled = true; btn.textContent = 'Conectando…';
    hasAudio = false;
    // Unlock media playback inside this tap so sound can start later without another gesture.
    video.muted = false;
    video.play().catch(() => {});
    const proto = location.protocol === 'https:' ? 'wss' : 'ws';
    ws = new WebSocket(`${proto}://${location.host}/ws`);

    ws.onopen = () => ws.send(JSON.stringify({
      type: 'hello', name, kind,
      width: screen.width, height: screen.height, dpr: window.devicePixelRatio || 1,
    }));
    ws.onmessage = (e) => onSignal(JSON.parse(e.data));
    ws.onclose = () => { if (!pc || pc.connectionState !== 'connected') reset(status.textContent); };
    ws.onerror = () => reset('No se pudo contactar al PC. ¿Está DupliXon abierto y en la misma Wi-Fi?');
  }

  async function onSignal(msg) {
    switch (msg.type) {
      case 'pending':
        btn.textContent = 'Esperando aceptación…'; btn.classList.add('waiting');
        setStatus('Acepta la solicitud en tu PC para comenzar.');
        break;
      case 'rejected':
        reset('La solicitud fue rechazada en el PC.');
        break;
      case 'offer':
        btn.classList.remove('waiting'); btn.textContent = 'Conectando…';
        meta.textContent = msg.info || '';
        await startPeer(msg.sdp);
        break;
      case 'ice':
        if (pc && msg.candidate) {
          try { await pc.addIceCandidate({ candidate: msg.candidate, sdpMid: msg.sdpMid, sdpMLineIndex: msg.sdpMLineIndex }); }
          catch (err) { console.warn('ICE', err); }
        }
        break;
      case 'error':
        reset(msg.message || 'Ocurrió un error en el PC.');
        break;
    }
  }

  async function startPeer(sdp) {
    pc = new RTCPeerConnection({ iceServers: [] });
    const stream = new MediaStream();
    video.srcObject = stream;
    pc.ontrack = (e) => {
      stream.addTrack(e.track);
      if (e.track.kind === 'audio') hasAudio = true;
      startPlayback();
    };
    pc.ondatachannel = (e) => {
      if (e.channel.label !== 'input') return;
      channel = e.channel;
      channel.onopen = () => { keyboardBtn.hidden = false; };
      channel.onclose = () => { channel = null; keyboardBtn.hidden = true; };
    };
    pc.onicecandidate = (e) => {
      if (e.candidate && ws && ws.readyState === 1)
        ws.send(JSON.stringify({ type: 'ice', candidate: e.candidate.candidate, sdpMid: e.candidate.sdpMid, sdpMLineIndex: e.candidate.sdpMLineIndex }));
    };
    pc.onconnectionstatechange = () => {
      const s = pc && pc.connectionState;
      if (s === 'connected') showViewer();
      else if (s === 'failed') reset('Se perdió la conexión. Revisa que el firewall de Windows permita DupliXon.');
      else if (s === 'closed' || s === 'disconnected') reset('El PC dejó de compartir la pantalla.');
    };

    await pc.setRemoteDescription({ type: 'offer', sdp });
    const answer = await pc.createAnswer();
    await pc.setLocalDescription(answer);
    ws.send(JSON.stringify({ type: 'answer', sdp: pc.localDescription.sdp }));
  }

  function showViewer() {
    lobby.hidden = true; viewer.hidden = false;
    hudText.textContent = 'En vivo';
    pokeHud();
  }

  function pokeHud() {
    hud.classList.remove('hidden');
    handle.classList.add('hidden');
    clearTimeout(hudTimer);
    // Keep the HUD visible while the sound still needs a tap.
    hudTimer = setTimeout(() => {
      if (hasAudio && video.muted) return;
      hud.classList.add('hidden');
      handle.classList.remove('hidden');
    }, 3000);
  }

  // Live resolution readout once frames arrive.
  video.addEventListener('resize', () => {
    if (video.videoWidth) hudText.textContent = `En vivo · ${video.videoWidth}×${video.videoHeight}`;
  });

  btn.addEventListener('click', connect);
  nameInput.addEventListener('keydown', (e) => { if (e.key === 'Enter') connect(); });
  // With remote control on, taps belong to the PC; the small handle brings the controls back.
  viewer.addEventListener('pointerdown', (e) => { if (!canControl() && !e.target.closest('.hud')) pokeHud(); });
  handle.addEventListener('pointerdown', (e) => { e.stopPropagation(); pokeHud(); });
  keyboardBtn.addEventListener('click', (e) => {
    e.stopPropagation();
    remote.toggleKeyboard();
    pokeHud();
  });
  $('stop').addEventListener('click', (e) => {
    e.stopPropagation();
    try { ws && ws.send(JSON.stringify({ type: 'bye' })); } catch {}
    reset('Desconectado.');
  });
  soundBtn.addEventListener('click', (e) => {
    e.stopPropagation();
    video.muted = !video.muted;
    video.play().catch(() => {});
    updateSound();
    pokeHud();
  });
  $('fullscreen').addEventListener('click', (e) => {
    e.stopPropagation();
    const el = viewer;
    if (el.requestFullscreen) el.requestFullscreen().catch(() => {});
    else if (video.webkitEnterFullscreen) video.webkitEnterFullscreen();
  });

  // Keep the screen awake while mirroring (supported in Safari 16.4+).
  let lock = null;
  video.addEventListener('playing', async () => {
    try { lock = await navigator.wakeLock?.request('screen'); } catch {}
  });
  document.addEventListener('visibilitychange', () => {
    if (document.visibilityState === 'visible' && !viewer.hidden && !lock) navigator.wakeLock?.request('screen').then((l) => (lock = l)).catch(() => {});
  });

  // Diagnostics hook (connection stats from the console / automated tests).
  window.duplixonStats = async () => {
    if (!pc) return null;
    const out = {};
    (await pc.getStats()).forEach((r) => {
      if (r.type === 'inbound-rtp') out[r.kind] = { packets: r.packetsReceived, lost: r.packetsLost, fps: r.framesPerSecond, jitter: r.jitter };
    });
    return out;
  };

  // Opened from the QR code: connect straight away.
  if (new URLSearchParams(location.search).has('go')) setTimeout(connect, 300);
})();
