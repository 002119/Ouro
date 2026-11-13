# 🐍 OURO Replay Engine

> **Zero-Lag Retrospective Game Clipping Engine**  
> Graba tus mejores jugadas de los últimos 30-120 segundos directamente en Memoria RAM sin escribir un solo byte en disco hasta que pulsas guardar.

---

## ⚡ Características Principales

- 🧠 **Zero-Disk RAM Buffer**: Búfer circular en memoria RAM con aceleración por hardware (DirectX 11 + NVENC). 0 MB/s de desgaste en tu SSD mientras juegas.
- 🎧 **Feedback Auditivo Háptico**: Confirmación con suave tono armónico en auriculares al presionar la tecla de guardado (`F12`), sin overlays que tapen tu mira.
- 🎬 **Galería Integrada con 1 Clic a Discord**: Visualizador de clips por carpetas de juegos con subida directa a webhooks de Discord optimizada para límites de subida (18 MB / 48 MB).
- 🖥️ **Soporte Multi-Monitor & Modos OBS**: Captura de pantalla completa con selector de monitor físico o detección automática de ventana de juego.
- 🎚️ **Audio Juego + Micrófono**: Captura WASAPI Loopback y micrófono, mezclados en una sola pista AAC dentro del MP4.
- 🚀 **Arranque Silencioso**: Opción para iniciar con Windows minimizado en la bandeja del sistema (`--tray`).
- 💾 **Sensor de Disco en Vivo**: Monitoreo de espacio disponible en almacenamiento en tiempo real.

---

## 🔐 Licencia y Activación

Ouro utiliza **licencias offline firmadas con RSA-2048** y vinculadas al identificador de hardware (HWID) de tu equipo.

1. Abre Ouro. Si no hay una licencia válida, aparecerá la ventana de activación.
2. Pega el contenido de tu licencia (JSON o token `OURO2`) o pulsa **Cargar archivo** y selecciona el `.ourolic` que recibiste.
3. La licencia queda guardada en `%APPDATA%\Ouro\license.ourolic`.

Notas:

- Las claves antiguas de 12 caracteres **ya no son válidas**.
- Una licencia solo funciona en el equipo cuyo HWID coincide con el firmado.
- No compartas tu archivo de licencia: contiene tu HWID.
- La aplicación solo incluye la **clave pública** de verificación. La clave privada de emisión nunca se distribuye.

---

## 📁 Datos de Usuario

Ouro no escribe configuración ni credenciales en la carpeta de instalación.

| Ruta | Contenido |
| --- | --- |
| `%APPDATA%\Ouro\settings.json` | Preferencias del usuario (se crean al guardar cambios) |
| `%APPDATA%\Ouro\license.ourolic` | Licencia activada en este equipo |
| `%APPDATA%\Ouro\logs\ouro-YYYYMMDD.log` | Diagnóstico de errores no controlados |
| `appsettings.json` (instalación) | Plantilla de valores por defecto, solo lectura |

---

## 📥 Descarga & Instalación

1. Ve a la sección de **[Releases](https://github.com/002119/Ouro/releases)** (o al canal `#descarga-ouro` en **SKYX Labs**).
2. Descarga **`Ouro_Setup_v1.1.0.exe`** (Instalador recomendado) o **`Ouro_Portable_v1.1.0.zip`**.
3. Ejecuta el instalador y sigue el asistente en pantalla.

---

## 🛠️ Tecnologías

- **Lenguaje**: C# / .NET 8.0 (mínimo declarado actualmente: Windows 11 22H2 x64)
- **Interfaz**: WPF (Windows Presentation Foundation) con tema Dark / Mint Cyberpunk
- **Gráficos & Captura**: Windows Graphics Capture (WGC) + Direct3D 11 + DXGI (Vortice)
- **Audio**: WASAPI Loopback + micrófono, mezclados en una pista AAC
- **Codificación & Muxing**: FFmpeg Nativo C API (H.264 / AAC MP4)
- **Licenciamiento**: Licencias offline firmadas con RSA-2048 (PKCS#1 SHA-256)
- **Instalador**: Inno Setup con compresión LZMA2 Ultra

---

## 🔒 Seguridad

Consulta **[SECURITY.md](SECURITY.md)** para reportar vulnerabilidades, rotar credenciales y gestionar las claves de licencia.

## Desarrollo y preparación de distribución

Desde el código fuente, usa `Iniciar_Ouro.bat` con el SDK .NET 8 instalado. El
launcher compila en `bin/Launcher`, con archivos intermedios separados. Si mueves
el proyecto, ejecuta `powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Repair-Shortcut.ps1` para
actualizar el acceso directo local.

La preparación de paquetes y sus comprobaciones se describen en
[Tools/Release/README.md](Tools/Release/README.md). El proceso crea una carpeta
nueva de revisión y conserva los instaladores y portables existentes.

---

© 2026 SKYX Labs.
