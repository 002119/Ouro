[CmdletBinding()]
param([switch]$VerifyOnly)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$keyPaths = @('LicensingKeys\OuroPrivateKey.xml', 'Tools\LicensingKeys\OuroPrivateKey.xml')
$allowedSystemSids = @('S-1-5-18', 'S-1-5-32-544') # SYSTEM, local Administrators

foreach ($relative in $keyPaths) {
    $path = [IO.Path]::GetFullPath((Join-Path $repoRoot $relative))
    if (-not $path.StartsWith($repoRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'La ruta de la clave debe permanecer dentro del proyecto.'
    }
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { continue }
    $item = Get-Item -LiteralPath $path
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "No se modifican enlaces: $relative" }
    $existing = Get-Acl -LiteralPath $path
    $owner = $existing.GetOwner([Security.Principal.SecurityIdentifier])
    if ($owner.Value -in @('S-1-5-18', 'S-1-5-32-544')) {
        throw "La clave necesita un propietario de usuario identificado: $relative"
    }
    $allowed = @($owner.Value) + $allowedSystemSids
    if (-not $VerifyOnly) {
        # Preserve the actual owner's access, not the automation/sandbox identity.
        # Only the DACL changes: no key contents, ownership or license pair changes.
        $acl = New-Object Security.AccessControl.FileSecurity
        $acl.SetAccessRuleProtection($true, $false)
        foreach ($sidText in $allowed) {
            $sid = New-Object Security.Principal.SecurityIdentifier($sidText)
            $rule = New-Object Security.AccessControl.FileSystemAccessRule($sid, 'FullControl', 'Allow')
            $acl.AddAccessRule($rule)
        }
        Set-Acl -LiteralPath $path -AclObject $acl
    }
    $verified = Get-Acl -LiteralPath $path
    $rules = $verified.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier])
    $unexpected = @($rules | Where-Object { $_.IdentityReference.Value -notin $allowed -or $_.AccessControlType -ne 'Allow' })
    $missing = @($allowed | Where-Object { $sid = $_; -not @($rules | Where-Object { $_.IdentityReference.Value -eq $sid -and $_.AccessControlType -eq 'Allow' -and ($_.FileSystemRights -band [Security.AccessControl.FileSystemRights]::FullControl) -eq [Security.AccessControl.FileSystemRights]::FullControl }).Count })
    if (-not $verified.AreAccessRulesProtected -or $unexpected.Count -or $missing.Count) {
        throw "Permisos todavía amplios o incompletos: $relative"
    }
    Write-Output "OK permisos privados: $relative (propietario, SYSTEM y Administradores)."
}
