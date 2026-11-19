# DupliXon

Duplica la pantalla de tu PC con Windows en cualquier iPad, iPhone o navegador de tu Wi-Fi, **sin instalar nada en el dispositivo**.

- 📱 Escaneas un QR, se abre Safari, aceptas la solicitud en el PC y listo.
- ⚡ Captura por GPU (DXGI) y codificación por hardware (NVIDIA NVENC · Intel Quick Sync · AMD AMF) con WebRTC: baja latencia, 60 fps.
- 🔍 Resolución adaptada al panel del dispositivo (Auto) o fija en 1080p / 1440p / **4K**.
- 🔊 Audio del sistema (WASAPI loopback → Opus 160 kbps estéreo), activable o no.
- 🖥️ **Modo segunda pantalla**: extiende el escritorio al iPad usando una pantalla virtual que se ajusta sola a la resolución del dispositivo.
- 🔒 Nada se transmite sin tu aprobación; el video siempre va cifrado (DTLS-SRTP) y no sale de tu red local.
- 🪟 Interfaz estilo Apple con efecto glass (acrylic de Windows 11).

## Instalar
Descarga y ejecuta **`DupliXon-Setup-1.0.0.exe`**. El instalador incluye todo lo necesario
(no hace falta instalar .NET ni FFmpeg), crea los accesos directos, permite DupliXon en el firewall
para redes privadas y, opcionalmente, instala la pantalla virtual para el modo segunda pantalla.

Para generar el instalador desde el código:
```powershell
./scripts/build-installer.ps1   # → artifacts/DupliXon-Setup-<versión>.exe
```

## Créditos
Creado por **Nixon Ochoa** · njochoa8@gmail.com · [github.com/NixonOchoa](https://github.com/NixonOchoa)

## Requisitos para desarrollar
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

## Segunda pantalla
Windows solo puede extender el escritorio a una pantalla que exista. DupliXon usa el
**Parsec Virtual Display Driver** (gratuito), que se instala con un clic desde la app
(`winget install Parsec.ParsecVDD`, pide permiso de administrador). DupliXon conecta la pantalla virtual
al empezar la sesión y la retira al terminar; adopta la resolución disponible más cercana a la del iPad.
Ubícala a izquierda/derecha desde **Organizar** (Configuración → Pantalla).

## Control desde el iPad
Con **Ratón y teclado** activado en Ajustes:
| Gesto | Acción en el PC |
|---|---|
| Tocar | Clic |
| Mantener pulsado / tocar con dos dedos | Clic derecho |
| Arrastrar con un dedo | Arrastrar (seleccionar, mover ventanas) |
| Deslizar con dos dedos | Desplazar (scroll) |
| Trackpad / ratón del iPad | Puntero, clics y scroll |
| Botón ⌨︎ | Teclado en pantalla |
| Teclado físico | Teclas; ⌘ funciona como Ctrl (⌘C, ⌘V…) |

Desliza la barrita superior para mostrar los controles. Windows no deja controlar ventanas que se ejecutan como administrador.

## Pruebas
```powershell
dotnet test
```
Para pruebas E2E automáticas, la variable `DUPLIXON_DEV_AUTO_ACCEPT=1` acepta solicitudes sin mostrar la hoja (solo desarrollo).

## Hoja de ruta
- Control táctil del PC desde el iPad (DataChannel)
- Bitrate adaptativo por estadísticas RTCP y HEVC
- Descubrimiento mDNS (`duplixon.local`), ícono en bandeja e instalador MSIX
