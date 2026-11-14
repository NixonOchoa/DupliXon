# ADR 0001 — Receptor en Safari con WebRTC

**Estado:** aceptado · 2026-11-14

## Contexto
Se quiere duplicar la pantalla de Windows en iPad/iPhone **sin instalar nada** en el dispositivo.

## Opciones descartadas
- **AirPlay**: iOS/iPadOS solo *envía* AirPlay; no puede ser receptor.
- **AirDrop**: usa AWDL (protocolo propietario de Apple); Windows no puede emitir solicitudes AirDrop.
- **App nativa iOS**: contradice el requisito de cero instalación.
- **WebCodecs + WebSocket**: exige contexto seguro (HTTPS), lo que obliga a certificados autofirmados con advertencias en Safari.

## Decisión
- El receptor es una página web servida por el PC (`http://<ip>:8787`), abierta escaneando un QR con la cámara.
- El video viaja por **WebRTC** (H.264, cifrado DTLS-SRTP obligatorio). `RTCPeerConnection` funciona en HTTP en la LAN.
- La "solicitud tipo AirDrop" se invierte: el dispositivo pide conectarse y **el PC acepta o rechaza**.
- Captura con DXGI Desktop Duplication y codificación por GPU (NVENC / Quick Sync / AMF) usando FFmpeg como motor.

## Consecuencias
- Latencia baja (≈ 30–80 ms en Wi-Fi 5/6), resolución adaptada al panel del dispositivo, hasta 4K.
- Dependencia de FFmpeg (≥ 6.1) instalado vía winget o incluido junto al ejecutable.
- La app puede guardarse en la pantalla de inicio del iPad (PWA) para abrirla como app.
