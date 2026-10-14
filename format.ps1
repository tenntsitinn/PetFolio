#requires -Version 7.3
param([switch]$Check)
$ErrorActionPreference = 'Stop'

# PowerShell 7 bundles Roslyn for Add-Type. Formatting needs no SDK, project file,
# package restore or change to the .NET Framework build used by this repository.
foreach ($assembly in @('Microsoft.CodeAnalysis.dll', 'Microsoft.CodeAnalysis.CSharp.dll')) {
    $assemblyPath = Join-Path $PSHOME $assembly
    if (-not (Test-Path -LiteralPath $assemblyPath)) {
        throw "This formatter needs a PowerShell 7 distribution containing $assembly."
    }
    Add-Type -Path $assemblyPath
}

function Format-CSharpSource([string]$Source) {
    $original = [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText($Source).GetRoot()
    if ($original.ContainsDiagnostics) {
        $errors = @($original.GetDiagnostics() | Where-Object Severity -eq 'Error')
        if ($errors.Count -gt 0) { throw ($errors -join [Environment]::NewLine) }
    }
    $normalized = [Microsoft.CodeAnalysis.SyntaxNodeExtensions]::NormalizeWhitespace($original, '    ', "`r`n", $false)
    $normalizedText = $normalized.ToFullString()
    $originalTokens = @($original.DescendantTokens())
    $normalizedTokens = @($normalized.DescendantTokens())
    $result = [Text.StringBuilder]::new()
    $previousEnd = 0

    for ($index = 0; $index -lt $normalizedTokens.Count; $index++) {
        $token = $normalizedTokens[$index]
        $gap = $normalizedText.Substring($previousEnd, $token.SpanStart - $previousEnd)
        # Keep deliberately wrapped argument lists and expressions. Roslyn's
        # normalizer otherwise joins them into long lines. Only whitespace is
        # replaced here; comments, directives and literal contents stay intact.
        if ($index -gt 0 -and $gap -match '^\s*$' -and $gap -notmatch '[\r\n]') {
            $originalEnd = $originalTokens[$index - 1].Span.End
            $originalGap = $Source.Substring($originalEnd, $originalTokens[$index].SpanStart - $originalEnd)
            if ($originalGap -match '^\s*\r?\n\s*$') {
                $lineStart = $normalizedText.LastIndexOf("`n", [Math]::Max(0, $token.SpanStart - 1)) + 1
                $linePrefix = $normalizedText.Substring($lineStart, $token.SpanStart - $lineStart)
                $indent = [regex]::Match($linePrefix, '^ *').Length + 4
                $gap = "`r`n" + (' ' * $indent)
            }
        }
        # Close initializer / lambda arguments as `});` or `})`, without leaving
        # a detached closing parenthesis after an expanded brace block.
        if ($index -gt 0 -and $normalizedTokens[$index - 1].Text -eq '}' -and
            $token.Text -in @(')', ']', ',', ';') -and $gap -match '^\s*$') {
            $gap = ''
        }
        [void]$result.Append($gap)
        [void]$result.Append($token.Text)
        $previousEnd = $token.Span.End
    }
    [void]$result.Append($normalizedText.Substring($previousEnd))
    $formatted = $result.ToString().TrimEnd() + "`r`n"
    $parsedResult = [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText($formatted).GetRoot()
    $resultTokens = @($parsedResult.DescendantTokens())
    if ($originalTokens.Count -ne $resultTokens.Count) { throw 'Formatting changed the token count.' }
    for ($index = 0; $index -lt $originalTokens.Count; $index++) {
        if ($originalTokens[$index].RawKind -ne $resultTokens[$index].RawKind -or
            $originalTokens[$index].Text -cne $resultTokens[$index].Text) {
            throw "Formatting changed a C# token at index $index."
        }
    }
    return $formatted
}

$changes = @()
foreach ($file in Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' -File | Sort-Object Name) {
    $source = [IO.File]::ReadAllText($file.FullName)
    $formatted = Format-CSharpSource $source
    $bytes = [IO.File]::ReadAllBytes($file.FullName)
    $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 239 -and $bytes[1] -eq 187 -and $bytes[2] -eq 191
    if ($source -cne $formatted -or -not $hasBom) {
        $changes += $file.Name
        if (-not $Check) { [IO.File]::WriteAllText($file.FullName, $formatted, [Text.UTF8Encoding]::new($true)) }
    }
}
if ($Check -and $changes.Count -gt 0) { throw ('C# formatting required: ' + ($changes -join ', ')) }
Write-Output ("C# formatting {0}: {1} file(s)." -f $(if ($Check) { 'checked' } else { 'updated' }), $changes.Count)
