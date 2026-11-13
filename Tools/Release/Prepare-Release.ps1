[CmdletBinding()]
param(
    [switch]$SelfContained
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$id = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$reviewRoot = Join-Path $repoRoot "publish\Review-$id"
$payload = Join-Path $reviewRoot 'Ouro'
# A new output directory preserves all previous installers and portable packages.
if (Test-Path -LiteralPath $reviewRoot) { throw 'La carpeta de revision ya existe.' }
New-Item -ItemType Directory -Path $payload | Out-Null
Push-Location $repoRoot
try {
    $publishArgs = @(
        'publish', (Join-Path $repoRoot 'Ouro.csproj'),
        '-c', 'Release',
        '--no-restore',
        '-p:IntermediateOutputPath=obj/ReleaseReview/',
        '-p:PublishDir='
    )
    if ($SelfContained) {
        $publishArgs += @('-r', 'win-x64', '--self-contained', 'true')
    }
    $publishArgs += @('-o', $payload)

    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) { throw 'Fallo la publicacion; no se genera un paquete aprobado.' }

    # Resolve package metadata from the actual restored dependency graph.
    $assets = Get-Content -LiteralPath (Join-Path $repoRoot 'obj\project.assets.json') -Raw | ConvertFrom-Json
    $packageFolders = @($assets.packageFolders.PSObject.Properties.Name)
    $notices = Join-Path $payload 'ThirdParty'
    New-Item -ItemType Directory -Path $notices | Out-Null
    $packages = foreach ($library in $assets.libraries.PSObject.Properties) {
        if ($library.Value.type -ne 'package') { continue }
        $identity = $library.Name.Split('/')
        $packageDirectory = $null
        foreach ($folder in $packageFolders) {
            $candidate = Join-Path $folder $library.Value.path
            if (Test-Path -LiteralPath $candidate -PathType Container) { $packageDirectory = $candidate; break }
        }
        if (-not $packageDirectory) { throw "No se encuentra metadata de $($identity[0])." }
        $nuspecFile = Get-ChildItem -LiteralPath $packageDirectory -Filter '*.nuspec' | Select-Object -First 1
        if (-not $nuspecFile) { throw "Falta nuspec de $($identity[0])." }
        [xml]$nuspec = Get-Content -LiteralPath $nuspecFile.FullName -Raw
        $metadata = $nuspec.package.metadata
        $packageNotices = @()
        foreach ($notice in Get-ChildItem -LiteralPath $packageDirectory -File | Where-Object { $_.Name -match '(?i)^(license|licence|copying|third-party-notices|notice)(\..*)?$' }) {
            $targetDirectory = Join-Path $notices ($identity[0] + '-' + $identity[1])
            New-Item -ItemType Directory -Path $targetDirectory -Force | Out-Null
            Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path $targetDirectory $notice.Name)
            $packageNotices += $notice.Name
        }
        [ordered]@{
            Id = $identity[0]; Version = $identity[1]
            License = [string]$metadata.license.InnerText
            LicenseType = [string]$metadata.license.type
            ProjectUrl = [string]$metadata.projectUrl
            LicenseUrl = [string]$metadata.licenseUrl
            CopiedNotices = $packageNotices
        }
    }
    @($packages) | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $notices 'packages.json') -Encoding UTF8
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'DISTRIBUTION-NOTES.md') -Destination (Join-Path $payload 'DISTRIBUTION-NOTES.md')
    & (Join-Path $PSScriptRoot 'Test-ReleasePayload.ps1') -PayloadPath $payload

    $manifest = foreach ($file in Get-ChildItem -LiteralPath $payload -Recurse -File | Sort-Object FullName) {
        [ordered]@{
            Path = $file.FullName.Substring($payload.Length).TrimStart('\', '/')
            Bytes = $file.Length
            Sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
    [ordered]@{
        BuiltAtUtc = [DateTime]::UtcNow.ToString('o')
        BaseCommit = (& git rev-parse HEAD)
        IncludesUncommittedChanges = [bool]@(& git status --porcelain).Count
        SelfContained = [bool]$SelfContained
        Payload = 'Ouro'
        Files = @($manifest)
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $reviewRoot 'manifest.json') -Encoding UTF8
    Write-Output "PREPARACION_OK=$reviewRoot"
    Write-Output 'Carpeta de revision local; no se ha subido ni reemplazado una release.'
}
finally { Pop-Location }
