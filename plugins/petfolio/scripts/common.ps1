$ErrorActionPreference = 'Stop'

function Get-PetFolioExecutable {
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT -or -not [Environment]::Is64BitOperatingSystem) {
        throw 'PetFolio requires Windows x64.'
    }
    $framework = Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -ErrorAction SilentlyContinue
    if (-not $framework -or $framework.Release -lt 528040) { throw 'PetFolio requires .NET Framework 4.8.' }
    $path = Join-Path (Split-Path -Parent $PSScriptRoot) 'bin\win-x64\PetFolio.exe'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw 'PetFolio.exe is missing. Build or reinstall the complete Windows plugin package.' }
    return $path
}

function ConvertTo-PetFolioArgument([string]$Value) {
    # Windows CommandLineToArgvW quoting, also supported by Windows PowerShell 5.1.
    return '"' + ([regex]::Replace([regex]::Replace($Value, '(\\*)"', '$1$1\"'), '(\\+)$', '$1$1')) + '"'
}

function New-PetFolioProcess([string]$Executable, [string[]]$CommandArguments) {
    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName = $Executable
    $info.Arguments = (@($CommandArguments | ForEach-Object { ConvertTo-PetFolioArgument $_ }) -join ' ')
    $info.WorkingDirectory = Split-Path -Parent $Executable
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.StandardOutputEncoding = New-Object System.Text.UTF8Encoding($false)
    $info.StandardErrorEncoding = New-Object System.Text.UTF8Encoding($false)
    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $info
    return $process
}

function Invoke-PetFolioControl([string]$Executable, [string[]]$CommandArguments) {
    $process = New-PetFolioProcess $Executable $CommandArguments
    try {
        [void]$process.Start()
        $output = $process.StandardOutput.ReadToEndAsync()
        $errors = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(15000)) {
            $process.Kill()
            throw 'PetFolio control command timed out.'
        }
        $text = $output.Result.Trim()
        if ($text) { return ($text | ConvertFrom-Json) }
        if ($process.ExitCode -ne 0) { throw ('PetFolio control failed: ' + $errors.Result.Trim()) }
        return [pscustomobject]@{ ok = $true }
    } finally { $process.Dispose() }
}

function Get-PetFolioPaths([string]$CodexExecutable, [string]$DataDirectory) {
    if ([bool]$CodexExecutable -ne [bool]$DataDirectory) {
        throw 'Specify both -CodexExecutable and -DataDirectory, or omit both for automatic discovery.'
    }
    if ($CodexExecutable) { return @($CodexExecutable, $DataDirectory) }
}

function Write-PetFolioResult($Result) {
    $json = $Result | ConvertTo-Json -Depth 6 -Compress
    # Windows PowerShell 5.1 can encode redirected output using the system code
    # page. Unicode JSON escapes preserve paths and errors on every host.
    [regex]::Replace($json, '[^\x00-\x7F]', [Text.RegularExpressions.MatchEvaluator]{
        param($match)
        '\u{0:x4}' -f [int][char]$match.Value
    })
}
