$ErrorActionPreference = 'Stop'
$guard = Join-Path $PSScriptRoot 'Test-Architecture.ps1'
$temporaryParent = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$fixtureRoot = Join-Path $temporaryParent ('direct2dcad-architecture-guard-' + [Guid]::NewGuid().ToString('N'))
$caseCount = 0

function Write-Fixture([hashtable] $Projects, [string[]] $ExtraSolutionPaths = @()) {
    $casePath = Join-Path $fixtureRoot ([Guid]::NewGuid().ToString('N'))
    [void] (New-Item -ItemType Directory -Path $casePath -Force)
    $solutionEntries = [System.Collections.Generic.List[string]]::new()
    foreach ($name in $Projects.Keys) {
        $projectDirectory = Join-Path $casePath $name
        [void] (New-Item -ItemType Directory -Path $projectDirectory -Force)
        $content = $Projects[$name]
        Set-Content -LiteralPath (Join-Path $projectDirectory "$name.csproj") -Value "<Project>$content</Project>"
        $solutionEntries.Add("<Project Path=`"$name/$name.csproj`" />")
    }
    foreach ($path in $ExtraSolutionPaths) { $solutionEntries.Add("<Project Path=`"$path`" />") }
    Set-Content -LiteralPath (Join-Path $casePath 'Direct2dCad.slnx') -Value "<Solution>$($solutionEntries -join '')</Solution>"
    return $casePath
}

function Reference([string] $Name) {
    return "<ItemGroup><ProjectReference Include=`"../$Name/$Name.csproj`" /></ItemGroup>"
}

function Assert-Fixture([string] $Path, [string] $ExpectedFailure = '') {
    $actualFailure = $null
    try { & $guard -RepositoryRoot $Path | Out-Null }
    catch { $actualFailure = $_.Exception.Message }
    if (!$ExpectedFailure -and $actualFailure) { throw "Valid graph was rejected: $actualFailure" }
    if ($ExpectedFailure -and (!$actualFailure -or $actualFailure -notlike "*$ExpectedFailure*")) {
        throw "Expected '$ExpectedFailure', received '$actualFailure'."
    }
    $script:caseCount++
}

try {
    Assert-Fixture (Write-Fixture @{
        'Direct2dCad.Db' = ''
        'Direct2dCad.Commands' = (Reference 'Direct2dCad.Db')
        'Direct2dCad.Application' = (Reference 'Direct2dCad.Commands')
        'Direct2dCad.ViewModels' = (Reference 'Direct2dCad.Application')
    })
    Assert-Fixture (Write-Fixture @{ 'A' = (Reference 'B'); 'B' = (Reference 'A') }) 'Project reference cycle'
    Assert-Fixture (Write-Fixture @{ 'A' = '' } @('Missing/Missing.csproj')) 'Missing solution project'
    Assert-Fixture (Write-Fixture @{ 'A' = (Reference 'Missing') }) 'Missing project reference'
    $unlisted = Write-Fixture @{ 'A' = (Reference 'External') }
    [void] (New-Item -ItemType Directory -Path (Join-Path $unlisted 'External'))
    Set-Content -LiteralPath (Join-Path $unlisted 'External/External.csproj') -Value '<Project />'
    Assert-Fixture $unlisted 'not registered in the solution'
    Assert-Fixture (Write-Fixture @{
        'Direct2dCad.Db' = (Reference 'Direct2dCad.ViewModels')
        'Direct2dCad.ViewModels' = ''
    }) 'Core dependency boundary'
    Assert-Fixture (Write-Fixture @{
        'Direct2dCad.Editor' = (Reference 'Helper')
        'Helper' = (Reference 'Direct2dCad.Rendering.Direct2D')
        'Direct2dCad.Rendering.Direct2D' = ''
    }) 'Direct2dCad.Editor -> Helper -> Direct2dCad.Rendering.Direct2D'
    Assert-Fixture (Write-Fixture @{
        'Direct2dCad.Application' = (Reference 'Direct2dCad.ViewModels.Services')
        'Direct2dCad.ViewModels.Services' = ''
    }) 'Application dependency boundary'
    Assert-Fixture (Write-Fixture @{
        'Direct2dCad.ViewModels.Services' = (Reference 'Direct2dCad.Rendering.Direct2D')
        'Direct2dCad.Rendering.Direct2D' = ''
    }) 'Presentation dependency boundary'
    Assert-Fixture (Write-Fixture @{
        'Direct2dCad.Commands' = '<ItemGroup><PackageReference Include="Vortice.Windows" Version="3.8.3" /></ItemGroup>'
    }) 'Core native/UI dependency'
    Assert-Fixture (Write-Fixture @{
        'Direct2dCad.Db' = '<PropertyGroup><UseWPF>true</UseWPF></PropertyGroup>'
    }) 'Core native/UI dependency'
    Write-Output "Architecture guard regression passed: $caseCount isolated graph fixtures."
}
finally {
    # Only this newly generated fixture directory may be removed recursively.
    $resolvedFixture = [System.IO.Path]::GetFullPath($fixtureRoot)
    $allowedPrefix = $temporaryParent.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (!$resolvedFixture.StartsWith($allowedPrefix, [System.StringComparison]::OrdinalIgnoreCase) -or
        [System.IO.Path]::GetFileName($resolvedFixture) -notlike 'direct2dcad-architecture-guard-*') {
        throw "Refusing to remove fixture outside temporary root: $resolvedFixture"
    }
    if (Test-Path -LiteralPath $resolvedFixture) { Remove-Item -LiteralPath $resolvedFixture -Recurse -Force }
}
