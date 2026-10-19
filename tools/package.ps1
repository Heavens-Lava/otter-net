<#
.SYNOPSIS
Builds otter-net's release-candidate packages for one runtime.

.DESCRIPTION
Publishes the native otter (Native AOT) for -Runtime and writes, into
-OutputDirectory (artifacts/packages by default):

  otter-net-<version>-<rid>.zip       Windows: otter.exe (+ otter-desktop.exe
                                      once src/Otter.Desktop exists), LICENSE,
                                      README.md, VERSION,
                                      THIRD-PARTY-NOTICES.txt, SHA256SUMS
  otter-net-<version>-<rid>.tar.gz    macOS and Linux: bin/otter, install.sh,
                                      uninstall.sh and the same documents

and with -Installer the platform's installer, built on that platform:

  otter-net-<version>-<rid>.msi       Windows (WiX 5, distribution/windows)
  otter-net-<version>-<rid>.pkg       macOS (pkgbuild + productbuild, unsigned)
  otter-net_<version>_<arch>.deb      Linux and Chromebooks (dpkg-deb)

Each package gets a <file>.sha256, and SHA256SUMS lists every package in the
folder. The version comes from VERSION (the one source; MSBuild reads it
too). Nothing is signed and nothing is published: docs/PACKAGING.md.

Native AOT needs the platform's linker (tools/publish.ps1 says which). Works
in PowerShell 7 on every platform.

.EXAMPLE
pwsh tools/package.ps1 -Runtime osx-arm64 -Installer
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('win-x64', 'win-arm64', 'osx-arm64', 'osx-x64', 'linux-x64', 'linux-arm64')]
    [string]$Runtime,
    # Also build the installer (.msi, .pkg or .deb); needs the runtime's own platform.
    [switch]$Installer,
    [string]$OutputDirectory = '',
    # Overrides VERSION: only for building an "older" package to test upgrades.
    [string]$Version = '',
    # An existing publish folder with the native otter, instead of publishing now.
    [string]$PublishDirectory = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3
$root = Split-Path -Parent $PSScriptRoot
$utf8 = [System.Text.UTF8Encoding]::new($false)

if (-not $Version) { $Version = [System.IO.File]::ReadAllText((Join-Path $root 'VERSION')).Trim() }
if ($Version -notmatch '^(\d+)\.(\d+)\.(\d+)(-[0-9A-Za-z.]+)?$') { throw "Version '$Version' is not <major>.<minor>.<patch>[-<prerelease>]." }
$numericVersion = "$($Matches[1]).$($Matches[2]).$($Matches[3])"
# What `otter --version` must print: the Otter language version (Program.Version).
$languageVersion = if ((Get-Content -Raw (Join-Path $root 'src/Otter.Cli/Program.cs')) -match 'const string Version = "([^"]+)"') { $Matches[1] } else { throw 'Program.Version not found.' }

$os, $arch = $Runtime.Split('-')
$onWindows = [System.IO.Path]::DirectorySeparatorChar -eq '\'
$hostOs = if ($onWindows) { 'win' } elseif ($IsMacOS) { 'osx' } else { 'linux' }
$hostRid = "$hostOs-" + [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
$exe = if ($os -eq 'win') { 'otter.exe' } else { 'otter' }
$name = "otter-net-$Version-$Runtime"

$artifacts = Join-Path $root 'artifacts'
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $artifacts 'packages' }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$OutputDirectory = (Resolve-Path $OutputDirectory).Path
$work = Join-Path $artifacts "package-work/$Runtime"
if (Test-Path $work) { Remove-Item -Recurse -Force $work }
New-Item -ItemType Directory -Force -Path $work | Out-Null

function Invoke-Native {
    param([string]$File, [string[]]$Arguments)
    & $File @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$File $($Arguments -join ' ') failed ($LASTEXITCODE)" }
}

function Write-Sha256 {
    param([string]$Path)
    $hash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    [System.IO.File]::WriteAllText("$Path.sha256", "$hash  $([System.IO.Path]::GetFileName($Path))`n", $utf8)
    $hash
}

# Lists every file under $Folder with its SHA-256, as sha256sum prints them.
function Write-Sha256Sums {
    param([string]$Folder, [string]$Path, [string[]]$Files)
    $lines = foreach ($file in $Files) {
        $hash = (Get-FileHash -LiteralPath (Join-Path $Folder $file) -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $($file.Replace('\', '/'))"
    }
    [System.IO.File]::WriteAllText($Path, (($lines -join "`n") + "`n"), $utf8)
}

# ---- 1. The native executable ------------------------------------------------

if (-not $PublishDirectory) {
    $PublishDirectory = Join-Path $artifacts "publish/$Runtime"
    if ($onWindows) {
        # Native AOT finds Visual Studio's linker through vswhere.exe.
        $vsInstaller = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer'
        if ((Test-Path $vsInstaller) -and -not ($env:PATH -split ';' | Where-Object { $_ -eq $vsInstaller })) { $env:PATH = "$vsInstaller;$env:PATH" }
    }
    Write-Host "Publishing otter for $Runtime ..."
    Invoke-Native dotnet @('publish', (Join-Path $root 'src/Otter.Cli/Otter.Cli.csproj'), '-c', 'Release', '-r', $Runtime,
        '-p:UseAppHost=true', "-p:Version=$Version", '-o', $PublishDirectory, '--nologo', '-v', 'quiet')
}
$binary = Join-Path $PublishDirectory $exe
if (-not (Test-Path -LiteralPath $binary)) { throw "No $exe in $PublishDirectory." }
if ($Runtime -eq $hostRid) {
    $printed = (& $binary --version | Out-String).Trim()
    if ($printed -ne "Otter $languageVersion") { throw "$binary --version printed '$printed', not 'Otter $languageVersion'." }
}

# otter-desktop.exe (desktop windows on Windows), once src/Otter.Desktop exists.
$desktopFiles = @()
$desktopProject = Get-ChildItem -Path (Join-Path $root 'src/Otter.Desktop') -Filter '*.csproj' -ErrorAction SilentlyContinue | Select-Object -First 1
if ($os -eq 'win' -and $desktopProject) {
    $desktopOut = Join-Path $artifacts "publish/$Runtime-desktop"
    Write-Host "Publishing $($desktopProject.Name) for $Runtime ..."
    # As ci.yml and publish.ps1 publish it: framework-dependent, with its app host.
    Invoke-Native dotnet @('publish', $desktopProject.FullName, '-c', 'Release', '-r', $Runtime, '--self-contained', 'false', '-p:UseAppHost=true', "-p:Version=$Version", '-o', $desktopOut, '--nologo', '-v', 'quiet')
    $desktopFiles = @(Get-ChildItem -LiteralPath $desktopOut -File | Where-Object { $_.Extension -ne '.pdb' })
    if (-not ($desktopFiles | Where-Object Name -eq 'otter-desktop.exe')) { throw "$($desktopProject.Name) did not publish otter-desktop.exe." }
}

# ---- 2. The documents ----------------------------------------------------------

# The .NET runtime's notices, as published with the Native AOT compiler this
# build used (project.assets.json names its version).
$assets = Get-Content -Raw (Join-Path $root 'src/Otter.Cli/obj/project.assets.json') | ConvertFrom-Json
$ilc = @($assets.libraries.PSObject.Properties.Name | Where-Object { $_ -like 'Microsoft.DotNet.ILCompiler/*' }) | Select-Object -First 1
if (-not $ilc) { throw 'The Native AOT compiler (Microsoft.DotNet.ILCompiler) is not in project.assets.json; publish first.' }
$ilcNotices = $null
foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
    $candidate = Join-Path $folder (Join-Path $ilc.ToLowerInvariant() 'THIRD-PARTY-NOTICES.TXT')
    if (Test-Path -LiteralPath $candidate) { $ilcNotices = $candidate; break }
}
if (-not $ilcNotices) { throw "THIRD-PARTY-NOTICES.TXT of $ilc not found in the NuGet package folders." }
$notices = [System.IO.File]::ReadAllText((Join-Path $root 'distribution/package/THIRD-PARTY-NOTICES.header.txt')) +
    ([System.IO.File]::ReadAllText($ilcNotices) -replace "`r`n", "`n")

function Add-Documents {
    param([string]$Folder)
    Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination (Join-Path $Folder 'LICENSE')
    Copy-Item -LiteralPath (Join-Path $root 'distribution/package/README.md') -Destination (Join-Path $Folder 'README.md')
    [System.IO.File]::WriteAllText((Join-Path $Folder 'VERSION'), "$Version`n", $utf8)
    [System.IO.File]::WriteAllText((Join-Path $Folder 'THIRD-PARTY-NOTICES.txt'), $notices, $utf8)
}

# ---- 3. The archive ---------------------------------------------------------------

$staged = Join-Path $work "archive/$name"
New-Item -ItemType Directory -Force -Path $staged | Out-Null
$built = @()
if ($os -eq 'win') {
    Copy-Item -LiteralPath $binary -Destination $staged
    foreach ($file in $desktopFiles) {
        if (Test-Path -LiteralPath (Join-Path $staged $file.Name)) { throw "otter-desktop's $($file.Name) would replace otter's." }
        Copy-Item -LiteralPath $file.FullName -Destination $staged
    }
    Add-Documents $staged
    $inside = @(Get-ChildItem -LiteralPath $staged -File -Recurse | ForEach-Object { $_.FullName.Substring($staged.Length + 1) } | Sort-Object)
    Write-Sha256Sums -Folder $staged -Path (Join-Path $staged 'SHA256SUMS') -Files $inside
    $archive = Join-Path $OutputDirectory "$name.zip"
    if (Test-Path $archive) { Remove-Item -Force $archive }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory((Split-Path -Parent $staged), $archive, [System.IO.Compression.CompressionLevel]::Optimal, $false)
} else {
    New-Item -ItemType Directory -Force -Path (Join-Path $staged 'bin') | Out-Null
    Copy-Item -LiteralPath $binary -Destination (Join-Path $staged 'bin/otter')
    Copy-Item -LiteralPath (Join-Path $root 'distribution/unix/install.sh') -Destination $staged
    Copy-Item -LiteralPath (Join-Path $root 'distribution/unix/uninstall.sh') -Destination $staged
    Add-Documents $staged
    Invoke-Native chmod @('755', (Join-Path $staged 'bin/otter'), (Join-Path $staged 'install.sh'), (Join-Path $staged 'uninstall.sh'))
    $inside = @(Get-ChildItem -LiteralPath $staged -File -Recurse | ForEach-Object { $_.FullName.Substring($staged.Length + 1) } | Sort-Object)
    Write-Sha256Sums -Folder $staged -Path (Join-Path $staged 'SHA256SUMS') -Files $inside
    $archive = Join-Path $OutputDirectory "$name.tar.gz"
    # No macOS extended attributes (._ files) and no build machine's user in the archive.
    $env:COPYFILE_DISABLE = '1'
    $owner = if ((& tar --version | Out-String) -match 'GNU tar') { @('--owner=0', '--group=0', '--numeric-owner') } else { @('--uid', '0', '--gid', '0') }
    Invoke-Native tar (@('-c', '-z', '-f', $archive) + $owner + @('-C', (Split-Path -Parent $staged), $name))
}
$built += $archive

# ---- 4. The installer ------------------------------------------------------------

if ($Installer) {
    if ($os -ne $hostOs) { throw "The $Runtime installer is built on $os, not on $hostOs." }
    switch ($os) {
        'win' {
            # One component per file, each kept by its own value under
            # HKCU\Software\otter-net\Files (a per-user install, ICE38), and a
            # RemoveFolder for every folder (ICE64). A file's component GUID
            # comes from its path, so it stays the same in every version.
            $md5 = [System.Security.Cryptography.MD5]::Create()
            $directoryIds = @{ '' = 'INSTALLFOLDER' }
            $directories = [System.Text.StringBuilder]::new()
            $components = [System.Text.StringBuilder]::new()
            function Add-Directory {
                param([string]$Path, [string]$Relative, [string]$Indent)
                foreach ($sub in (Get-ChildItem -LiteralPath $Path -Directory | Sort-Object Name)) {
                    $rel = if ($Relative) { "$Relative\$($sub.Name)" } else { $sub.Name }
                    $id = 'd' + $directoryIds.Count.ToString('000')
                    $directoryIds[$rel] = $id
                    [void]$directories.AppendLine("$Indent<Directory Id=`"$id`" Name=`"$([System.Security.SecurityElement]::Escape($sub.Name))`">")
                    Add-Directory -Path $sub.FullName -Relative $rel -Indent "$Indent  "
                    [void]$directories.AppendLine("$Indent</Directory>")
                }
            }
            Add-Directory -Path $staged -Relative '' -Indent '      '
            $removed = @{ 'INSTALLFOLDER' = $true }   # Package.wxs removes it
            $index = 0
            foreach ($file in (Get-ChildItem -LiteralPath $staged -File -Recurse | Sort-Object FullName)) {
                $index++
                $relative = $file.FullName.Substring($staged.Length + 1)
                $dirId = $directoryIds[(Split-Path -Parent $relative)]
                $guid = [Guid]::new($md5.ComputeHash($utf8.GetBytes("otter-net-msi:$relative".ToLowerInvariant()))).ToString().ToUpperInvariant()
                $escapedRelative = [System.Security.SecurityElement]::Escape($relative)
                [void]$components.AppendLine("      <Component Id=`"c$($index.ToString('000'))`" Directory=`"$dirId`" Guid=`"$guid`">")
                [void]$components.AppendLine("        <File Id=`"f$($index.ToString('000'))`" Source=`"$([System.Security.SecurityElement]::Escape($file.FullName))`" KeyPath=`"no`" />")
                [void]$components.AppendLine("        <RegistryValue Root=`"HKCU`" Key=`"Software\otter-net\Files`" Name=`"$escapedRelative`" Type=`"integer`" Value=`"1`" KeyPath=`"yes`" />")
                if (-not $removed.ContainsKey($dirId)) {
                    [void]$components.AppendLine("        <RemoveFolder Id=`"r$dirId`" Directory=`"$dirId`" On=`"uninstall`" />")
                    $removed[$dirId] = $true
                }
                [void]$components.AppendLine('      </Component>')
            }
            $fragment = Join-Path $work 'OtterNetFiles.wxs'
            [System.IO.File]::WriteAllText($fragment, @"
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
  <Fragment>
    <DirectoryRef Id="INSTALLFOLDER">
$($directories.ToString().TrimEnd())
    </DirectoryRef>
    <ComponentGroup Id="OtterNetFiles">
$($components.ToString().TrimEnd())
    </ComponentGroup>
  </Fragment>
</Wix>
"@, $utf8)
            $msiOut = Join-Path $work 'msi'
            Invoke-Native dotnet @('build', (Join-Path $root 'distribution/windows/otter-net.wixproj'), '-c', 'Release', '--nologo', '-v', 'quiet',
                "-p:InstallerPlatform=$arch", "-p:ProductVersion=$numericVersion", "-p:PackageVersion=$Version",
                "-p:PayloadWxs=$fragment", "-p:OutputPath=$msiOut/", "-p:IntermediateOutputPath=$(Join-Path $work 'msi-obj')/")
            $msi = Join-Path $OutputDirectory "$name.msi"
            Copy-Item -LiteralPath (Join-Path $msiOut 'otter-net.msi') -Destination $msi -Force
            $built += $msi
        }
        'osx' {
            $identifier = 'com.otterprogramming.otter-net'
            $pkgArch = if ($arch -eq 'arm64') { 'arm64' } else { 'x86_64' }
            $fill = { param([string]$Text) $Text.Replace('@VERSION@', $Version).Replace('@ARCH@', $pkgArch).Replace('@IDENTIFIER@', $identifier) }
            # The payload is only otter-net's own folder, /usr/local/lib/otter-net:
            # a package that carries /usr/local/bin itself would take over that
            # folder's owner and permissions (Homebrew's, on Intel Macs).
            # postinstall makes the /usr/local/bin/otter link.
            $payload = Join-Path $work 'pkg/root'
            $lib = Join-Path $payload $Version
            New-Item -ItemType Directory -Force -Path $lib | Out-Null
            [System.IO.File]::WriteAllText((Join-Path $lib '.otter-net-install'), "otter-net installation marker - written by the otter-net .pkg`nVersion: $Version`n", $utf8)
            Copy-Item -LiteralPath $binary -Destination (Join-Path $lib 'otter')
            Add-Documents $lib
            [System.IO.File]::WriteAllText((Join-Path $lib 'uninstall.sh'), (& $fill ([System.IO.File]::ReadAllText((Join-Path $root 'distribution/macos/uninstall.sh')))), $utf8)
            Invoke-Native chmod @('755', (Join-Path $lib 'otter'), (Join-Path $lib 'uninstall.sh'))
            # No extended attributes (they would become ._ files in the payload).
            Invoke-Native xattr @('-cr', $payload)
            $scripts = Join-Path $work 'pkg/scripts'
            New-Item -ItemType Directory -Force -Path $scripts | Out-Null
            foreach ($script in 'preinstall', 'postinstall') {
                [System.IO.File]::WriteAllText((Join-Path $scripts $script), (& $fill ([System.IO.File]::ReadAllText((Join-Path $root "distribution/macos/scripts/$script")))), $utf8)
                Invoke-Native chmod @('755', (Join-Path $scripts $script))
            }
            $resources = Join-Path $work 'pkg/resources'
            New-Item -ItemType Directory -Force -Path $resources | Out-Null
            Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination (Join-Path $resources 'LICENSE.txt')
            [System.IO.File]::WriteAllText((Join-Path $resources 'README.html'), (& $fill ([System.IO.File]::ReadAllText((Join-Path $root 'distribution/macos/README.html')))), $utf8)
            $distribution = Join-Path $work 'pkg/distribution.xml'
            [System.IO.File]::WriteAllText($distribution, (& $fill ([System.IO.File]::ReadAllText((Join-Path $root 'distribution/macos/distribution.xml')))), $utf8)
            $components = Join-Path $work 'pkg/components'
            New-Item -ItemType Directory -Force -Path $components | Out-Null
            $env:COPYFILE_DISABLE = '1'
            Invoke-Native pkgbuild @('--root', $payload, '--identifier', $identifier, '--version', $Version, '--scripts', $scripts,
                '--install-location', '/usr/local/lib/otter-net', '--ownership', 'recommended', '--quiet', (Join-Path $components 'otter-net-component.pkg'))
            $pkg = Join-Path $OutputDirectory "$name.pkg"
            Invoke-Native productbuild @('--distribution', $distribution, '--resources', $resources, '--package-path', $components, '--quiet', $pkg)
            # macOS adds com.apple.provenance to files written by some sandboxed
            # processes; it cannot be removed and pkgbuild stores it as empty
            # ._ entries. Harmless, but not in a clean (CI) build.
            if ((& pkgutil --payload-files $pkg) -match '/\._') { Write-Warning "$pkg has ._ (extended attribute) entries: build it where files carry no com.apple.provenance, as CI does." }
            $built += $pkg
        }
        'linux' {
            $debArch = if ($arch -eq 'x64') { 'amd64' } else { 'arm64' }
            # 1.0.1-rc.1 -> 1.0.1~rc.1, which Debian sorts before 1.0.1.
            $debVersion = $Version -replace '^(\d+\.\d+\.\d+)-', '$1~'
            $deb = Join-Path $work 'deb/root'
            $lib = Join-Path $deb 'usr/lib/otter-net'
            $doc = Join-Path $deb 'usr/share/doc/otter-net'
            New-Item -ItemType Directory -Force -Path $lib, $doc, (Join-Path $deb 'usr/bin'), (Join-Path $deb 'DEBIAN') | Out-Null
            Copy-Item -LiteralPath $binary -Destination (Join-Path $lib 'otter')
            [System.IO.File]::WriteAllText((Join-Path $lib 'VERSION'), "$Version`n", $utf8)
            Invoke-Native ln @('-s', '../lib/otter-net/otter', (Join-Path $deb 'usr/bin/otter'))
            Copy-Item -LiteralPath (Join-Path $root 'distribution/package/README.md') -Destination (Join-Path $doc 'README.md')
            [System.IO.File]::WriteAllText((Join-Path $doc 'THIRD-PARTY-NOTICES.txt'), $notices, $utf8)
            [System.IO.File]::WriteAllText((Join-Path $doc 'copyright'),
                "Format: https://www.debian.org/doc/packaging-manuals/copyright-format/1.0/`nUpstream-Name: otter-net`nSource: https://github.com/Heavens-Lava/otter-net`n`nFiles: *`nCopyright: 2026 Jeffrey Macy`nLicense: Otter-Free-Use-1.1`n See the license text below, and THIRD-PARTY-NOTICES.txt for the .NET runtime it includes.`n`n" +
                ([System.IO.File]::ReadAllText((Join-Path $root 'LICENSE')) -replace "`r`n", "`n"), $utf8)
            # The C library version the executable needs (the newest GLIBC_
            # symbol version it uses) and the libraries it links; OpenSSL is
            # loaded at run time (HTTPS, TLS, hashing), so it is named too.
            $glibc = (& objdump -T $binary | Select-String -Pattern 'GLIBC_([0-9.]+)' -AllMatches | ForEach-Object { $_.Matches } | ForEach-Object { $_.Groups[1].Value } |
                Sort-Object { [version]$_ } | Select-Object -Last 1)
            if (-not $glibc) { throw "objdump found no GLIBC versions in $binary." }
            $needed = @(& objdump -p $binary | Select-String -Pattern '^\s*NEEDED\s+(\S+)' | ForEach-Object { $_.Matches[0].Groups[1].Value })
            $depends = [System.Collections.Generic.List[string]]::new()
            $depends.Add("libc6 (>= $glibc)")
            foreach ($library in $needed) {
                switch -Wildcard ($library) {
                    'libc.so*' { } 'libm.so*' { } 'libdl.so*' { } 'libpthread.so*' { } 'librt.so*' { } 'ld-linux*' { }
                    'libgcc_s.so*' { $depends.Add('libgcc-s1') }
                    'libstdc++.so*' { $depends.Add('libstdc++6') }
                    'libz.so*' { $depends.Add('zlib1g') }
                    default { throw "The executable links $library; say which Debian package provides it in tools/package.ps1." }
                }
            }
            $depends.Add('libssl3 | libssl3t64')
            Get-ChildItem -LiteralPath $deb -Recurse -Directory | ForEach-Object { Invoke-Native chmod @('755', $_.FullName) }
            Get-ChildItem -LiteralPath $deb -Recurse -File | ForEach-Object { Invoke-Native chmod @('644', $_.FullName) }
            Invoke-Native chmod @('755', (Join-Path $lib 'otter'))
            $installedKb = [int][Math]::Ceiling(((Get-ChildItem -LiteralPath $deb -Recurse -File | Where-Object { $_.FullName -notlike '*DEBIAN*' } | Measure-Object Length -Sum).Sum) / 1KB)
            $control = [System.IO.File]::ReadAllText((Join-Path $root 'distribution/linux/control')).Replace('@VERSION@', $debVersion).Replace('@ARCH@', $debArch).
                Replace('@INSTALLED_SIZE@', "$installedKb").Replace('@DEPENDS@', ($depends -join ', ')) -replace "`r`n", "`n"
            [System.IO.File]::WriteAllText((Join-Path $deb 'DEBIAN/control'), $control, $utf8)
            $debFile = Join-Path $OutputDirectory "otter-net_${debVersion}_$debArch.deb"
            Invoke-Native dpkg-deb @('--root-owner-group', '-Zxz', '--build', $deb, $debFile)
            Write-Host "Depends: $($depends -join ', ')"
            $built += $debFile
        }
    }
}

# ---- 5. Checksums --------------------------------------------------------------

foreach ($file in $built) {
    $hash = Write-Sha256 $file
    "{0}  {1:N0} bytes  sha256 {2}" -f $file, (Get-Item -LiteralPath $file).Length, $hash
}
$packages = @(Get-ChildItem -LiteralPath $OutputDirectory -File | Where-Object { $_.Name -match '\.(zip|tar\.gz|msi|pkg|deb)$' } | Sort-Object Name | ForEach-Object Name)
Write-Sha256Sums -Folder $OutputDirectory -Path (Join-Path $OutputDirectory 'SHA256SUMS') -Files $packages
