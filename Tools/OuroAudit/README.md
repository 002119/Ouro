# Pruebas de auditoría de Ouro

## Primera tanda: compartir en Discord

```powershell
dotnet run --project Tools/OuroAudit/OuroAudit.csproj -c Release -- --discord
dotnet run --project Tools/OuroAudit/OuroAudit.csproj -c Release -- --discord-smoke
```

Resultados: 34/34 comprobaciones de regresión y 4/4 comprobaciones con FFmpeg real.
La primera suite simula respuestas HTTP; no usa red ni webhooks reales. Comprueba
validación de destinos, envío directo, límites seleccionados de 18/48 MB,
reintentos por 413, limpieza de temporales y terminación de un proceso bloqueado.
La segunda genera un vídeo sintético de dos segundos y lo comprime con el camino
real de producción; comprueba H.264/AAC, duración y conservación del original.
No equivale a una prueba de sincronización audiovisual de una partida.

Se conservan los umbrales, cálculo de bitrate, presets, audio, escalado y orden
NVENC/CPU previos. No se han modificado interfaz, buffers ni captura.

## Auditoría inicial (antes de las correcciones)

Ejecutar desde la raíz: `dotnet run --project Tools/OuroAudit/OuroAudit.csproj -c Release`.

Estas pruebas llaman clases reales del proyecto. No leen claves privadas, no activan
licencias, no guardan preferencias y solo envían una petición HTTP a un servidor
temporal en loopback. El paquete FFmpeg se carga para comprobar referencias nativas.

Resultado inicial: 13 comprobaciones correctas y 4 requisitos incumplidos. El código
de salida 1 es intencional cuando se reproducen esos defectos. No equivale a un error
de compilación. Los fallos cubren nombres de carpeta, destinos webhook y liberación
de snapshots tras fallar la exportación. El probe libera sus referencias al terminar.

La prueba de referencias ilustra el límite entre buffer y exportador; la conclusión
de fuga en la aplicación requiere además revisar que ClipOrchestrator tampoco
libera esos snapshots, como documenta el informe. No se prueba el rendimiento GPU,
la salida audiovisual real ni la activación positiva con una licencia de producción.
