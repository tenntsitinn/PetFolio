param([ValidateRange(2,60)][int]$Seconds=30,[ValidateRange(0,30)][int]$Warmup=10,[ValidateRange(1,10)][int]$Rounds=5,[switch]$NativeDrag,[switch]$WatcherOnly,
    [ValidateSet('idle-quota','moving-quota','native-drag','system-low-opacity','hidden-bubble','pet-closed')][string[]]$Scenarios=@())
$ErrorActionPreference='Stop'
if(-not $Scenarios.Count){$Scenarios=@('idle-quota','moving-quota','system-low-opacity','hidden-bubble','pet-closed');if($NativeDrag){$Scenarios+='native-drag'}}
if($Scenarios -contains 'native-drag' -and -not $NativeDrag){throw 'Native drag scenarios require explicit -NativeDrag.'}
$benchmarkRoot=Join-Path $PSScriptRoot ('.test-build\benchmark-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $benchmarkRoot | Out-Null
$utf8=New-Object Text.UTF8Encoding($false)
$sources=@{}
foreach($label in @('reference','optimized')) {
    $sourceRoot=Join-Path $benchmarkRoot ($label+'-source')
    New-Item -ItemType Directory -Path (Join-Path $sourceRoot 'assets') -Force | Out-Null
    Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | ForEach-Object {Copy-Item -LiteralPath $_.FullName -Destination $sourceRoot}
    foreach($name in @('build.ps1','app.manifest')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $sourceRoot}
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'assets\PetFolio.ico') -Destination (Join-Path $sourceRoot 'assets')
    $sources[$label]=$sourceRoot
}
# A controlled reference disables only the three performance changes. It keeps
# the same diagnostics, quota lifecycle, logging, renderer and measurement code.
$trackerPath=Join-Path $sources.reference 'PetStateService.cs'
$tracker=[IO.File]::ReadAllText($trackerPath)
$tracker=$tracker.Replace('        StartMetadataWatcher();','        // Reference: polling without file notifications.')
if(-not $WatcherOnly) {
$condition='        var previous=Current;\r?\n        if\(previous.Target[\s\S]*?return;\r?\n'
if([regex]::Matches($tracker,$condition).Count -ne 1){throw 'Reference snapshot guard mismatch.'}
$tracker=[regex]::Replace($tracker,$condition,'')
$original=(& git -C $PSScriptRoot show HEAD:PetStateService.cs) -join "`n"
if($LASTEXITCODE -ne 0){throw 'Cannot read reference selection parser.'}
$start=$original.IndexOf('    void ReadSelection() {');$end=$original.IndexOf('    void ApplyPetColour(',$start)
$selection=$original.Substring($start,$end-$start).Replace('    void ReadSelection() {',"    void ReadSelection() {`n        SelectionReads++;")
$start=$tracker.IndexOf('    void ReadSelection() {');$end=$tracker.IndexOf('    void ApplyPetColour(',$start)
$tracker=$tracker.Substring(0,$start)+$selection+$tracker.Substring($end)
$tracker=$tracker.Replace('follow.Interval=100;','follow.Interval=16;').Replace('follow.Interval=drag.Active?16:100;','follow.Interval=16;')
$tracker=$tracker.Replace('if(!drag.Active || watch.ElapsedMilliseconds>=nextMetadata)', 'if(watch.ElapsedMilliseconds>=nextMetadata)')
}
[IO.File]::WriteAllText($trackerPath,$tracker,$utf8)
$viewPath=Join-Path $sources.reference 'QuotaLabel.cs';$view=[IO.File]::ReadAllText($viewPath)
if(-not $WatcherOnly) {
$start=$view.IndexOf('    void UpdateInteractionTimer() {');$end=$view.IndexOf('    Color WithOpacity(',$start)
$view=$view.Substring(0,$start)+"    void UpdateInteractionTimer() {interactionTimer.Stop();}`n"+$view.Substring($end)
}
[IO.File]::WriteAllText($viewPath,$view,$utf8)
$manifest=@()
foreach($label in @('reference','optimized')) {
    $binaryRoot=Join-Path $benchmarkRoot $label
    & (Join-Path $sources[$label] 'build.ps1') -OutputDirectory $binaryRoot -OutputName 'PairedPerformanceProbe.exe' -EntryPoint PairedPerformanceProbe -Target exe -AdditionalSources PairedPerformanceProbe.cs
    $manifest+=@{label=$label;executableSha256=(Get-FileHash -LiteralPath (Join-Path $binaryRoot 'PairedPerformanceProbe.exe')).Hash;
        sources=@(Get-ChildItem -LiteralPath $sources[$label] -Filter '*.cs' | Sort-Object Name | ForEach-Object {@{name=$_.Name;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash}})}
}
$referenceDescription=if($WatcherOnly){'Same snapshot with file notifications disabled; all other behavior identical.'}else{'Same snapshot with file notifications, config cache, changed-state suppression and idle timer changes disabled.'}
[IO.File]::WriteAllText((Join-Path $benchmarkRoot 'manifest.json'),(@{seconds=$Seconds;warmup=$Warmup;rounds=$Rounds;scenarios=$Scenarios;nativeDrag=[bool]$NativeDrag;builds=$manifest;reference=$referenceDescription}|ConvertTo-Json -Depth 8),$utf8)
Write-Output "Frozen benchmark: $benchmarkRoot"
$cancel=Join-Path $benchmarkRoot 'cancel'
for($round=1;$round -le $Rounds;$round++) {
    $order=if($round%2){@('reference','optimized')}else{@('optimized','reference')}
    foreach($label in $order) {
        Write-Output "Round $round/$Rounds : $label"
        $exe=Join-Path $benchmarkRoot ($label+'\PairedPerformanceProbe.exe');$output=Join-Path $benchmarkRoot ("round-$round-$label.json")
        & $exe $output $Seconds $Warmup $label ([string][bool]$NativeDrag).ToLowerInvariant() $cancel ($Scenarios -join ',')
        if($LASTEXITCODE -ne 0){throw "Benchmark failed: $label round $round"}
    }
}
Write-Output "Completed benchmark: $benchmarkRoot"
