# 🐍 OURO Replay Engine

> **Zero-Lag Retrospective Game Clipping Engine**  
> Graba tus mejores jugadas en los últimos 30-120 segundos directamente en Memoria RAM sin escribir un solo byte en disco hasta que pulsas guardar.

---

## ⚡ Características Principales

- 🧠 **Zero-Disk RAM Buffer**: Búfer circular en memoria RAM con aceleración por hardware (DirectX 11 + NVENC). 0 MB/s de desgaste en tu SSD mientras juegas.
- 🎧 **Feedback Auditivo Háptico**: Confirmación con suave tono armónico en auriculares al presionar la tecla de guardado (`F12`), sin overlays que tapen tu mira.
- 🎬 **Galería Integrada con 1 Clic a Discord**: Visualizador de clips por carpetas de juegos con subida directa a webhooks de Discord optimizada para límites de subida (18 MB / 48 MB).
- 🖥️ **Soporte Multi-Monitor & Modos OBS**: Captura de pantalla completa con selector de monitor físico o detección automática de ventana de juego.
- 🚀 **Arranque Silencioso**: Opción para iniciar con Windows minimizado en la bandeja del sistema (`--tray`).
- 💾 **Sensor de Disco en Vivo**: Monitoreo de espacio disponible en almacenamiento en tiempo real.

---

## 📥 Descarga & Instalación

1. Ve a la sección de **[Releases](../../releases)**.
2. Descarga **`Ouro_Setup_v1.0.0.exe`** (Instalador recomendado) o **`Ouro_Portable_v1.0.0.zip`**.
3. Ejecuta el instalador y sigue el asistente en pantalla.

---

## 🛠️ Tecnologías

- **Lenguaje**: C# / .NET 8.0 (Windows 10/11 x64)
- **Interfaz**: WPF (Windows Presentation Foundation) con tema Dark / Mint Cyberpunk
- **Gráficos & Captura**: Windows Graphics Capture (WGC) + Direct3D 11 + DXGI (Vortice)
- **Audio**: WASAPI Loopback dual-track (juego y micrófono por separado)
- **Codificación & Muxing**: FFmpeg Nativo C API (H.264 / AAC MP4)
- **Instalador**: Inno Setup con compresión LZMA2 Ultra

---

© 2026 Skyx Gaming.
