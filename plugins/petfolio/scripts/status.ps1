param([switch]$CheckEnvironment, [string]$CodexExecutable, [string]$DataDirectory)
. (Join-Path $PSScriptRoot 'common.ps1')
try {
    $executable = Get-PetFolioExecutable
    $paths = @(Get-PetFolioPaths $CodexExecutable $DataDirectory)
    if (-not $CheckEnvironment -and $paths.Count) { throw 'Path arguments require -CheckEnvironment.' }
    $result = Invoke-PetFolioControl $executable @('--status')
    if ($CheckEnvironment) {
        $environment = Invoke-PetFolioControl $executable (@('--check') + $paths)
        $result | Add-Member -NotePropertyName environment -NotePropertyValue $environment
        $result.ok = $result.ok -and $environment.ok
    }
    Write-PetFolioResult $result
    if (-not $result.ok) { exit 1 }
} catch {
    Write-PetFolioResult @{ ok = $false; error = $_.Exception.Message }
    exit 1
}
