param(
    [Parameter(Mandatory)][string]$ReleaseDirectory,
    [string]$ExpectedReleaseVersion = '0.1.3',
    [string]$ExpectedInstallerVersion = '1.0.3'
)

$ErrorActionPreference = 'Stop'
$releaseRoot = [IO.Path]::GetFullPath($ReleaseDirectory)
$publishDirectory = Join-Path $releaseRoot 'publish'
$distDirectory = Join-Path $releaseRoot 'dist'
$msiPath = Join-Path $distDirectory 'Direct2dCad.msi'
$zipPath = Join-Path $distDirectory 'Direct2dCad.zip'

$runtimeConfig = Get-Content -LiteralPath (Join-Path $publishDirectory 'Direct2dCad.runtimeconfig.json') -Raw | ConvertFrom-Json
if ($runtimeConfig.runtimeOptions.framework -or $runtimeConfig.runtimeOptions.frameworks) {
    throw 'The app requires a machine-installed runtime.'
}
$includedFrameworks = @($runtimeConfig.runtimeOptions.includedFrameworks.name)
foreach ($requiredFramework in @('Microsoft.NETCore.App','Microsoft.WindowsDesktop.App')) {
    if ($requiredFramework -notin $includedFrameworks) { throw "The bundled runtime does not include $requiredFramework." }
}
foreach ($requiredFile in @('Direct2dCad.exe','Direct2dCad.dll','Direct2dCad.ico','hostfxr.dll','coreclr.dll','PresentationFramework.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $publishDirectory $requiredFile) -PathType Leaf)) {
        throw "The self-contained app is missing $requiredFile."
    }
}
$assemblyVersion = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $publishDirectory 'Direct2dCad.dll')).Version
$appVersion = if ($assemblyVersion.Revision -eq 0) { $assemblyVersion.ToString(3) } else { $assemblyVersion.ToString() }
if ($appVersion -ne $ExpectedReleaseVersion) { throw "Expected app version $ExpectedReleaseVersion, found $appVersion." }

$iconReader = [IO.BinaryReader]::new([IO.File]::OpenRead((Join-Path $publishDirectory 'Direct2dCad.ico')))
try {
    if ($iconReader.ReadUInt16() -ne 0 -or $iconReader.ReadUInt16() -ne 1 -or $iconReader.ReadUInt16() -lt 1) {
        throw 'Direct2dCad.ico is empty or is not a valid Windows icon.'
    }
}
finally { $iconReader.Dispose() }

$installer = New-Object -ComObject WindowsInstaller.Installer
$database = $installer.OpenDatabase($msiPath,0)
function ReadMsiRows([string]$query,[int]$columnCount) {
    $view = $database.OpenView($query)
    [void]$view.Execute()
    $rows = [Collections.Generic.List[object]]::new()
    while ($record = $view.Fetch()) {
        $row = [Collections.Generic.List[string]]::new()
        for ($column=1; $column -le $columnCount; $column++) { $row.Add($record.StringData($column)) }
        $rows.Add([pscustomobject]@{Values=$row.ToArray()})
    }
    [void]$view.Close()
    return $rows.ToArray()
}

function ReadMsiProperty([string]$name) {
    $quotedName = $name.Replace("'","''")
    $rows = @(ReadMsiRows "SELECT ``Value`` FROM ``Property`` WHERE ``Property`` = '$quotedName'" 1)
    if ($rows.Count -ne 1) { throw "MSI property '$name' is missing or ambiguous." }
    return $rows[0].Values[0]
}

if ((ReadMsiProperty 'ProductVersion') -ne $ExpectedInstallerVersion) { throw "The MSI upgrade version must be $ExpectedInstallerVersion." }
if ((ReadMsiProperty 'ARPVERSION') -ne $ExpectedReleaseVersion) { throw "The MSI display version must be $ExpectedReleaseVersion." }
if ((ReadMsiProperty 'ARPPRODUCTICON') -ne 'Direct2dCadIcon') { throw 'The MSI Add/Remove Programs icon is not configured.' }
$iconRows = @(ReadMsiRows "SELECT ``Name`` FROM ``Icon``" 1)
if (@($iconRows | Where-Object { $_.Values[0] -eq 'Direct2dCadIcon' }).Count -ne 1) { throw 'The application icon is missing from the MSI.' }
$shortcuts = @(ReadMsiRows "SELECT ``Shortcut``,``Directory_``,``Name``,``Target``,``Icon_`` FROM ``Shortcut``" 5)
if ($shortcuts.Count -ne 2) { throw "Expected Start menu and desktop shortcuts; found $($shortcuts.Count)." }
$shortcutDirectories = @($shortcuts | ForEach-Object { $_.Values[1] })
foreach ($requiredDirectory in @('ApplicationProgramsFolder','DesktopFolder')) {
    if ($requiredDirectory -notin $shortcutDirectories) { throw "The MSI is missing the $requiredDirectory shortcut." }
}
foreach ($shortcut in $shortcuts) {
    if ($shortcut.Values[3] -ne '[INSTALLFOLDER]Direct2dCad.exe' -or $shortcut.Values[4] -ne 'Direct2dCadIcon') {
        throw 'An MSI shortcut does not target the app using its configured icon.'
    }
}
$upgradeRows = @(ReadMsiRows "SELECT ``UpgradeCode`` FROM ``Upgrade``" 1)
if (@($upgradeRows | Where-Object { $_.Values[0] -eq '{15045137-B490-4015-B5E5-C33A4E2341F2}' }).Count -lt 1) {
    throw 'The MSI does not use the existing application upgrade code.'
}
$msiFiles = @(ReadMsiRows "SELECT ``FileName`` FROM ``File``" 1)
foreach ($runtimeFile in @('hostfxr.dll','coreclr.dll')) {
    $match = @($msiFiles | Where-Object { $_.Values[0].Split('|')[-1] -eq $runtimeFile })
    if ($match.Count -ne 1) { throw "The MSI does not contain bundled runtime file $runtimeFile." }
}

$checksums = @{}
foreach ($line in Get-Content -LiteralPath (Join-Path $distDirectory 'SHA256SUMS.txt')) {
    if ($line -match '^([0-9a-f]{64})  (.+)$') { $checksums[$Matches[2]] = $Matches[1] }
}
foreach ($package in @(Get-Item -LiteralPath $msiPath,$zipPath)) {
    $actualHash = (Get-FileHash -LiteralPath $package.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($checksums[$package.Name] -ne $actualHash) { throw "The checksum does not match $($package.Name)." }
}

[ordered]@{
    releaseVersion = $appVersion
    runtimeIncluded = $includedFrameworks
    installerProductVersion = ReadMsiProperty 'ProductVersion'
    displayVersion = ReadMsiProperty 'ARPVERSION'
    shortcuts = @($shortcuts | ForEach-Object { [ordered]@{directory=$_.Values[1];name=$_.Values[2];target=$_.Values[3];icon=$_.Values[4]} })
    productIcon = ReadMsiProperty 'ARPPRODUCTICON'
    installerSigned = $false
    packages = @(Get-Item -LiteralPath $msiPath,$zipPath | ForEach-Object {
        [ordered]@{name=$_.Name;bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
    })
} | ConvertTo-Json -Depth 6
