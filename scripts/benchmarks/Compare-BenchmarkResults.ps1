[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string]$BaselineCsv,

    [Parameter(Mandatory = $true)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string]$CurrentCsv,

    [ValidateRange(0.0, 1000.0)]
    [double]$MaxRegressionPercent = 10.0,

    [ValidateSet('Mean', 'P95')]
    [string]$Metric = 'Mean',

    [switch]$FailOnMissing
)

$ErrorActionPreference = 'Stop'

function ConvertTo-Nanoseconds {
    param([Parameter(Mandatory = $true)][string]$Value)

    $normalized = $Value.Trim().Replace(',', '')
    if ($normalized -notmatch '^(?<number>[0-9]+(?:\.[0-9]+)?)\s*(?<unit>\S+)$') {
        throw "Unsupported BenchmarkDotNet time value: '$Value'"
    }

    $number = [double]::Parse(
        $Matches.number,
        [Globalization.CultureInfo]::InvariantCulture)
    $factor = switch ($Matches.unit) {
        'ns' { 1.0 }
        'us' { 1e3 }
        'ms' { 1e6 }
        's'  { 1e9 }
        default {
            if ($Matches.unit.EndsWith('s', [StringComparison]::OrdinalIgnoreCase)) {
                1e3
            }
            else {
                throw "Unsupported BenchmarkDotNet time unit: '$($Matches.unit)'"
            }
        }
    }

    return $number * $factor
}

function Get-ScenarioMetadata {
    param([Parameter(Mandatory = $true)][object[]]$Rows)

    if ($Rows.Count -eq 0) {
        throw 'Benchmark CSV does not contain any result rows.'
    }

    $columns = @($Rows[0].PSObject.Properties.Name)
    $categoriesIndex = [Array]::IndexOf($columns, 'Categories')
    $meanIndex = [Array]::IndexOf($columns, 'Mean')
    if ($categoriesIndex -lt 0 -or $meanIndex -lt 0 -or $meanIndex -le $categoriesIndex) {
        throw 'Benchmark CSV does not contain the expected Categories and Mean columns.'
    }

    $parameterColumns = if ($meanIndex -gt $categoriesIndex + 1) {
        @($columns[($categoriesIndex + 1)..($meanIndex - 1)])
    }
    else {
        @()
    }

    return [pscustomobject]@{
        ParameterColumns = $parameterColumns
    }
}

function Get-ScenarioKey {
    param(
        [Parameter(Mandatory = $true)][object]$Row,
        [Parameter(Mandatory = $true)][string[]]$ParameterColumns
    )

    $parts = @([string]$Row.Method, [string]$Row.Categories)
    foreach ($column in $ParameterColumns) {
        $parts += "$column=$($Row.$column)"
    }

    return $parts -join '|'
}

$baselineRows = @(Import-Csv -LiteralPath $BaselineCsv -Encoding UTF8)
$currentRows = @(Import-Csv -LiteralPath $CurrentCsv -Encoding UTF8)
$baselineMetadata = Get-ScenarioMetadata $baselineRows
$currentMetadata = Get-ScenarioMetadata $currentRows
if ($Metric -notin @($baselineRows[0].PSObject.Properties.Name) -or
    $Metric -notin @($currentRows[0].PSObject.Properties.Name)) {
    throw "The selected metric '$Metric' is not present in both CSV files."
}

if (($baselineMetadata.ParameterColumns -join '|') -ne
    ($currentMetadata.ParameterColumns -join '|')) {
    throw 'Baseline and current CSV files use different benchmark parameter columns.'
}

$baselineByKey = @{}
foreach ($row in $baselineRows) {
    $key = Get-ScenarioKey $row $baselineMetadata.ParameterColumns
    $baselineByKey[$key] = $row
}

$comparisons = [Collections.Generic.List[object]]::new()
$missing = [Collections.Generic.List[string]]::new()
foreach ($current in $currentRows) {
    $key = Get-ScenarioKey $current $currentMetadata.ParameterColumns
    if (-not $baselineByKey.ContainsKey($key)) {
        $missing.Add($key)
        continue
    }

    $baseline = $baselineByKey[$key]
    $baselineValue = [string]$baseline.$Metric
    $currentValue = [string]$current.$Metric
    $baselineNanoseconds = ConvertTo-Nanoseconds $baselineValue
    $currentNanoseconds = ConvertTo-Nanoseconds $currentValue
    $changePercent = if ($baselineNanoseconds -eq 0) {
        0.0
    }
    else {
        ($currentNanoseconds - $baselineNanoseconds) / $baselineNanoseconds * 100.0
    }

    $comparisons.Add([pscustomobject]@{
        Scenario = $key
        Metric = $Metric
        Baseline = $baselineValue
        Current = $currentValue
        ChangePercent = [Math]::Round($changePercent, 2)
        Status = if ($changePercent -gt $MaxRegressionPercent) { 'REGRESSION' } else { 'OK' }
    })
}

$comparisons |
    Sort-Object ChangePercent -Descending |
    Format-Table Scenario, Metric, Baseline, Current, ChangePercent, Status -AutoSize

if ($missing.Count -gt 0) {
    Write-Warning "Current results contain $($missing.Count) scenario(s) without a baseline:"
    $missing | ForEach-Object { Write-Warning "  $_" }
}

$regressions = @($comparisons | Where-Object Status -eq 'REGRESSION')
if ($regressions.Count -gt 0) {
    Write-Error "$($regressions.Count) benchmark scenario(s) exceeded the $MaxRegressionPercent% regression threshold."
    exit 1
}

if ($FailOnMissing -and $missing.Count -gt 0) {
    Write-Error 'Benchmark scenarios are missing from the baseline.'
    exit 1
}

Write-Host "Benchmark comparison passed ($($comparisons.Count) matched scenario(s))."
