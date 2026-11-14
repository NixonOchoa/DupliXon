# ADR 0002 — Audio del sistema y modo segunda pantalla

**Estado:** aceptado · 2026-11-14

## Audio
- Captura con **WASAPI loopback** (NAudio): todo lo que suena en el PC, sin dispositivos virtuales.
- Conversión a 48 kHz estéreo (re-muestreo lineal) y codificación **Opus** 20 ms con Concentus (C# puro, sin binarios nativos).
- Un único capturador/encoder compartido por todas las sesiones; se enciende solo cuando alguna sesión con audio está en vivo.
- Si no suena nada, WASAPI no entrega muestras: se rellenan con silencio para que el reloj RTP no se desfase del video.
- En Safari el sonido requiere un gesto: el toque en «Conectar» desbloquea la reproducción; si aun así se bloquea, aparece un botón 🔇 en el HUD.

## Segunda pantalla
- Windows solo extiende el escritorio a monitores reales; se usa un controlador **IddCx** de pantalla virtual
  (Virtual Display Driver, MIT, instalado vía winget con aprobación de administrador del usuario).
- Los monitores se enumeran con **DXGI** (Vortice) para obtener el par (adaptador, salida) exacto que usa `ddagrab`;
  FFmpeg recibe `-init_hw_device d3d11va=dx:<adaptador>` porque el monitor virtual puede pertenecer a otro adaptador.
- Al conectar se aplica con `ChangeDisplaySettingsEx` el modo del monitor virtual más cercano a la resolución nativa del dispositivo (misma relación de aspecto primero).

## Pendiente
- Control táctil del PC desde el iPad (DataChannel → SendInput), especialmente útil en modo segunda pantalla.
