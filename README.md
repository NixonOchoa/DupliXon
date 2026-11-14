# DupliXon

Duplica la pantalla de tu PC con Windows en cualquier iPad, iPhone o navegador de tu Wi-Fi, **sin instalar nada en el dispositivo**.

- 📱 Escaneas un QR, se abre Safari, aceptas la solicitud en el PC y listo.
- ⚡ Captura por GPU (DXGI) y codificación por hardware (NVIDIA NVENC · Intel Quick Sync · AMD AMF) con WebRTC: baja latencia, 60 fps.
- 🔍 Resolución adaptada al panel del dispositivo (Auto) o fija en 1080p / 1440p / **4K**.
- 🔒 Nada se transmite sin tu aprobación; el video siempre va cifrado (DTLS-SRTP) y no sale de tu red local.
- 🪟 Interfaz estilo Apple con efecto glass (acrylic de Windows 11).

## Requisitos
- Windows 10 21H2+ (efecto glass completo en Windows 11 22H2+)
- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- FFmpeg ≥ 6.1 → `./scripts/install-ffmpeg.ps1` (usa winget)
- PC y dispositivo en la misma red Wi-Fi, marcada como **privada**

## Ejecutar
```powershell
./scripts/install-ffmpeg.ps1          # una vez
dotnet run --project src/DupliXon.App
```
La primera vez Windows preguntará si permites el acceso a la red: elige **Redes privadas**.
O bien ejecuta como administrador `./scripts/setup-firewall.ps1` tras compilar en Release.

## Usar
1. Abre DupliXon en el PC.
2. En el iPad/iPhone, apunta la cámara al QR y toca el enlace.
3. Acepta la solicitud en el PC.
4. Opcional: en Safari, *Compartir → Añadir a pantalla de inicio* para tenerlo como app a pantalla completa.

## Estructura
```
src/
  DupliXon.Core        Modelos, perfiles de resolución, parser H.264 Annex-B
  DupliXon.Media       FFmpeg: captura DXGI + encoder por hardware
  DupliXon.Streaming   Sesiones WebRTC (SIPSorcery)
  DupliXon.Server      Kestrel: receptor web (wwwroot) + señalización WebSocket
  DupliXon.App         App WPF (MVVM, CommunityToolkit) con tema glass
tests/DupliXon.Core.Tests
docs/                  Arquitectura y ADRs
scripts/               FFmpeg, firewall, íconos
```
Ver [docs/arquitectura.md](docs/arquitectura.md) y [docs/adr](docs/adr).

## Pruebas
```powershell
dotnet test
```
Para pruebas E2E automáticas, la variable `DUPLIXON_DEV_AUTO_ACCEPT=1` acepta solicitudes sin mostrar la hoja (solo desarrollo).

## Hoja de ruta
- Audio del sistema (WASAPI loopback → Opus)
- Control táctil del PC desde el iPad (DataChannel)
- Bitrate adaptativo por estadísticas RTCP y HEVC
- Descubrimiento mDNS (`duplixon.local`), ícono en bandeja e instalador MSIX
