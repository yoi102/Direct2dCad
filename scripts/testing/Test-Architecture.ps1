param(
    [string] $RepositoryRoot = (Join-Path $PSScriptRoot '../..'),
    [string] $SolutionFile = 'Direct2dCad.slnx'
)

$ErrorActionPreference = 'Stop'
$repositoryPath = [System.IO.Path]::GetFullPath($RepositoryRoot)
$solutionPath = [System.IO.Path]::GetFullPath((Join-Path $repositoryPath $SolutionFile))
$failures = [System.Collections.Generic.List[string]]::new()
$graph = @{}

if (!(Test-Path -LiteralPath $solutionPath -PathType Leaf)) {
    throw "Architecture: solution does not exist: $solutionPath"
}
[xml] $solution = Get-Content -LiteralPath $solutionPath -Raw
$solutionDirectory = Split-Path -Parent $solutionPath
foreach ($item in $solution.SelectNodes("//*[local-name()='Project' and @Path]")) {
    $projectPath = [System.IO.Path]::GetFullPath((Join-Path $solutionDirectory $item.GetAttribute('Path')))
    if ($graph.ContainsKey($projectPath)) {
        $failures.Add("Duplicate solution project: $projectPath")
        continue
    }
    if (!(Test-Path -LiteralPath $projectPath -PathType Leaf)) {
        $failures.Add("Missing solution project: $projectPath")
        continue
    }
    [xml] $projectXml = Get-Content -LiteralPath $projectPath -Raw
    $references = [System.Collections.Generic.List[string]]::new()
    foreach ($reference in $projectXml.SelectNodes("//*[local-name()='ProjectReference' and @Include]")) {
        $include = $reference.GetAttribute('Include')
        # Inspect every declared conditional edge, so another target/configuration
        # cannot hide a forbidden dependency. This repository uses literal paths.
        if ($include -match '[\$\*;]') {
            $failures.Add("Unsupported nonliteral project reference in ${projectPath}: $include")
            continue
        }
        $target = [System.IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $projectPath) $include))
        $references.Add($target)
        if (!(Test-Path -LiteralPath $target -PathType Leaf)) {
            $failures.Add("Missing project reference: $projectPath -> $target")
        }
    }
    $nativeDependencies = [System.Collections.Generic.List[string]]::new()
    foreach ($package in $projectXml.SelectNodes("//*[local-name()='PackageReference' and @Include]")) {
        $packageName = $package.GetAttribute('Include')
        if ($packageName -match '^(Vortice|SharpDX)(\.|$)') { $nativeDependencies.Add($packageName) }
    }
    foreach ($framework in $projectXml.SelectNodes("//*[local-name()='FrameworkReference' and @Include]")) {
        $frameworkName = $framework.GetAttribute('Include')
        if ($frameworkName -match '^Microsoft\.WindowsDesktop\.App') { $nativeDependencies.Add($frameworkName) }
    }
    foreach ($property in $projectXml.SelectNodes("//*[local-name()='UseWPF' or local-name()='UseWindowsForms']")) {
        if ($property.InnerText.Trim() -ieq 'true') { $nativeDependencies.Add($property.LocalName) }
    }
    $graph[$projectPath] = [pscustomobject]@{
        Name = [System.IO.Path]::GetFileNameWithoutExtension($projectPath)
        References = $references.ToArray()
        NativeDependencies = $nativeDependencies.ToArray()
    }
}
if ($graph.Count -eq 0) { $failures.Add('The solution contains no readable projects.') }
foreach ($source in $graph.Keys) {
    foreach ($target in $graph[$source].References) {
        if (!$graph.ContainsKey($target) -and (Test-Path -LiteralPath $target -PathType Leaf)) {
            $failures.Add("Referenced project is not registered in the solution: $source -> $target")
        }
    }
}

$visiting = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
$visited = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
function Visit-Project([string] $Path, [string[]] $Trail) {
    if (!$graph.ContainsKey($Path) -or $visited.Contains($Path)) { return }
    if (!$visiting.Add($Path)) {
        $names = @($Trail + $Path | ForEach-Object { [System.IO.Path]::GetFileNameWithoutExtension($_) })
        $failures.Add("Project reference cycle: $($names -join ' -> ')")
        return
    }
    foreach ($target in $graph[$Path].References) { Visit-Project $target ($Trail + $Path) }
    [void] $visiting.Remove($Path)
    [void] $visited.Add($Path)
}
foreach ($source in $graph.Keys) { Visit-Project $source @() }

$coreProjects = @(
    'Direct2dCad.Db', 'Direct2dCad.ChangeTracking', 'Direct2dCad.Commands',
    'Direct2dCad.CommandLine', 'Direct2dCad.Editor', 'Direct2dCad.Indexing',
    'Direct2dCad.HitTesting', 'Direct2dCad.IO', 'Direct2dCad.Rendering',
    'Direct2dCad.Rendering.Handles', 'Direct2dCad.Rendering.Transient', 'Direct2dCad.AI.Contracts'
)
foreach ($source in $graph.Keys) {
    $name = $graph[$source].Name
    $policy = if ($name -in $coreProjects) { 'Core' }
        elseif ($name -eq 'Direct2dCad.Application') { 'Application' }
        elseif ($name -in @('Direct2dCad.ViewModels', 'Direct2dCad.ViewModels.Services')) { 'Presentation' }
        else { $null }
    if (!$policy) { continue }

    $reachable = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $queue = [System.Collections.Generic.Queue[object]]::new()
    $queue.Enqueue([pscustomobject]@{ Path = $source; Names = @($name) })
    while ($queue.Count -gt 0) {
        $step = $queue.Dequeue()
        if (!$reachable.Add($step.Path) -or !$graph.ContainsKey($step.Path)) { continue }
        $dependency = $graph[$step.Path]
        $dependencyName = $dependency.Name
        $forbidden = $dependencyName -match '^Direct2dCad\.(Rendering\.Direct2D|Ole\.Windows|wpf)(\.|$)'
        if ($policy -in @('Core', 'Application')) {
            $forbidden = $forbidden -or $dependencyName -match '^Direct2dCad\.(ViewModels|Client\.Common)(\.|$)'
        }
        if ($policy -eq 'Core' -and $dependencyName -eq 'Direct2dCad.Application') { $forbidden = $true }
        if ($forbidden) { $failures.Add("$policy dependency boundary: $($step.Names -join ' -> ')") }
        foreach ($native in $dependency.NativeDependencies) {
            $failures.Add("$policy native/UI dependency: $($step.Names -join ' -> ') -> $native")
        }
        foreach ($target in $dependency.References) {
            if ($graph.ContainsKey($target)) {
                $queue.Enqueue([pscustomobject]@{ Path = $target; Names = @($step.Names) + $graph[$target].Name })
            }
        }
    }
}

if ($failures.Count -gt 0) {
    throw "Architecture validation failed:`n - $($failures -join "`n - ")"
}
$edgeCount = 0
foreach ($project in $graph.Values) { $edgeCount += $project.References.Count }
Write-Output "Architecture validation passed: $($graph.Count) projects, $edgeCount project references; no cycles or forbidden dependencies."
