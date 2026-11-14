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

  let ws = null, pc = null, hudTimer = 0;

  const setStatus = (text) => { status.textContent = text; };

  function reset(message) {
    try { pc && pc.close(); } catch {}
    try { ws && ws.close(); } catch {}
    pc = null; ws = null;
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
    pc.ontrack = (e) => {
      video.srcObject = e.streams[0] || new MediaStream([e.track]);
      video.play().catch(() => {});
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
    clearTimeout(hudTimer);
    hudTimer = setTimeout(() => hud.classList.add('hidden'), 2500);
  }

  // Live resolution readout once frames arrive.
  video.addEventListener('resize', () => {
    if (video.videoWidth) hudText.textContent = `En vivo · ${video.videoWidth}×${video.videoHeight}`;
  });

  btn.addEventListener('click', connect);
  nameInput.addEventListener('keydown', (e) => { if (e.key === 'Enter') connect(); });
  viewer.addEventListener('pointerdown', pokeHud);
  $('stop').addEventListener('click', (e) => {
    e.stopPropagation();
    try { ws && ws.send(JSON.stringify({ type: 'bye' })); } catch {}
    reset('Desconectado.');
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

  // Opened from the QR code: connect straight away.
  if (new URLSearchParams(location.search).has('go')) setTimeout(connect, 300);
})();
