param(
    [Parameter(Mandatory)][string]$PackageDirectory,
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$ReleaseVersion = '0.1.6',
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$InstallerVersion = '1.0.6')

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$artifactRoot = Join-Path $repo '.artifacts'
$portable = (Resolve-Path -LiteralPath $PackageDirectory).Path
$validation = Get-Content -LiteralPath (Join-Path $portable 'validation.json') -Raw | ConvertFrom-Json
$portableExe = Join-Path $portable 'Direct2dCad.Avalonia.exe'
$actualExeHash = (Get-FileHash -LiteralPath $portableExe -Algorithm SHA256).Hash
if (!$validation.NativeAot -or $validation.Failure -or $validation.ExecutableSha256 -ne $actualExeHash) {
    throw 'The MSI input is not a verified NativeAOT package.'
}

$payload = Join-Path $artifactRoot ('avalonia-msi-payload-' + [Guid]::NewGuid().ToString('N'))
$output = Join-Path $artifactRoot 'avalonia-msi-build'
New-Item -ItemType Directory -Force -Path $payload,$output | Out-Null

$modules = @(Get-ChildItem -LiteralPath $portable -File | Where-Object { $_.Extension -in '.exe','.dll' })
if (@($modules | Where-Object Name -EQ 'Direct2dCad.Ole.Native.dll').Count -ne 0) {
    throw 'The Avalonia MSI input contains the retired custom native bridge.'
}
if (@($modules | Where-Object Name -EQ 'Direct2dCad.Avalonia.exe').Count -ne 1) {
    throw 'The Avalonia MSI input is missing its native executable.'
}
foreach ($module in $modules) { Copy-Item -LiteralPath $module.FullName -Destination $payload }

$supportFiles = @(
    'README.md',
    'AVALONIA-WINDOWS.md',
    'AVALONIA-UI-PARITY.md',
    'AVALONIA-UI-LIBRARIES.md',
    'THIRD-PARTY-UI-LICENSES.txt',
    'THIRD-PARTY-INTEROP-LICENSES.txt')
foreach ($name in $supportFiles) {
    $source = Join-Path $portable $name
    if (Test-Path -LiteralPath $source -PathType Leaf) { Copy-Item -LiteralPath $source -Destination $payload }
}
Copy-Item -LiteralPath (Join-Path $repo 'Direct2dCad.wpf/Direct2dCad.ico') -Destination $payload

$project = Join-Path $repo 'installer/Direct2dCad.Avalonia.Msi/Direct2dCad.Avalonia.Msi.wixproj'
& dotnet restore $project --locked-mode -p:ContinuousIntegrationBuild=true -v minimal
if ($LASTEXITCODE -ne 0) { throw 'Locked WiX restore failed.' }
& dotnet build $project -c Release --no-restore -p:RestoreLockedMode=true `
    "-p:Version=$InstallerVersion" "-p:ReleaseVersion=$ReleaseVersion" `
    "-p:PayloadDirectory=$payload" "-p:ReleaseArtifactsDirectory=$output" -v minimal
if ($LASTEXITCODE -ne 0) { throw 'Avalonia MSI build failed.' }

$builtMsi = Join-Path $output 'Direct2dCad.Avalonia.msi'
$releaseMsi = Join-Path $artifactRoot "Direct2dCad-Avalonia-$ReleaseVersion-win-x64.msi"
if (!(Test-Path -LiteralPath $builtMsi -PathType Leaf)) { throw 'WiX did not produce the Avalonia MSI.' }
Copy-Item -LiteralPath $builtMsi -Destination $releaseMsi -Force

& (Join-Path $PSScriptRoot 'verify-installer.ps1') -InstallerPath $releaseMsi `
    -PackageDirectory $payload -ReleaseVersion $ReleaseVersion -InstallerVersion $InstallerVersion
if (!$?) { throw 'Avalonia MSI validation failed.' }
Write-Host "Avalonia MSI: $releaseMsi"
