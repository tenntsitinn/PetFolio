param(
    [string]$CodexExecutable,
    [string]$DataDirectory,
    [switch]$DesktopLaunch
)
$ErrorActionPreference = 'Stop'
try {
    if ([bool]$CodexExecutable -ne [bool]$DataDirectory) { throw '請同時指定 -CodexExecutable 與 -DataDirectory，或省略兩者以自動偵測。' }
    $petFolioExecutable = Join-Path $PSScriptRoot 'PetFolio.exe'
    if (-not (Test-Path -LiteralPath $petFolioExecutable)) {
        & (Join-Path $PSScriptRoot 'build.ps1')
    }
    if ($CodexExecutable) {
        $petFolioArgumentLine = '"' + $CodexExecutable.TrimEnd('\') + '" "' + $DataDirectory.TrimEnd('\') + '"'
        Start-Process -FilePath $petFolioExecutable -ArgumentList $petFolioArgumentLine -WorkingDirectory $PSScriptRoot -WindowStyle Hidden
    } else {
        Start-Process -FilePath $petFolioExecutable -WorkingDirectory $PSScriptRoot -WindowStyle Hidden
    }
} catch {
    if ($DesktopLaunch) {
        Add-Type -AssemblyName System.Windows.Forms
        [void][System.Windows.Forms.MessageBox]::Show($_.Exception.Message, 'PetFolio', 'OK', 'Error')
        exit 1
    }
    throw
}
