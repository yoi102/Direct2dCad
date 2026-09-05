$ErrorActionPreference = 'Stop'
$output = Join-Path $PSScriptRoot "../../TestResults/coverage-summary-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
& (Join-Path $PSScriptRoot 'Get-CoverageSummary.ps1') -ResultsDirectory (Join-Path $PSScriptRoot 'fixtures/coverage') -OutputDirectory $output
$files = @(Import-Csv -LiteralPath (Join-Path $output 'coverage-files.csv'))
$projects = @(Import-Csv -LiteralPath (Join-Path $output 'coverage-projects.csv'))
if ($files.Count -ne 1 -or $projects.Count -ne 1) { throw 'Source roots were not deduplicated or exclusions failed.' }
if ($files[0].Lines -ne 3 -or $files[0].Covered -ne 2 -or $files[0].Percent -ne 66.67) {
    throw 'Coverage hits from the same physical file were not merged correctly.'
}
Write-Output 'Coverage summary regression passed.'
