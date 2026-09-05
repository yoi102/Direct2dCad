param(
    [Parameter(Mandatory)] [string[]] $ResultsDirectory,
    [string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'
$files = @{}
$reports = @($ResultsDirectory | ForEach-Object {
    Get-ChildItem -LiteralPath $_ -Filter coverage.cobertura.xml -Recurse
})
if ($reports.Count -eq 0) { throw 'No Cobertura coverage reports were found.' }

function Resolve-CoveragePath($report, $sourceRoots, [string] $fileName) {
    if ([System.IO.Path]::IsPathRooted($fileName)) {
        return [System.IO.Path]::GetFullPath($fileName).Replace('\', '/')
    }
    $candidates = @($sourceRoots | ForEach-Object {
        $source = [string] $_
        if (![System.IO.Path]::IsPathRooted($source)) { $source = Join-Path $report.DirectoryName $source }
        [System.IO.Path]::GetFullPath((Join-Path $source $fileName))
    })
    if ($candidates.Count -eq 0) {
        throw "Relative coverage path '$fileName' has no source root in $($report.FullName)."
    }
    $existing = @($candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf })
    if ($existing.Count -gt 1) { throw "Ambiguous coverage path: $fileName" }
    $resolved = if ($existing.Count -eq 1) { $existing[0] } else { $candidates[0] }
    return $resolved.Replace('\', '/')
}

foreach ($report in $reports) {
    [xml]$xml = Get-Content -LiteralPath $report.FullName -Raw
    $sourceRoots = @($xml.coverage.sources.source | Where-Object { ![string]::IsNullOrWhiteSpace($_) })
    foreach ($package in $xml.coverage.packages.package) {
        if ($package.name -notlike 'Direct2dCad*' -or $package.name -like '*Tests') { continue }
        foreach ($class in $package.classes.class) {
            $path = Resolve-CoveragePath $report $sourceRoots ([string]$class.filename)
            if ($path -match '(^|/)obj/|\.g\.cs$|\.Designer\.cs$') { continue }
            $key = "$($package.name)|$path"
            if (!$files.ContainsKey($key)) {
                $files[$key] = [pscustomobject]@{
                    Project = [string]$package.name
                    File = $path
                    Lines = [System.Collections.Generic.HashSet[int]]::new()
                    Covered = [System.Collections.Generic.HashSet[int]]::new()
                }
            }
            foreach ($line in $class.lines.line) {
                [void]$files[$key].Lines.Add([int]$line.number)
                if ([long]$line.hits -gt 0) { [void]$files[$key].Covered.Add([int]$line.number) }
            }
        }
    }
}

$fileSummary = @($files.Values | ForEach-Object {
    [pscustomobject]@{
        Project = $_.Project
        File = $_.File
        Covered = $_.Covered.Count
        Lines = $_.Lines.Count
        Uncovered = $_.Lines.Count - $_.Covered.Count
        Percent = [math]::Round(100 * $_.Covered.Count / [math]::Max(1, $_.Lines.Count), 2)
    }
} | Sort-Object Project, File)
$projectSummary = @($fileSummary | Group-Object Project | ForEach-Object {
    $covered = ($_.Group | Measure-Object Covered -Sum).Sum
    $lines = ($_.Group | Measure-Object Lines -Sum).Sum
    [pscustomobject]@{
        Project = $_.Name
        Covered = [int]$covered
        Lines = [int]$lines
        Percent = [math]::Round(100 * $covered / [math]::Max(1, $lines), 2)
    }
} | Sort-Object Project)

if ($OutputDirectory) {
    [void](New-Item -ItemType Directory -Path $OutputDirectory -Force)
    $fileSummary | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'coverage-files.csv') -NoTypeInformation
    $projectSummary | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'coverage-projects.csv') -NoTypeInformation
}
Write-Output 'Executable line coverage; generated files excluded; external UI process not instrumented.'
$projectSummary | Format-Table -AutoSize
