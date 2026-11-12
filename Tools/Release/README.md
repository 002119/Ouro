# Preparar una distribución para revisión

Desde la raíz del proyecto:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Release/Prepare-Release.ps1
```

`Bypass` se aplica solamente a ese proceso; no cambia la política permanente de
Windows. Se necesita SDK .NET 8 y dependencias restauradas previamente.

El script publica en una carpeta nueva `publish/Review-FECHA-ID/Ouro`, copia avisos
disponibles de dependencias, genera un inventario y un manifiesto SHA-256 exterior
al payload. Rechaza claves, licencias de usuario, emisores, herramientas de auditoría,
configuración personal y patrones de secretos conocidos, incluso en DLL/EXE.
No elimina carpetas ni reemplaza instaladores anteriores; no publica en Internet.

La comprobación tiene pruebas con archivos sintéticos, sin red ni claves reales:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Release/Test-ReleaseChecks.ps1
```

Para repetir solo la revisión:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Release/Test-ReleasePayload.ps1 -PayloadPath "publish/Review-FECHA-ID/Ouro"
```

El resultado depende del Desktop Runtime .NET 8 x64. La firma de ejecutables, el
cumplimiento completo de licencias de FFmpeg y las pruebas de instalación limpia
siguen pendientes antes de preparar una release pública. El inventario NuGet no
enumera exhaustivamente todas las bibliotecas internas de la compilación FFmpeg.

Para proteger las claves locales sin cambiar su contenido ni el propietario:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Protect-LicenseKeys.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Protect-LicenseKeys.ps1 -VerifyOnly
```

Este último script requiere permiso para modificar/leer ACL de las dos claves.
Conserva acceso para su propietario real, SYSTEM y Administradores; la cuenta del
agente no se añade. `OuroKeyGen --check-key` comprueba acceso y coincidencia con la
clave pública sin emitir ni activar una licencia.
