param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist'))
$ErrorActionPreference = 'Stop'
$version = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'VERSION') -Raw).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'VERSION must contain a three-part version.' }
$source = Join-Path $PSScriptRoot 'plugins\petfolio'
$portableManifest = Get-Content -LiteralPath (Join-Path $source 'plugin.json') -Raw | ConvertFrom-Json
$compatibilityManifest = Get-Content -LiteralPath (Join-Path $source '.codex-plugin\plugin.json') -Raw | ConvertFrom-Json
if ($portableManifest.version -ne $version -or $compatibilityManifest.version -ne $version) { throw 'VERSION and both plugin manifest versions must match.' }
$program = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Program.cs') -Raw
if (-not $program.Contains('AssemblyVersion("' + $version + '.0")') -or -not $program.Contains('AssemblyFileVersion("' + $version + '.0")')) {
    throw 'VERSION and assembly versions must match.'
}
$appManifest = [xml](Get-Content -LiteralPath (Join-Path $PSScriptRoot 'app.manifest') -Raw)
if ($appManifest.assembly.assemblyIdentity.version -ne ($version + '.0')) { throw 'VERSION and application manifest version must match.' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$stage = Join-Path $OutputDirectory ('stage-plugin-' + [guid]::NewGuid().ToString('N'))
$payload = Join-Path $stage 'petfolio'
$binaryDirectory = Join-Path $payload 'bin\win-x64'
New-Item -ItemType Directory -Path $binaryDirectory -Force | Out-Null
# Explicit payload list excludes machine-specific state and any source-tree binaries.
$files = @('plugin.json', '.codex-plugin\plugin.json', 'skills\petfolio\SKILL.md',
    'scripts\common.ps1', 'scripts\start.ps1', 'scripts\stop.ps1', 'scripts\status.ps1', 'README.md', 'assets\icon.png')
foreach ($relative in $files) {
    $target = Join-Path $payload $relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $source $relative) -Destination $target
}
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $binaryDirectory
New-Item -ItemType Directory -Path (Join-Path $payload 'assets') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'assets\PetFolio.ico') -Destination (Join-Path $payload 'assets')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'VERSION') -Destination $payload
$zip = Join-Path $OutputDirectory "PetFolio-$version-plugin-win-x64.zip"
# Use .NET ZIP APIs so the hidden compatibility manifest is included reliably.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$temporaryZip = $stage + '.zip'
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $temporaryZip, [IO.Compression.CompressionLevel]::Optimal, $false)
Move-Item -LiteralPath $temporaryZip -Destination $zip -Force
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath ($zip + '.sha256') -Value ($hash + '  ' + [IO.Path]::GetFileName($zip)) -Encoding ascii

# A standalone marketplace root works without writing project or user configuration.
$marketplaceRoot = Join-Path $OutputDirectory 'petfolio-marketplace'
$marketplacePlugin = Join-Path $marketplaceRoot 'plugins\petfolio'
New-Item -ItemType Directory -Path $marketplacePlugin -Force | Out-Null
Get-ChildItem -LiteralPath $payload -Force | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $marketplacePlugin -Recurse -Force }
$catalogDirectory = Join-Path $marketplaceRoot '.agents\plugins'
New-Item -ItemType Directory -Path $catalogDirectory -Force | Out-Null
$catalog = [ordered]@{
    name = 'petfolio-local'
    interface = @{ displayName = 'PetFolio Local' }
    plugins = @(@{
        name = 'petfolio'
        source = @{ source = 'local'; path = './plugins/petfolio' }
        policy = @{ installation = 'AVAILABLE'; authentication = 'ON_INSTALL' }
        category = 'Productivity'
    })
}
[IO.File]::WriteAllText((Join-Path $catalogDirectory 'marketplace.json'), ($catalog | ConvertTo-Json -Depth 6), (New-Object Text.UTF8Encoding($false)))
Write-Output $zip
Write-Output $marketplaceRoot
