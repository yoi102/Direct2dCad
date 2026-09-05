param(
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release',
    [switch] $CollectCoverage,
    [switch] $IncludeWindowsIntegration,
    [switch] $IncludeUiAutomation,
    [switch] $IncludeClipboardTests,
    [switch] $NoBuild,
    [string] $ResultsDirectory
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
& (Join-Path $PSScriptRoot 'Test-CoverageSummary.ps1')
if (!$ResultsDirectory) {
    $ResultsDirectory = Join-Path $root "TestResults/regression-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
}
$ResultsDirectory = [System.IO.Path]::GetFullPath($ResultsDirectory)
if (!$NoBuild) {
    dotnet build (Join-Path $root 'Direct2dCad.slnx') -c $Configuration --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw 'The solution build failed.' }
}

$projects = @(Get-ChildItem -LiteralPath $root -Directory -Filter '*Tests' | ForEach-Object {
    Get-ChildItem -LiteralPath $_.FullName -Filter '*.csproj'
} | Sort-Object Name)
$failed = @()
foreach ($project in $projects) {
    $isUi = $project.BaseName -eq 'Direct2dCad.UiAutomation.Tests'
    if ($isUi -and !$IncludeUiAutomation) { continue }
    if ($project.BaseName -eq 'Direct2dCad.Windows.IntegrationTests' -and !$IncludeWindowsIntegration) { continue }
    $arguments = @('test', $project.FullName, '-c', $Configuration, '--no-build', '--nologo', '-v', 'minimal',
        '--logger', 'trx', '--results-directory', (Join-Path $ResultsDirectory $project.BaseName))
    if ($CollectCoverage -and !$isUi) { $arguments += '--collect:XPlat Code Coverage' }
    if ($isUi -and !$IncludeClipboardTests) {
        Write-Warning 'Clipboard UI test excluded. Run it only in a dedicated test desktop with -IncludeClipboardTests.'
        $arguments += @('--filter', 'FullyQualifiedName!~CadImageAndOleClipboardContentEnterMovablePasteAndCanBePlaced')
    }
    dotnet @arguments
    if ($LASTEXITCODE -ne 0) { $failed += $project.BaseName }
}

if ($CollectCoverage) {
    & (Join-Path $PSScriptRoot 'Get-CoverageSummary.ps1') -ResultsDirectory $ResultsDirectory -OutputDirectory $ResultsDirectory
}
Write-Output "Results: $ResultsDirectory"
if ($failed.Count -gt 0) { throw "Failed test projects: $($failed -join ', ')" }
