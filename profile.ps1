param([ValidateRange(2,30)][double]$Seconds = 8, [string]$OutputFile = '.test-build\performance.json')
$ErrorActionPreference = 'Stop'
$outputPath = if ([IO.Path]::IsPathRooted($OutputFile)) { [IO.Path]::GetFullPath($OutputFile) } else { [IO.Path]::GetFullPath((Join-Path $PSScriptRoot $OutputFile)) }
$buildDirectory = Join-Path $PSScriptRoot '.test-build\performance'
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $buildDirectory -OutputName 'PerformanceProbe.exe' -EntryPoint 'PerformanceProbe' -Target 'exe' -AdditionalSources 'PerformanceProbe.cs'
& (Join-Path $buildDirectory 'PerformanceProbe.exe') $outputPath $Seconds.ToString([Globalization.CultureInfo]::InvariantCulture)
if ($LASTEXITCODE -ne 0) { throw 'Performance probe failed.' }
Write-Output "Performance report: $outputPath"
