param(
    [string]$OutputDirectory = 'TestResults/github-release-0.1.3',
    [string]$ReleaseVersion = '0.1.3'
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$releaseRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDirectory))
if (-not $releaseRoot.StartsWith($repoRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Release output must remain inside the repository.'
}
if (Test-Path -LiteralPath $releaseRoot) {
    throw "Release output already exists. Choose a fresh OutputDirectory: $releaseRoot"
}

$wixVersion = '1.0.3' # Keep the MSI upgrade version monotonic; ARPVERSION displays the app release version.
$publishDirectory = Join-Path $releaseRoot 'publish'
$installerDirectory = Join-Path $releaseRoot 'installer'
$distDirectory = Join-Path $releaseRoot 'dist'
$null = New-Item -ItemType Directory -Path $installerDirectory,$distDirectory

dotnet restore (Join-Path $repoRoot 'Direct2dCad.wpf/Direct2dCad.wpf.csproj') `
    --locked-mode -p:ContinuousIntegrationBuild=true -v minimal
if ($LASTEXITCODE -ne 0) { throw 'Locked application package restore failed.' }

dotnet publish (Join-Path $repoRoot 'Direct2dCad.wpf/Direct2dCad.wpf.csproj') `
    -c Release --no-restore --self-contained true `
    -p:RestoreLockedMode=true -p:ContinuousIntegrationBuild=true -p:DebugType=None -p:DebugSymbols=false `
    "-p:Version=$ReleaseVersion" "-p:InformationalVersion=$ReleaseVersion" `
    "-p:AssemblyVersion=$ReleaseVersion" "-p:FileVersion=$ReleaseVersion" `
    --output "$publishDirectory" -v minimal
if ($LASTEXITCODE -ne 0) { throw 'Self-contained Windows x64 application publish failed.' }

$appExe = Join-Path $publishDirectory 'Direct2dCad.exe'
$appIcon = Join-Path $publishDirectory 'Direct2dCad.ico'
$runtimeConfigPath = Join-Path $publishDirectory 'Direct2dCad.runtimeconfig.json'
foreach ($requiredFile in @($appExe,$appIcon,$runtimeConfigPath)) {
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) { throw "Required self-contained publish file is missing: $requiredFile" }
}
$runtimeConfig = Get-Content -LiteralPath $runtimeConfigPath -Raw | ConvertFrom-Json
if ($runtimeConfig.runtimeOptions.framework -or $runtimeConfig.runtimeOptions.frameworks) {
    throw 'The publish references a locally installed .NET runtime instead of bundling it.'
}
if (-not $runtimeConfig.runtimeOptions.includedFrameworks) {
    throw 'The publish does not declare its bundled .NET runtime frameworks.'
}

dotnet build (Join-Path $repoRoot 'installer/Direct2dCad.Msi/Direct2dCad.Msi.wixproj') `
    -c Release -p:RestoreLockedMode=false `
    "-p:Version=$wixVersion" "-p:ReleaseVersion=$ReleaseVersion" "-p:PublishDirectory=$publishDirectory" `
    "-p:ReleaseArtifactsDirectory=$installerDirectory" -v minimal
if ($LASTEXITCODE -ne 0) { throw 'WiX installer build failed.' }

$builtMsi = Get-ChildItem -LiteralPath $installerDirectory -Filter 'Direct2dCad.msi' -File -Recurse |
    Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
if (-not $builtMsi) { throw 'The WiX build completed without producing Direct2dCad.msi.' }
$releaseMsi = Join-Path $distDirectory 'Direct2dCad.msi'
Copy-Item -LiteralPath $builtMsi.FullName -Destination $releaseMsi

@"
Direct2dCad $ReleaseVersion
Windows x64. Extract this ZIP and run Direct2dCad.exe.
This package contains its own .NET 10 runtime and does not require a separately installed .NET Desktop Runtime.
"@ | Set-Content -LiteralPath (Join-Path $publishDirectory 'README.txt') -Encoding utf8
$portableZip = Join-Path $distDirectory 'Direct2dCad.zip'
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($publishDirectory,$portableZip,[IO.Compression.CompressionLevel]::Optimal,$false)

$checksums = foreach ($file in @(Get-Item -LiteralPath $releaseMsi,$portableZip)) {
    '{0}  {1}' -f (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant(),$file.Name
}
$checksums | Set-Content -LiteralPath (Join-Path $distDirectory 'SHA256SUMS.txt') -Encoding ascii

$releaseFiles = foreach ($file in @(Get-Item -LiteralPath $releaseMsi,$portableZip,(Join-Path $distDirectory 'SHA256SUMS.txt'))) {
    [ordered]@{name=$file.Name;bytes=$file.Length;sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash}
}
$manifest = [ordered]@{
    releaseVersion = $ReleaseVersion
    installerProductVersion = $wixVersion
    displayVersion = $ReleaseVersion
    runtime = 'win-x64 self-contained; .NET 10 included'
    icon = 'Direct2dCad.ico; embedded in application and registered in MSI'
    shortcuts = @('Start menu','Desktop')
    signed = $false
    files = @($releaseFiles)
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $releaseRoot 'release-manifest.json') -Encoding utf8

Write-Output "Release files: $distDirectory"
$releaseFiles | ConvertTo-Json -Depth 4
& (Join-Path $PSScriptRoot 'Validate-GitHubRelease.ps1') `
    -ReleaseDirectory $releaseRoot `
    -ExpectedReleaseVersion $ReleaseVersion `
    -ExpectedInstallerVersion $wixVersion
if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) { throw 'Release package self-validation failed.' }
