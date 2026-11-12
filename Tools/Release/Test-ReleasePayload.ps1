[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$PayloadPath)

$ErrorActionPreference = 'Stop'
$payload = (Resolve-Path -LiteralPath $PayloadPath).ProviderPath
$entries = @(Get-ChildItem -LiteralPath $payload -Recurse -Force)
if (@($entries | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) {
    throw 'El paquete no debe incluir enlaces o puntos de reanalisis.'
}
$files = @($entries | Where-Object { -not $_.PSIsContainer })
foreach ($required in @('Ouro.exe', 'Ouro.dll', 'Ouro.deps.json', 'Ouro.runtimeconfig.json', 'appsettings.json')) {
    if (-not (Test-Path -LiteralPath (Join-Path $payload $required) -PathType Leaf)) {
        throw "Falta un archivo requerido: $required"
    }
}
$sensitivePath = '(?i)(^|[\\/])(LicensingKeys|OuroKeyGen|OuroAudit)([\\/]|\.)|PrivateKey|(^|[\\/])\.env($|\.)|\.(ourolic|lic|pem|pfx|snk|cs|ps1|bat|lnk)$|appsettings\.(local|user)\.json$'
$secretPatterns = @(
    'https://(?:canary\.|ptb\.)?discord(?:app)?\.com/api(?:/v[0-9]+)?/webhooks/[0-9]+/[A-Za-z0-9_-]{20,}',
    '<RSAKeyValue>[\s\S]{0,16384}<D>',
    '-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----',
    '\bgh[pousr]_[A-Za-z0-9]{30,}\b',
    '\bgithub_pat_[A-Za-z0-9_]{30,}\b'
)
foreach ($file in $files) {
    $relative = $file.FullName.Substring($payload.Length).TrimStart('\', '/')
    if ($relative -match $sensitivePath) { throw "Archivo no distribuible detectado: $relative" }
    if ($file.Extension -in @('.exe', '.dll', '.json', '.xml', '.txt', '.config', '.md', '.pdb')) {
        $bytes = [IO.File]::ReadAllBytes($file.FullName)
        foreach ($encoding in @([Text.Encoding]::UTF8, [Text.Encoding]::Unicode)) {
            $content = $encoding.GetString($bytes)
            foreach ($pattern in $secretPatterns) {
                if ([regex]::IsMatch($content, $pattern, [Text.RegularExpressions.RegexOptions]::CultureInvariant, [TimeSpan]::FromSeconds(5))) {
                    throw "Posible secreto detectado en $relative. El valor se omite."
                }
            }
        }
    }
}
$config = Get-Content -LiteralPath (Join-Path $payload 'appsettings.json') -Raw | ConvertFrom-Json
if (-not $config.OuroSettings) { throw 'Falta la seccion OuroSettings.' }
foreach ($property in @('DiscordWebhookUrl', 'SelectedAudioOutputDeviceId', 'SelectedAudioInputDeviceId', 'SelectedWindowTitle')) {
    if (-not [string]::IsNullOrWhiteSpace([string]$config.OuroSettings.$property)) {
        throw "La plantilla contiene un valor de usuario: $property"
    }
}
Write-Output "OK paquete: $($files.Count) archivos revisados; sin secretos reconocidos ni material de emision."
