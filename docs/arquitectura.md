# Arquitectura de DupliXon

```
 Windows PC                                                         iPad / iPhone (Safari)
┌───────────────────────────────────────────────┐                ┌──────────────────────────┐
│ DupliXon.App (WPF, glass/acrylic, MVVM)       │                │ wwwroot (HTML/CSS/JS)    │
│   └─ aprueba solicitudes, QR, ajustes         │                │  1. QR → abre la página  │
│ DupliXon.Server (Kestrel :8787)               │◄── WebSocket ──│  2. "hello" (pantalla)   │
│   ├─ sirve wwwroot (receptor + PWA)           │─── offer/ICE ─►│  3. answer/ICE           │
│   └─ señalización /ws                         │                │                          │
│ DupliXon.Streaming (SIPSorcery WebRTC)        │═══ SRTP/UDP ══►│  4. <video> H.264 HW     │
│ DupliXon.Media (FFmpeg: ddagrab → GPU H.264)  │                └──────────────────────────┘
│ DupliXon.Core (perfiles, parser Annex-B)      │
└───────────────────────────────────────────────┘
```

## Flujo de una sesión
1. El dispositivo abre `http://<ip>:8787/?go=1` (QR) y envía `hello` con `screen.width/height` y `devicePixelRatio`.
2. El PC muestra la hoja **«X quiere ver tu pantalla»** → Aceptar / Rechazar (60 s de espera máximo).
3. Se calcula un `StreamProfile`: resolución nativa del panel (p. ej. iPad Pro 13" → 2752×2064), sin superar la del monitor; o 1080p/1440p/4K fijos.
4. El PC crea la `RTCPeerConnection` (solo envío, H.264 packetization-mode 1) y manda la oferta.
5. Al conectar, arranca FFmpeg: `ddagrab` (DXGI) → escalado → encoder HW con GOP de 1 s, sin B-frames, CBR.
6. Cada *access unit* (separado por AUD) se envía como un frame RTP.

## Capas
| Proyecto | Responsabilidad | Depende de |
|---|---|---|
| Core | Modelos y lógica pura, testeable | — |
| Media | Localiza FFmpeg, detecta encoder, captura y codifica | Core |
| Streaming | Sesión WebRTC por dispositivo | Core, Media |
| Server | HTTP + WebSocket, aprobación, registro de sesiones | Streaming |
| App | UI de escritorio | Server |
