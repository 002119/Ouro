# Política de Seguridad de Ouro

## Reportar una vulnerabilidad

No publiques credenciales, claves privadas ni datos de usuarios en issues públicos.
Si encuentras un problema de seguridad, contacta de forma privada al mantenedor del
repositorio (`https://github.com/002119/Ouro`) antes de divulgarlo.

---

## Gestión de secretos

Nunca deben incluirse en el repositorio, en un instalador ni en un artefacto portable:

- `LicensingKeys/OuroPrivateKey.xml` y cualquier copia de la clave privada RSA.
- Archivos `.ourolic`, `*.pem`, `*.pfx`, `*.snk` y `.env`.
- URLs de webhooks de Discord, tokens de API o credenciales de servicios.
- `appsettings.local.json`, `appsettings.user.json` o configuraciones específicas de una máquina.

El archivo `appsettings.json` versionado debe contener **solo valores por defecto vacíos o genéricos**.
La configuración real del usuario se guarda en `%APPDATA%\Ouro\settings.json`.

### Clave privada de licencias

La clave privada RSA-2048 (`LicensingKeys/OuroPrivateKey.xml`) es el único elemento capaz de emitir
licencias válidas. Debe mantenerse:

- Fuera de git y de cualquier copia pública del proyecto.
- Preferiblemente en un equipo offline o en un gestor de secretos.
- Con copia de seguridad cifrada.

Si la clave privada se filtra:

1. Genera un nuevo par RSA-2048.
2. Sustituye la clave pública en `Services/Licensing/OuroLicensePublicKey.cs`.
3. Recompila y redistribuye el cliente.
4. Reemite todas las licencias activas con la nueva clave privada.
5. Considera las licencias antiguas como comprometidas.

El emisor (`Tools/OuroKeyGen`) comprueba que la clave privada coincide con la pública
embebida en el cliente antes de generar licencias, para evitar distribuir licencias inválidas.

El mantenimiento local incluye `Tools/Protect-LicenseKeys.ps1`: restringe las dos
copias conocidas de la clave al propietario real del archivo, SYSTEM y Administradores.
No cambia la clave, su propietario ni la validez de las licencias existentes. Requiere
permisos de administración de la ACL. `-VerifyOnly` comprueba la protección sin modificarla.
El emisor permite `--check-key` para comprobar acceso y coincidencia sin emitir licencias.
La exclusión de Git no sustituye esta protección del archivo local.

### Rotación del webhook de Discord

Un webhook de Discord publicado en git debe considerarse comprometido incluso si ya se eliminó
del último commit, porque permanece en el historial. Para revocarlo:

1. En Discord, abre **Configuración del servidor → Integraciones → Webhooks**.
2. Elimina el webhook antiguo (o regenera su URL).
3. Crea un webhook nuevo y copia la URL.
4. Pega la nueva URL únicamente en **Ouro → Preferencias**, no en `appsettings.json` ni en el repositorio.

Mientras el webhook antiguo no se elimine en Discord, cualquiera que conserve la URL podrá publicar
mensajes en el canal correspondiente.

### Historial de git

Eliminar un secreto del commit actual no lo borra del historial. Si es necesario purgar el historial:

1. Rota primero el secreto afectado.
2. Haz una copia de seguridad del repositorio.
3. Usa `git filter-repo` (o una herramienta equivalente) para eliminar la ruta o el valor del historial.
4. Fuerza la actualización de las ramas y avisa a todos los colaboradores.

La reescritura de historial es destructiva y debe coordinarse con todo el equipo.

---

## Modelo de licenciamiento

- Las licencias son archivos JSON firmados, o tokens compactos `OURO2.<payload>.<firma>`.
- La firma usa RSA-2048 con PKCS#1 v1.5 y SHA-256.
- El payload canónico es `Version|Product|HWID|Plan|IssuedAtUnix|ExpiresAtUnix|Nonce`.
- El cliente solo lleva la clave pública; no puede emitir licencias nuevas.
- Cada licencia está vinculada al HWID del equipo y, si es mensual, a una fecha de vencimiento.
- Las claves legacy de 12 caracteres basadas en HMAC quedaron obsoletas y se rechazan.
