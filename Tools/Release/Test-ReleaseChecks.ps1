[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$publishRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'publish'))
$testRoot = [IO.Path]::GetFullPath((Join-Path $publishRoot ('GateTests-' + [Guid]::NewGuid().ToString('N'))))
if (-not $testRoot.StartsWith($publishRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Ruta de prueba fuera de publish.' }
$gate = Join-Path $PSScriptRoot 'Test-ReleasePayload.ps1'
$passed = 0
function Expect-Rejected([string]$name, [string]$message) {
    try { & $gate -PayloadPath $testRoot | Out-Null }
    catch {
        if ($_.Exception.Message -notlike $message) { throw }
        Write-Output "PASS $name"
        $script:passed++
        return
    }
    throw "No se rechazo la prueba: $name"
}
New-Item -ItemType Directory -Path $testRoot | Out-Null
try {
    foreach ($name in @('Ouro.exe', 'Ouro.dll', 'Ouro.deps.json', 'Ouro.runtimeconfig.json')) {
        [IO.File]::WriteAllText((Join-Path $testRoot $name), '{}')
    }
    $configPath = Join-Path $testRoot 'appsettings.json'
    $cleanConfig = '{"OuroSettings":{"DiscordWebhookUrl":"","SelectedAudioOutputDeviceId":"","SelectedAudioInputDeviceId":"","SelectedWindowTitle":""}}'
    [IO.File]::WriteAllText($configPath, $cleanConfig)
    & $gate -PayloadPath $testRoot | Out-Null
    Write-Output 'PASS payload generico aceptado'; $passed++

    $privateFile = Join-Path $testRoot 'OuroPrivateKey.xml'
    [IO.File]::WriteAllText($privateFile, 'synthetic')
    Expect-Rejected 'nombre de clave privada rechazado' '*Archivo no distribuible*'
    Remove-Item -LiteralPath $privateFile

    $licenseFile = Join-Path $testRoot 'customer.ourolic'
    [IO.File]::WriteAllText($licenseFile, 'synthetic')
    Expect-Rejected 'licencia de cliente rechazada' '*Archivo no distribuible*'
    Remove-Item -LiteralPath $licenseFile

    $secretFile = Join-Path $testRoot 'accidental.json'
    # Fictitious value; never used as a network destination.
    [IO.File]::WriteAllText($secretFile, 'https://discord.com/api/webhooks/123456789/SYNTHETIC_AUDIT_TOKEN_NOT_REAL_123456789')
    Expect-Rejected 'webhook sintetico en JSON rechazado' '*Posible secreto*'
    Remove-Item -LiteralPath $secretFile

    [IO.File]::WriteAllText((Join-Path $testRoot 'Ouro.dll'), '-----BEGIN PRIVATE KEY-----synthetic', [Text.Encoding]::Unicode)
    Expect-Rejected 'patron privado UTF16 en DLL rechazado' '*Posible secreto*'
    [IO.File]::WriteAllText((Join-Path $testRoot 'Ouro.dll'), '{}')

    [IO.File]::WriteAllText($configPath, $cleanConfig.Replace('"SelectedWindowTitle":""', '"SelectedWindowTitle":"Personal window"'))
    Expect-Rejected 'configuracion personal rechazada' '*valor de usuario*'
    [IO.File]::WriteAllText($configPath, $cleanConfig)

    Remove-Item -LiteralPath (Join-Path $testRoot 'Ouro.exe')
    Expect-Rejected 'ejecutable ausente rechazado' '*Falta un archivo requerido*'
    Write-Output "RELEASE_GATE_TESTS=$passed; FAILED=0"
}
finally {
    # Validate the final absolute deletion target, then use one native shell only.
    $resolved = [IO.Path]::GetFullPath($testRoot)
    if (-not $resolved.StartsWith($publishRoot + '\', [StringComparison]::OrdinalIgnoreCase) -or
        (Get-Item -LiteralPath $resolved).Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw 'Se rechaza la limpieza fuera de la carpeta temporal verificada.'
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
