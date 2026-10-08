param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist'))
$ErrorActionPreference = 'Stop'
$version = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'VERSION') -Raw).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'VERSION must contain a three-part version.' }
$program = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Program.cs') -Raw
if (-not $program.Contains('AssemblyFileVersion("' + $version + '.0")')) { throw 'VERSION and AssemblyFileVersion must match.' }
if (-not $program.Contains('AssemblyVersion("' + $version + '.0")')) { throw 'VERSION and AssemblyVersion must match.' }
$manifest = [xml](Get-Content -LiteralPath (Join-Path $PSScriptRoot 'app.manifest') -Raw)
if ($manifest.assembly.assemblyIdentity.version -ne ($version + '.0')) { throw 'VERSION and manifest version must match.' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
# Always build into a fresh directory; no local runtime files are packaged.
$stage = Join-Path $OutputDirectory ('stage-' + [guid]::NewGuid().ToString('N'))
$payload = Join-Path $stage 'PetFolio'
New-Item -ItemType Directory -Path $payload -Force | Out-Null
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $payload
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination $payload
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'PLUGIN.md') -Destination $payload
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'create-desktop-shortcut.ps1') -Destination $payload
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'VERSION') -Destination $payload
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'assets\PetFolio.ico') -Destination $payload
$zip = Join-Path $OutputDirectory "PetFolio-$version-win-x64.zip"
Compress-Archive -LiteralPath $payload -DestinationPath $zip -Force
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath ($zip + '.sha256') -Value ($hash + '  ' + [IO.Path]::GetFileName($zip)) -Encoding ascii
Write-Output $zip
