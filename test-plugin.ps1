param(
    [string]$MarketplaceDirectory = (Join-Path $PSScriptRoot 'dist\petfolio-marketplace'),
    [switch]$SkipCodexInstall,
    [switch]$TestLaunch
)
$ErrorActionPreference = 'Stop'
$marketplaceRoot = (Resolve-Path -LiteralPath $MarketplaceDirectory).Path
$pluginRoot = Join-Path $marketplaceRoot 'plugins\petfolio'
. (Join-Path $pluginRoot 'scripts\common.ps1')
$testRoot = Join-Path $PSScriptRoot ('.test-build\plugin-' + [guid]::NewGuid().ToString('N'))
$pluginTestHome = Join-Path $testRoot 'codex home'
$runtimeDirectory = Join-Path $testRoot 'user data'
New-Item -ItemType Directory -Path $pluginTestHome -Force | Out-Null
$script:checks = 0
$originalDataDirectory = $env:PETFOLIO_DATA_DIR
$originalInstanceId = $env:PETFOLIO_INSTANCE_ID
$env:PETFOLIO_DATA_DIR = $runtimeDirectory
$env:PETFOLIO_INSTANCE_ID = 'test-' + [guid]::NewGuid().ToString('N')
$ownsLaunch = $false

function Assert-Plugin([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    $script:checks++
}

function Invoke-TestProcess([string]$Executable, [string[]]$CommandArguments, [bool]$IsCodex = $false) {
    $process = New-PetFolioProcess $Executable $CommandArguments
    $process.StartInfo.WorkingDirectory = $testRoot
    if ($IsCodex) { $process.StartInfo.EnvironmentVariables['CODEX_HOME'] = $pluginTestHome }
    try {
        [void]$process.Start()
        $output = $process.StandardOutput.ReadToEndAsync()
        $errors = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(30000)) { $process.Kill(); throw 'Plugin test subprocess timed out.' }
        return [pscustomobject]@{ Code = $process.ExitCode; Text = $output.Result.Trim(); Error = $errors.Result.Trim() }
    } finally { $process.Dispose() }
}

function Invoke-TestScript([string]$Name, [string[]]$ScriptArguments = @(), [string]$Root = $pluginRoot) {
    $shell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $response = Invoke-TestProcess $shell (@('-NoProfile', '-File', (Join-Path $Root ('scripts\' + $Name))) + $ScriptArguments)
    if (-not $response.Text) { throw ('Script returned no JSON: ' + $response.Error) }
    return [pscustomobject]@{ Code = $response.Code; Result = ($response.Text | ConvertFrom-Json) }
}

function Invoke-TestCodex([string[]]$CommandArguments) {
    $response = Invoke-TestProcess (Get-Command codex -ErrorAction Stop).Source $CommandArguments $true
    if ($response.Code -ne 0) { throw ('Isolated Codex test failed: ' + $response.Error + ' ' + $response.Text) }
    return ($response.Text | ConvertFrom-Json)
}

try {
    $version = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'VERSION') -Raw).Trim()
    $manifest = Get-Content -LiteralPath (Join-Path $pluginRoot 'plugin.json') -Raw | ConvertFrom-Json
    $compatibility = Get-Content -LiteralPath (Join-Path $pluginRoot '.codex-plugin\plugin.json') -Raw | ConvertFrom-Json
    $catalog = Get-Content -LiteralPath (Join-Path $marketplaceRoot '.agents\plugins\marketplace.json') -Raw | ConvertFrom-Json
    Assert-Plugin ($manifest.name -eq 'petfolio' -and $manifest.version -eq $version -and $compatibility.version -eq $version) 'Plugin identities and versions must match.'
    Assert-Plugin ($catalog.plugins[0].source.path -eq './plugins/petfolio') 'Marketplace paths must resolve from the marketplace root.'
    $setup = Join-Path $pluginRoot $manifest.extensions.'com.openai'.onboardingSkill
    Assert-Plugin (Test-Path -LiteralPath $setup -PathType Leaf) 'Onboarding must point to a bundled skill.'
    Assert-Plugin (([Diagnostics.FileVersionInfo]::GetVersionInfo((Get-PetFolioExecutable)).FileVersion) -eq ($version + '.0')) 'Executable and plugin versions must match.'

    $zipPath = Join-Path (Split-Path -Parent $marketplaceRoot) "PetFolio-$version-plugin-win-x64.zip"
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        $entries = @($archive.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
        Assert-Plugin ($entries -contains 'petfolio/plugin.json' -and $entries -contains 'petfolio/.codex-plugin/plugin.json') 'ZIP must include both manifests under one top-level plugin folder.'
        Assert-Plugin ($entries -contains 'petfolio/bin/win-x64/PetFolio.exe') 'ZIP must include the prebuilt Windows executable.'
        $unexpected = @($entries | Where-Object { $_ -notlike 'petfolio/*' -or $_ -match '(appearance\.json|pet-palettes\.json|quota-snapshot\.json|probe-events\.jsonl|\.cs|\.py)$' })
        Assert-Plugin ($unexpected.Count -eq 0) 'ZIP must exclude local records and development sources.'
    } finally { $archive.Dispose() }
    $expectedHash = ((Get-Content -LiteralPath ($zipPath + '.sha256') -Raw).Trim() -split '\s+')[0]
    Assert-Plugin ((Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant() -eq $expectedHash) 'ZIP checksum must match.'

    # Execute scripts through Windows PowerShell 5.1 and an unrelated working directory.
    $status = Invoke-TestScript 'status.ps1'
    Assert-Plugin ($status.Code -eq 0 -and $status.Result.ok -and $status.Result.dataDirectory -eq $runtimeDirectory) 'Status must be readable JSON and honor the data override.'
    Assert-Plugin (-not (Test-Path -LiteralPath $runtimeDirectory)) 'Status must not create or migrate user data.'
    $fakeHome = Join-Path $testRoot '桌面 data'
    New-Item -ItemType Directory -Path $fakeHome -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $fakeHome '.codex-global-state.json'), '{}')
    $fakeCli = Join-Path $testRoot '假 CLI.exe'
    $compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    & $compiler /nologo /target:exe /platform:x64 /reference:System.Web.Extensions.dll "/out:$fakeCli" (Join-Path $PSScriptRoot 'FakeQuotaServer.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Fake CLI build failed.' }
    [IO.File]::WriteAllText((Join-Path $testRoot 'fake-quota-mode.txt'), 'success')
    $paths = @('-CodexExecutable', $fakeCli, '-DataDirectory', $fakeHome)
    $check = Invoke-TestScript 'status.ps1' (@('-CheckEnvironment') + $paths)
    Assert-Plugin ($check.Code -eq 0 -and $check.Result.ok -and $check.Result.environment.codexExecutable -eq $fakeCli) 'Environment checks must preserve spaces and Unicode paths.'
    Assert-Plugin ($check.Result.environment.authentication -eq 'not-checked') 'Environment check must not imply login or quota verification.'
    Assert-Plugin (-not (Test-Path -LiteralPath $runtimeDirectory)) 'Environment checks must not write user state.'
    $invalid = Invoke-TestScript 'status.ps1' @('-CheckEnvironment', '-CodexExecutable', $fakeCli)
    Assert-Plugin ($invalid.Code -ne 0 -and -not $invalid.Result.ok) 'Incomplete path pairs must return a structured failure.'
    $missing = Invoke-TestScript 'start.ps1' @('-CodexExecutable', (Join-Path $testRoot 'missing.exe'), '-DataDirectory', $fakeHome)
    Assert-Plugin ($missing.Code -ne 0 -and -not $missing.Result.ok -and $missing.Result.error -match 'Codex') 'Missing CLI must fail without showing an error dialog.'

    # A copy in an unrelated path simulates the installed plugin cache.
    $cacheCopy = Join-Path $testRoot 'installed plugin copy'
    Copy-Item -LiteralPath $pluginRoot -Destination $cacheCopy -Recurse
    $copied = Invoke-TestScript 'status.ps1' @() $cacheCopy
    Assert-Plugin ($copied.Code -eq 0 -and $copied.Result.ok) 'Scripts must work after plugin cache relocation.'
    Write-Output 'Payload, environment checks, and relocated scripts passed.'

    if ($TestLaunch) {
        Assert-Plugin (-not $status.Result.running) 'The isolated test instance must initially be stopped.'
        $blockedDirectory = Join-Path $testRoot 'blocked data directory'
        [IO.File]::WriteAllText($blockedDirectory, 'this is a file')
        $env:PETFOLIO_DATA_DIR = $blockedDirectory
        try {
            $failedStart = Invoke-TestScript 'start.ps1' $paths $cacheCopy
            Assert-Plugin ($failedStart.Code -ne 0 -and -not $failedStart.Result.ok -and $failedStart.Result.error) 'Startup failures must return JSON through the detached launcher.'
        } finally { $env:PETFOLIO_DATA_DIR = $runtimeDirectory }
        New-Item -ItemType Directory -Path $runtimeDirectory -Force | Out-Null
        $preferences = '{"opacityPercent":60,"panelCorner":"bottom-start"}'
        [IO.File]::WriteAllText((Join-Path $runtimeDirectory 'appearance.json'), $preferences)
        $ownsLaunch = $true
        $started = Invoke-TestScript 'start.ps1' $paths $cacheCopy
        Assert-Plugin ($started.Code -eq 0 -and $started.Result.running -and $started.Result.ready) 'Start must confirm host readiness.'
        $testExecutable = Join-Path $cacheCopy 'bin\win-x64\PetFolio.exe'
        $firstIds = @(Get-Process PetFolio -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $testExecutable } | ForEach-Object { $_.Id })
        $second = Invoke-TestScript 'start.ps1' $paths $cacheCopy
        $secondIds = @(Get-Process PetFolio -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $testExecutable } | ForEach-Object { $_.Id })
        Assert-Plugin ($second.Code -eq 0 -and $second.Result.ready -and $firstIds.Count -eq 1 -and $secondIds.Count -eq 1 -and $firstIds[0] -eq $secondIds[0]) 'Repeated start must restore the same process.'
        $snapshot = Join-Path $runtimeDirectory 'quota-snapshot.json'
        $timer = [Diagnostics.Stopwatch]::StartNew()
        while (-not (Test-Path -LiteralPath $snapshot) -and $timer.Elapsed.TotalSeconds -lt 5) { Start-Sleep -Milliseconds 100 }
        Assert-Plugin (Test-Path -LiteralPath $snapshot) 'Fake quota query must write its snapshot to user data.'
        Assert-Plugin ((Get-Content -LiteralPath $snapshot -Raw | ConvertFrom-Json).rateLimits.primary.usedPercent -eq 17) 'The snapshot must come from the fake CLI.'
        Assert-Plugin (-not (Test-Path -LiteralPath (Join-Path $cacheCopy 'bin\win-x64\quota-snapshot.json'))) 'The plugin executable directory must remain free of runtime state.'
        $stopped = Invoke-TestScript 'stop.ps1' @() $cacheCopy
        Assert-Plugin ($stopped.Code -eq 0 -and -not $stopped.Result.running) 'Stop must await graceful shutdown.'
        $ownsLaunch = $false
        $stoppedAgain = Invoke-TestScript 'stop.ps1' @() $cacheCopy
        Assert-Plugin ($stoppedAgain.Code -eq 0 -and -not $stoppedAgain.Result.running) 'Repeated stop must be harmless.'
        Assert-Plugin ([IO.File]::ReadAllText((Join-Path $runtimeDirectory 'appearance.json')) -eq $preferences) 'Restart and stop must preserve existing preferences.'
        Write-Output 'Fake-CLI launch, repeated start, and graceful stop passed.'
    }

    if (-not $SkipCodexInstall) {
        Write-Output 'Verifying installation with isolated CODEX_HOME...'
        $added = Invoke-TestCodex @('plugin', 'marketplace', 'add', $marketplaceRoot, '--json')
        Assert-Plugin ($added.marketplaceName -eq 'petfolio-local') 'Codex must register the local marketplace.'
        $installed = Invoke-TestCodex @('plugin', 'add', 'petfolio@petfolio-local', '--json')
        Assert-Plugin ($null -ne $installed) 'Codex must install the plugin.'
        $cachedManifest = @(Get-ChildItem -LiteralPath (Join-Path $pluginTestHome 'plugins\cache\petfolio-local\petfolio') -Filter plugin.json -Recurse -Force)
        Assert-Plugin ($cachedManifest.Count -gt 0) 'Installation must produce a cached plugin manifest.'
        $installedRoot = Split-Path -Parent ($cachedManifest | Where-Object { $_.Directory.Name -ne '.codex-plugin' } | Select-Object -First 1).FullName
        $cachedStatus = Invoke-TestScript 'status.ps1' @() $installedRoot
        Assert-Plugin ($cachedStatus.Code -eq 0 -and $cachedStatus.Result.ok) 'The Codex-installed copy must have a working status script.'
        New-Item -ItemType Directory -Path $runtimeDirectory -Force | Out-Null
        [IO.File]::WriteAllText((Join-Path $runtimeDirectory 'keep.txt'), 'user data')
        [void](Invoke-TestCodex @('plugin', 'remove', 'petfolio@petfolio-local', '--json'))
        Assert-Plugin (Test-Path -LiteralPath (Join-Path $runtimeDirectory 'keep.txt')) 'Plugin uninstall must preserve user data.'
        [void](Invoke-TestCodex @('plugin', 'marketplace', 'remove', 'petfolio-local', '--json'))
    }
    Write-Output ("PASS $script:checks plugin checks")
    Write-Output ('Isolated verification artifacts: ' + $testRoot)
} finally {
    if ($ownsLaunch) { [void](Invoke-TestScript 'stop.ps1' @() $cacheCopy) }
    $env:PETFOLIO_DATA_DIR = $originalDataDirectory
    $env:PETFOLIO_INSTANCE_ID = $originalInstanceId
}
