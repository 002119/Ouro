# 🐍 OURO Replay Engine

> **Zero-Lag Retrospective Game Clipping Engine (`SKYX Labs • v1.1.0`)**  
> Graba tus mejores jugadas de los últimos 10 a 300 segundos (hasta 5 minutos) directamente en Memoria RAM sin escribir un solo byte en disco hasta que pulsas guardar.

---

## ⚡ Características Principales

- 🧠 **Zero-Disk RAM Buffer**: Búfer circular en memoria RAM (**10 a 300 s**) con aceleración por hardware (**DirectX 11 + NVIDIA NVENC H.264**) en **720p, 1080p, 1440p o 4K** a **30, 60, 120 o 144 FPS** (`10–50 Mbps`). 0 MB/s de desgaste en tu SSD mientras juegas.
- 🖥️ **Captura Multi-Monitor, Modos OBS y Escalado 16:9**: Elige entre pantalla completa por monitor físico (`Monitor`), detección automática de juego (`GameOnly`) o ventana específica estilo OBS (`SpecificWindow`), con escalado automático 16:9 por GPU (`StretchFilter`) y **Previsualización En Vivo** pausable para ahorrar GPU mientras juegas.
- 🎚️ **Audio Dual y MP4 Multipista**: Captura WASAPI Loopback (audio del sistema/juego) y micrófono en modos `DualTrack`, `GameOnly` o `MicOnly`, con selector de dispositivos, ganancia independiente (`0–200%`) y **pistas de audio separadas en el MP4** (activadas por defecto: **Pista 1 Mix** compatible con Discord/WhatsApp + **Pista 2 Juego** + **Pista 3 Micrófono** para Premiere, DaVinci o CapCut).
- 🔔 **Confirmación Discreta al Guardar**: Al pulsar tu atajo configurable (`F12`, `F11`, `F10`, `F9`, `F8`, `F7`, `Ctrl+F12`, `Alt+F10` o `Ctrl+Shift+S`), muestra una notificación HUD *click-through* en la esquina superior derecha (sin robar el foco del juego) y reproduce un tono armónico opcional (`clip_chime.wav`).
- 🏷️ **Overlay DirectX 11 Opcional en Clips**: Opción para incrustar por GPU (0% CPU) un badge con texto personalizado y telemetría (resolución y FPS) en los clips exportados (desactivado por defecto).
- 🎬 **Galería Integrada y 1 Clic a Discord**: Visualizador de clips organizados por carpeta de juego para reproducir, abrir carpeta o eliminar, junto con envío directo a **Webhooks de Discord**, verificación de conexión, publicación automática del manual de uso y compresión adaptada a límites de subida (**18 MB / 48 MB**).
- 🚀 **Bandeja del Sistema y Arranque Silencioso**: Minimizable a la bandeja de Windows y opción para iniciar automáticamente con Windows en segundo plano (`--tray`).
- 💾 **Sensor de Disco en Vivo**: Monitoreo en tiempo real del espacio libre disponible en la unidad de destino (con alerta visual por debajo de **10 GB**) y selectores rápidos `Disco C:\` / `Disco D:\`.

---

## 🔐 Licencia y Activación

Ouro utiliza **licencias anuales (1 año) offline firmadas con RSA-2048** y vinculadas al identificador de hardware (**HWID**) de tu equipo.

1. Únete al Discord oficial de **SKYX Labs**: **https://discord.gg/kB4uTYBRqD**
2. Acepta las reglas y verifícate en `#verificate`.
3. Abre Ouro, copia tu **HWID** con `Copiar ID` (o usa `Obtener Clave Gratis en Discord (SKYX Labs)`), solicita tu licencia gratuita de **1 año** en `#obtener-licencia` y pégala o cárgala con `Cargar archivo` en Ouro (`VALIDAR LICENCIA`).
4. La licencia queda guardada en `%APPDATA%\Ouro\license.ourolic` y la barra inferior muestra los días restantes (`✦ LICENCIA: ANUAL`).

Notas:

- Solo están habilitadas las **licencias anuales** (1 año); el formato `OURO2.<payload>.<firma>` o archivo `.ourolic` se verifica completamente offline.
- Una licencia solo funciona en el equipo cuyo HWID coincide con el firmado.
- No compartas tu archivo o token de licencia: contiene tu HWID.
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

**Requisitos**: Windows 11 22H2 o superior (`x64`) y GPU NVIDIA compatible con **NVENC**.

1. Ve a la sección de **[Releases](https://github.com/002119/Ouro/releases)** (o al canal `#descarga-ouro` en **SKYX Labs**: https://discord.gg/kB4uTYBRqD).
2. Descarga **`Ouro_Setup_v1.1.0.exe`** (Instalador recomendado) o **`Ouro_Portable_v1.1.0.zip`** (versión portable).
3. Ejecuta el instalador y sigue el asistente en pantalla, o descomprime el archivo portable y abre `Ouro.exe`.

---

## 🛠️ Tecnologías

- **Lenguaje y Runtime**: C# / .NET 8.0 (`win-x64`, mínimo declarado: Windows 11 22H2 x64)
- **Interfaz**: WPF (Windows Presentation Foundation) con tema Dark / Mint Cyberpunk
- **Gráficos & Captura**: Windows Graphics Capture (WGC) + Direct3D 11 + DXGI (`Vortice`)
- **Audio**: WASAPI Loopback + micrófono (pista AAC maestra y pistas separadas opcionales en MP4)
- **Codificación & Muxing**: NVIDIA NVENC Hardware H.264 + FFmpeg Nativo C API (`FFmpeg.AutoGen`)
- **Licenciamiento**: Licencias anuales offline firmadas con RSA-2048 (PKCS#1 SHA-256)
- **Instalador**: Inno Setup con compresión LZMA2 Ultra (`lzma2/ultra64`)

---

## 🔒 Seguridad

Consulta **[SECURITY.md](SECURITY.md)** para reportar vulnerabilidades, rotar credenciales y revisar el modelo de licenciamiento.

## 🧑‍💻 Desarrollo y preparación de distribución

Desde el código fuente, ejecuta **`Run_Ouro.bat`** con el SDK de **.NET 8 (`x64`)** instalado.

La preparación de paquetes de revisión y sus comprobaciones están documentadas en [Tools/Release/README.md](Tools/Release/README.md), y las pruebas de auditoría en [Tools/OuroAudit/README.md](Tools/OuroAudit/README.md).

---

© 2026 SKYX Labs.
