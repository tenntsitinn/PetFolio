param([string]$OutputDirectory = $PSScriptRoot, [string]$OutputName = 'PetFolio.exe', [string]$EntryPoint = 'Program', [string]$Target = 'winexe', [string[]]$AdditionalSources = @())
$ErrorActionPreference = 'Stop'
$probeRoot = $PSScriptRoot
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$frameworkRoot = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$gacRoot = 'C:\Windows\Microsoft.NET\assembly\GAC_MSIL'
if (-not (Test-Path -LiteralPath $OutputDirectory)) { New-Item -ItemType Directory -Path $OutputDirectory | Out-Null }
$sourceNames = @(
    'Program.cs',
    'StartupConfiguration.cs',
    'RuntimeData.cs',
    'CompanionSignals.cs',
    'CodexAutoStart.cs',
    'CompanionApplication.cs',
    'ICompanionFeature.cs',
    'QuotaFeature.cs',
    'QuotaLabel.cs',
    'QuotaData.cs',
    'QuotaService.cs',
    'CodexQuotaSource.cs',
    'PetStateService.cs',
    'Native.cs',
    'PetTheme.cs',
    'PetColourSwitch.cs',
    'PetDragFollower.cs',
    'PetPanelPlacement.cs',
    'GlassBackdrop.cs',
    'ShadowBackdrop.cs',
    'CloseBubbleButton.cs',
    'BackdropGaussian.cs'
) + $AdditionalSources
$sources = @($sourceNames | ForEach-Object { Join-Path $PSScriptRoot $_ })
$outputPath = Join-Path $OutputDirectory $OutputName
$iconPath = Join-Path $PSScriptRoot 'assets\PetFolio.ico'
if (-not (Test-Path -LiteralPath $iconPath)) { throw 'Project icon is missing: assets\PetFolio.ico' }
$sources = @("/win32icon:$iconPath", "/resource:$iconPath,PetFolio.Icon") + $sources
& $compiler /nologo "/target:$Target" "/main:$EntryPoint" /platform:x64 "/out:$outputPath" "/win32manifest:$probeRoot\app.manifest" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:System.Numerics.dll /reference:C:\Windows\System32\WinMetadata\Windows.UI.winmd /reference:C:\Windows\System32\WinMetadata\Windows.Foundation.winmd /reference:C:\Windows\System32\WinMetadata\Windows.Graphics.winmd "/reference:$frameworkRoot\System.Runtime.WindowsRuntime.dll" "/reference:$gacRoot\System.Runtime\v4.0_4.0.0.0__b03f5f7f11d50a3a\System.Runtime.dll" "/reference:$gacRoot\System.Numerics.Vectors\v4.0_4.0.0.0__b03f5f7f11d50a3a\System.Numerics.Vectors.dll" @sources
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
