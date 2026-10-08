param(
    [string]$ShortcutPath = (Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'PetFolio.lnk'),
    [string]$Executable = (Join-Path $PSScriptRoot 'PetFolio.exe'),
    [string]$DataDirectory
)
$ErrorActionPreference = 'Stop'
$executablePath = (Resolve-Path -LiteralPath $Executable).Path
if ($DataDirectory) {
    $dataPath = (Resolve-Path -LiteralPath $DataDirectory).Path
    if (-not (Test-Path -LiteralPath (Join-Path $dataPath '.codex-global-state.json') -PathType Leaf)) {
        throw 'DataDirectory must contain Codex desktop state (.codex-global-state.json).'
    }
}
$shortcutFile = [IO.Path]::GetFullPath($ShortcutPath)
if ([IO.Path]::GetExtension($shortcutFile) -ne '.lnk') { throw 'ShortcutPath must end with .lnk.' }
if (-not (Test-Path -LiteralPath (Split-Path -Parent $shortcutFile) -PathType Container)) { throw 'Shortcut directory does not exist.' }
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutFile)
$shortcut.TargetPath = $executablePath
$shortcut.WorkingDirectory = Split-Path -Parent $executablePath
$shortcut.IconLocation = $executablePath + ',0'
$shortcut.Description = 'PetFolio desktop companion for Codex Pet'
# CLI version folders change when Codex updates. Only pin desktop data, if needed.
$shortcut.Arguments = ''
if ($DataDirectory) { $shortcut.Arguments = '--data-directory "' + [regex]::Replace($dataPath, '(\\+)$', '$1$1') + '"' }
$shortcut.Save()
Write-Output $shortcutFile
