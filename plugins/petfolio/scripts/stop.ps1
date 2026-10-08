param()
. (Join-Path $PSScriptRoot 'common.ps1')
try {
    $executable = Get-PetFolioExecutable
    [void](Invoke-PetFolioControl $executable @('--stop'))
    $timer = [Diagnostics.Stopwatch]::StartNew()
    do {
        $result = Invoke-PetFolioControl $executable @('--status')
        if (-not $result.running) {
            Write-PetFolioResult $result
            return
        }
        Start-Sleep -Milliseconds 100
    } while ($timer.Elapsed.TotalSeconds -lt 15)
    throw 'PetFolio did not stop within 15 seconds. Use Exit in its system tray and retry.'
} catch {
    Write-PetFolioResult @{ ok = $false; error = $_.Exception.Message }
    exit 1
}
