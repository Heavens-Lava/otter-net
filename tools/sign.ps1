<#
.SYNOPSIS
Signs otter-net's files for tools/package.ps1 -Sign (docs/SIGNING.md).

.DESCRIPTION
One entry point for every platform; the provider comes from
OTTER_SIGN_PROVIDER, so the same package.ps1 run signs with a real identity
in an approved CI run and with a throwaway one in the rehearsal that every
package.yml run makes. Nothing here buys, registers or uploads anything
except the notarization submission of the developer-id provider.

-Kind exe    Windows executables (otter-net.exe, otter-desktop.exe)
-Kind msi    the Windows installer
-Kind macho  the macOS executable (otter-net)
-Kind pkg    the macOS installer: -Files is the unsigned .pkg; it is replaced
             by the signed (and, with notarization, stapled) one

Providers (OTTER_SIGN_PROVIDER):

  artifact-signing  Windows, Azure Artifact Signing through signtool and its
                    dlib. OTTER_SIGN_DLIB = Azure.CodeSigning.Dlib.dll,
                    OTTER_SIGN_METADATA = metadata.json (Endpoint,
                    CodeSigningAccountName, CertificateProfileName); the
                    Azure login (OIDC) comes from azure/login.
  pfx               Windows, a .pfx file: OTTER_SIGN_PFX, OTTER_SIGN_PFX_PASSWORD.
                    Used by the rehearsal with a self-signed certificate.
  developer-id      macOS: codesign with OTTER_SIGN_APP_IDENTITY ("Developer
                    ID Application: ..."), productsign with
                    OTTER_SIGN_INSTALLER_IDENTITY ("Developer ID Installer:
                    ..."), both from the keychain (OTTER_SIGN_KEYCHAIN if not
                    the default). With OTTER_NOTARY_KEY (path of the App
                    Store Connect .p8), OTTER_NOTARY_KEY_ID and
                    OTTER_NOTARY_ISSUER: notarytool submit --wait, and the
                    .pkg is stapled.
  adhoc             macOS rehearsal: an ad-hoc signature with the Hardened
                    Runtime (proves the executable runs under it); a .pkg is
                    left unsigned.

Every Windows signature is SHA-256 with an RFC 3161 timestamp
(OTTER_SIGN_TIMESTAMP, default http://timestamp.acs.microsoft.com) and is
verified after signing.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('exe', 'msi', 'macho', 'pkg')][string]$Kind,
    [Parameter(Mandatory)][string[]]$Files
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3
$provider = $env:OTTER_SIGN_PROVIDER
if (-not $provider) { throw 'OTTER_SIGN_PROVIDER is not set (artifact-signing, pfx, developer-id or adhoc; docs/SIGNING.md).' }

function Invoke-Checked {
    param([string]$File, [string[]]$Arguments)
    & $File @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$([System.IO.Path]::GetFileName($File)) failed ($LASTEXITCODE)." }
}

# notarytool submit --wait, which must end Accepted.
function Submit-Notarization([string]$Path) {
    $json = & xcrun notarytool submit $Path --key $env:OTTER_NOTARY_KEY --key-id (Get-Required 'OTTER_NOTARY_KEY_ID') --issuer (Get-Required 'OTTER_NOTARY_ISSUER') --wait --output-format json 2>&1 | Out-String
    $code = $LASTEXITCODE
    try { $result = $json | ConvertFrom-Json } catch { throw "notarytool (exit $code) did not answer in JSON: $json" }
    if ($code -ne 0 -or $result.status -ne 'Accepted') { throw "Notarization of $Path ended '$($result.status)' (submission $($result.id)); xcrun notarytool log $($result.id) shows why." }
    Write-Host "  notarized: submission $($result.id) Accepted"
}

function Get-Required([string]$Name) {
    $value = [Environment]::GetEnvironmentVariable($Name)
    if (-not $value) { throw "$Name is not set (OTTER_SIGN_PROVIDER=$provider; docs/SIGNING.md)." }
    $value
}

# signtool.exe from the newest Windows SDK, for this machine's processor.
function Find-SignTool {
    $kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $arch = if ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'Arm64') { 'arm64' } else { 'x64' }
    $found = Get-ChildItem -LiteralPath $kits -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^\d+\.' } |
        Sort-Object { [version]$_.Name } -Descending |
        ForEach-Object { foreach ($a in @($arch, 'x64')) { Join-Path $_.FullName "$a\signtool.exe" } } |
        Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $found) { throw "signtool.exe was not found under $kits (the Windows SDK)." }
    $found
}

foreach ($file in $Files) { if (-not (Test-Path -LiteralPath $file)) { throw "No file to sign: $file" } }

switch ($Kind) {
    { $_ -in 'exe', 'msi' } {
        $signtool = Find-SignTool
        $timestamp = if ($env:OTTER_SIGN_TIMESTAMP) { $env:OTTER_SIGN_TIMESTAMP } else { 'http://timestamp.acs.microsoft.com' }
        $common = @('sign', '/v', '/fd', 'SHA256', '/tr', $timestamp, '/td', 'SHA256')
        # A .pfx is imported into the user's certificate store for the run and
        # chosen by thumbprint, so its password never is a command-line argument.
        $imported = $null
        $identity = switch ($provider) {
            'artifact-signing' { @('/dlib', (Get-Required 'OTTER_SIGN_DLIB'), '/dmdf', (Get-Required 'OTTER_SIGN_METADATA')) }
            'pfx' {
                $imported = Import-PfxCertificate -FilePath (Get-Required 'OTTER_SIGN_PFX') -CertStoreLocation Cert:\CurrentUser\My -Password (ConvertTo-SecureString (Get-Required 'OTTER_SIGN_PFX_PASSWORD') -AsPlainText -Force)
                @('/sha1', $imported.Thumbprint, '/s', 'My')
            }
            default { throw "OTTER_SIGN_PROVIDER=$provider cannot sign Windows files." }
        }
        try {
        foreach ($file in $Files) {
            Write-Host "Signing $file ($provider) ..."
            # Up to three tries: a timestamp server that does not answer once
            # should not fail a build.
            for ($try = 1; ; $try++) {
                & $signtool @($common + $identity + @($file))
                if ($LASTEXITCODE -eq 0) { break }
                if ($try -ge 3) { throw "signtool failed ($LASTEXITCODE) for $file." }
                Write-Host "  signtool failed ($LASTEXITCODE); trying again in 10 s"
                Start-Sleep -Seconds 10
            }
            $signature = Get-AuthenticodeSignature -LiteralPath $file
            if (-not $signature.SignerCertificate) { throw "$file has no signature after signing." }
            if ($provider -ne 'pfx' -and $signature.Status -ne 'Valid') { throw "$file's signature is $($signature.Status): $($signature.StatusMessage)" }
            if (-not $signature.TimeStamperCertificate) { throw "$file's signature has no timestamp." }
            Write-Host "  $($signature.Status): $($signature.SignerCertificate.Subject); timestamp by $($signature.TimeStamperCertificate.Subject)"
        }
        } finally {
            if ($imported) { Remove-Item -LiteralPath "Cert:\CurrentUser\My\$($imported.Thumbprint)" -ErrorAction SilentlyContinue }
        }
    }
    'macho' {
        $identity = switch ($provider) {
            'developer-id' { Get-Required 'OTTER_SIGN_APP_IDENTITY' }
            'adhoc' { '-' }
            default { throw "OTTER_SIGN_PROVIDER=$provider cannot sign macOS files." }
        }
        foreach ($file in $Files) {
            Write-Host "Signing $file ($provider, Hardened Runtime) ..."
            $arguments = @('--force', '--options', 'runtime', '--sign', $identity)
            if ($provider -eq 'developer-id') { $arguments += '--timestamp' }
            if ($env:OTTER_SIGN_KEYCHAIN) { $arguments += @('--keychain', $env:OTTER_SIGN_KEYCHAIN) }
            Invoke-Checked codesign ($arguments + @($file))
            Invoke-Checked codesign @('--verify', '--strict', '--verbose=2', $file)
            if ($provider -eq 'developer-id' -and $env:OTTER_NOTARY_KEY) {
                # A bare executable cannot be stapled: Gatekeeper finds its
                # ticket online. notarytool takes it in a zip.
                $zip = "$file.notarize.zip"
                Invoke-Checked ditto @('-c', '-k', '--keepParent', $file, $zip)
                try {
                    Submit-Notarization $zip
                } finally { Remove-Item -LiteralPath $zip -Force -ErrorAction SilentlyContinue }
            }
        }
    }
    'pkg' {
        foreach ($file in $Files) {
            if ($provider -eq 'adhoc') { Write-Host "$file stays unsigned (adhoc: an installer package needs a Developer ID Installer certificate)."; continue }
            if ($provider -ne 'developer-id') { throw "OTTER_SIGN_PROVIDER=$provider cannot sign a .pkg." }
            $signed = "$file.signed"
            $arguments = @('--sign', (Get-Required 'OTTER_SIGN_INSTALLER_IDENTITY'), '--timestamp')
            if ($env:OTTER_SIGN_KEYCHAIN) { $arguments += @('--keychain', $env:OTTER_SIGN_KEYCHAIN) }
            Invoke-Checked productsign ($arguments + @($file, $signed))
            Move-Item -LiteralPath $signed -Destination $file -Force
            Invoke-Checked pkgutil @('--check-signature', $file)
            if ($env:OTTER_NOTARY_KEY) {
                Submit-Notarization $file
                Invoke-Checked xcrun @('stapler', 'staple', $file)
                Invoke-Checked spctl @('--assess', '--type', 'install', '--verbose', $file)
            }
        }
    }
}
