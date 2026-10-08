param([string]$CodexExecutable, [string]$DataDirectory)
. (Join-Path $PSScriptRoot 'common.ps1')
$process = $null
$startupResult = $null
try {
    $executable = Get-PetFolioExecutable
    $paths = @(Get-PetFolioPaths $CodexExecutable $DataDirectory)
    $environment = Invoke-PetFolioControl $executable (@('--check') + $paths)
    if (-not $environment.ok) { throw $environment.error }
    $startupResult = [IO.Path]::GetTempFileName()
    $launchArguments = (@(@('--background', '--startup-result', $startupResult) + $paths | ForEach-Object { ConvertTo-PetFolioArgument $_ }) -join ' ')
    # ShellExecute detaches the GUI process. Redirected/ inherited pipes can
    # otherwise keep a calling Codex tool or PowerShell session alive indefinitely.
    $process = Start-Process -FilePath $executable -ArgumentList $launchArguments -WorkingDirectory (Split-Path -Parent $executable) -WindowStyle Hidden -PassThru
    $timer = [Diagnostics.Stopwatch]::StartNew()
    do {
        if ($process.HasExited -and $process.ExitCode -ne 0) {
            $message = [IO.File]::ReadAllText($startupResult).Trim()
            if ($message) { throw (($message | ConvertFrom-Json).error) }
            throw ('PetFolio startup failed with exit code ' + $process.ExitCode + '.')
        }
        $result = Invoke-PetFolioControl $executable @('--status')
        if ($result.running -and $result.ready) {
            Write-PetFolioResult $result
            return
        }
        Start-Sleep -Milliseconds 100
    } while ($timer.Elapsed.TotalSeconds -lt 15)
    throw 'PetFolio did not become ready. Close any older PetFolio instance and retry; check its local diagnostics if it is still starting.'
} catch {
    Write-PetFolioResult @{ ok = $false; error = $_.Exception.Message }
    exit 1
} finally {
    if ($process) { $process.Dispose() }
    if ($startupResult -and (Test-Path -LiteralPath $startupResult)) { Remove-Item -LiteralPath $startupResult -Force }
}
