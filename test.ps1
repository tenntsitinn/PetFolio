param([switch]$CheckLocalPalettes)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$testRoot = Join-Path $projectRoot '.test-build'
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$createdDirectory = -not (Test-Path -LiteralPath $testRoot)
if ($createdDirectory) { New-Item -ItemType Directory -Path $testRoot | Out-Null }
$generated = @()
$originalDataDirectory = $env:PETFOLIO_DATA_DIR
$localDataDirectory = if ($originalDataDirectory) { $originalDataDirectory } else { Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'PetFolio' }
$env:PETFOLIO_DATA_DIR = Join-Path $testRoot 'runtime-data'
$suites = @(
    @{ Name = 'StartupTests'; Sources = @('StartupTests.cs', 'StartupConfiguration.cs') },
    @{ Name = 'RuntimeDataTests'; Sources = @('RuntimeDataTests.cs', 'RuntimeData.cs') },
    @{ Name = 'CompanionSignalsTests'; Sources = @('CompanionSignalsTests.cs', 'CompanionSignals.cs') },
    @{ Name = 'CodexAutoStartTests'; Sources = @('CodexAutoStartTests.cs', 'CodexAutoStart.cs', 'CompanionSignals.cs') },
    @{ Name = 'ThemeTests'; Sources = @('ThemeTests.cs', 'PetTheme.cs') },
    @{ Name = 'BubbleBackdropTests'; Sources = @('BubbleBackdropTests.cs', 'BubbleBackdrop.cs', 'PetTheme.cs') },
    @{ Name = 'BubbleCaptureTests'; Sources = @('BubbleCaptureTests.cs', 'BubbleBackdrop.cs', 'PetTheme.cs') },
    @{ Name = 'PetSwitchTests'; Sources = @('PetSwitchTests.cs', 'PetColourSwitch.cs', 'PetTheme.cs') },
    @{ Name = 'PetDragTests'; Sources = @('PetDragTests.cs', 'PetDragFollower.cs') },
    @{ Name = 'PetPanelTests'; Sources = @('PetPanelTests.cs', 'PetPanelPlacement.cs') },
    @{ Name = 'QuotaTests'; Sources = @('QuotaTests.cs', 'QuotaData.cs', 'QuotaService.cs', 'CodexQuotaSource.cs') }
)
try {
    $fakeServer = Join-Path $testRoot 'FakeQuotaServer.exe'
    $generated += $fakeServer
    & $compiler /nologo /target:exe /platform:x64 /reference:System.Web.Extensions.dll "/out:$fakeServer" (Join-Path $projectRoot 'FakeQuotaServer.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Build failed: FakeQuotaServer' }
    foreach ($suite in $suites) {
        $executable = Join-Path $testRoot ($suite.Name + '.exe')
        $generated += $executable
        $sourcePaths = @($suite.Sources | ForEach-Object { Join-Path $projectRoot $_ })
        & $compiler /nologo /target:exe /platform:x64 /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll "/out:$executable" @sourcePaths
        if ($LASTEXITCODE -ne 0) { throw ('Build failed: ' + $suite.Name) }
        $testArguments = @()
        if ($suite.Name -eq 'QuotaTests') { $testArguments += $fakeServer }
        $palettePath = Join-Path $localDataDirectory 'pet-palettes.json'
        if ($CheckLocalPalettes -and $suite.Name -eq 'PetSwitchTests' -and (Test-Path -LiteralPath $palettePath)) {
            $testArguments += $palettePath
        }
        $result = @(& $executable @testArguments 2>&1)
        if ($LASTEXITCODE -ne 0) { throw ($result -join [Environment]::NewLine) }
        Write-Output ('{0}: {1}' -f $suite.Name, $result[-1])
    }
    foreach ($suiteName in @('FeatureLifecycleTests', 'BubbleClickTests')) {
        $lifecycleExecutable = Join-Path $testRoot ($suiteName + '.exe')
        $generated += $lifecycleExecutable
        & (Join-Path $projectRoot 'build.ps1') -OutputDirectory $testRoot -OutputName ($suiteName + '.exe') -EntryPoint $suiteName -Target 'exe' -AdditionalSources ($suiteName + '.cs')
        $result = @(& $lifecycleExecutable 2>&1)
        if ($LASTEXITCODE -ne 0) { throw ($result -join [Environment]::NewLine) }
        Write-Output ($suiteName + ': ' + $result[-1])
    }
} finally {
    $env:PETFOLIO_DATA_DIR = $originalDataDirectory
    foreach ($file in $generated) {
        if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file -Force }
    }
    if ($createdDirectory -and @(Get-ChildItem -LiteralPath $testRoot -Force).Count -eq 0) {
        Remove-Item -LiteralPath $testRoot
    }
}
