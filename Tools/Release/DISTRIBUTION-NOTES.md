# Ouro: carpeta de revisión de distribución

Esta carpeta contiene un build para revisión local. No reemplaza los paquetes
publicados y no constituye una declaración de cumplimiento de todas las licencias.

- Es un build dependiente del runtime: necesita .NET 8 Desktop Runtime x64.
- El mínimo Windows declarado por el proyecto es Windows 11 22H2 x64.
- La grabación usa NVIDIA NVENC. Se conserva el código de captura, audio y búfer.
- `ThirdParty/packages.json` inventaría dependencias NuGet restauradas y metadata
  de licencias. También se copian avisos disponibles en la raíz de cada paquete.
- FFmpeg incluye componentes GPLv3. Antes de distribuir comercialmente, resolver
  las obligaciones de avisos, código fuente y compatibilidad de la integración.
  Copiar estos avisos o superar el escaneo no resuelve por sí solo esas obligaciones.
- El escaneo comprueba formatos conocidos; no certifica ausencia de todo secreto.
- La firma Authenticode y las pruebas de instalación limpia siguen pendientes.

Referencias del mantenedor: https://ffmpeg.org/legal.html y
https://www.gnu.org/licenses/gpl-faq.html.
