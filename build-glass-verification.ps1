param([string]$OutputDirectory = $PSScriptRoot)
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $OutputDirectory -OutputName 'GlassVerification.exe' -EntryPoint 'GlassVerification' -Target 'exe' -AdditionalSources 'GlassVerification.cs'
