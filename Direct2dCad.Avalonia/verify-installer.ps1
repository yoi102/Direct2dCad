param(
    [Parameter(Mandatory)][string]$InstallerPath,
    [Parameter(Mandatory)][string]$PackageDirectory,
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$ReleaseVersion = '0.1.6',
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$InstallerVersion = '1.0.6')

$ErrorActionPreference = 'Stop'
$msiPath = [IO.Path]::GetFullPath($InstallerPath)
$payload = [IO.Path]::GetFullPath($PackageDirectory)
$expectedUpgradeCode = '{A95257F5-8F20-4F70-A27A-13538A809B51}'
$installer = New-Object -ComObject WindowsInstaller.Installer
$database = $installer.OpenDatabase($msiPath,0)

function ReadMsiRows([string]$query,[int]$columnCount) {
    $view = $database.OpenView($query)
    [void]$view.Execute()
    $rows = [Collections.Generic.List[object]]::new()
    while ($record = $view.Fetch()) {
        $values = [Collections.Generic.List[string]]::new()
        for ($column=1; $column -le $columnCount; $column++) { $values.Add($record.StringData($column)) }
        $rows.Add([pscustomobject]@{Values=$values.ToArray()})
    }
    [void]$view.Close()
    return $rows.ToArray()
}

function ReadMsiProperty([string]$name) {
    $escaped = $name.Replace("'","''")
    $rows = @(ReadMsiRows "SELECT ``Value`` FROM ``Property`` WHERE ``Property`` = '$escaped'" 1)
    if ($rows.Count -ne 1) { throw "MSI property '$name' is missing or ambiguous." }
    return $rows[0].Values[0]
}

if ((ReadMsiProperty 'ProductVersion') -ne $InstallerVersion) { throw "MSI product version is not $InstallerVersion." }
if ((ReadMsiProperty 'ARPVERSION') -ne $ReleaseVersion) { throw "MSI display version is not $ReleaseVersion." }
if ((ReadMsiProperty 'ProductName') -ne 'Direct2dCad Avalonia') { throw 'MSI product identity is incorrect.' }
if ((ReadMsiProperty 'ARPPRODUCTICON') -ne 'Direct2dCadAvaloniaIcon') { throw 'The MSI does not use the Avalonia product icon.' }

$iconPath = Join-Path $payload 'Direct2dCad.ico'
$iconReader = [IO.BinaryReader]::new([IO.File]::OpenRead($iconPath))
try {
    if ($iconReader.ReadUInt16() -ne 0 -or $iconReader.ReadUInt16() -ne 1 -or $iconReader.ReadUInt16() -lt 1) {
        throw 'The installed application icon is invalid.'
    }
} finally { $iconReader.Dispose() }

$iconRows = @(ReadMsiRows "SELECT ``Name`` FROM ``Icon``" 1)
if (@($iconRows | Where-Object { $_.Values[0] -eq 'Direct2dCadAvaloniaIcon' }).Count -ne 1) {
    throw 'The MSI icon table does not contain the Avalonia application icon.'
}
$upgradeRows = @(ReadMsiRows "SELECT ``UpgradeCode`` FROM ``Upgrade``" 1)
if (@($upgradeRows | Where-Object { $_.Values[0] -eq $expectedUpgradeCode }).Count -lt 1) {
    throw 'The Avalonia MSI upgrade identity is missing.'
}
if (@($upgradeRows | Where-Object { $_.Values[0] -eq '{15045137-B490-4015-B5E5-C33A4E2341F2}' }).Count -ne 0) {
    throw 'The Avalonia MSI must remain independently installable beside the WPF edition.'
}

$dialogs = @(ReadMsiRows "SELECT ``Dialog`` FROM ``Dialog``" 1)
if (@($dialogs | Where-Object { $_.Values[0] -eq 'InstallDirDlg' }).Count -ne 1) {
    throw 'The MSI does not provide an installation location page.'
}
$controls = @(ReadMsiRows "SELECT ``Control``,``Type`` FROM ``Control`` WHERE ``Dialog_`` = 'InstallDirDlg'" 2)
if (@($controls | Where-Object { $_.Values[0] -eq 'Folder' -and $_.Values[1] -eq 'PathEdit' }).Count -ne 1 -or
    @($controls | Where-Object { $_.Values[0] -eq 'ChangeFolder' }).Count -ne 1) {
    throw 'The MSI installation location page is missing its editable path or browse button.'
}

$shortcuts = @(ReadMsiRows "SELECT ``Shortcut``,``Directory_``,``Name``,``Target``,``Icon_`` FROM ``Shortcut``" 5)
if ($shortcuts.Count -ne 2) { throw "Expected Start menu and desktop shortcuts; found $($shortcuts.Count)." }
$shortcutDirectories = @($shortcuts | ForEach-Object { $_.Values[1] })
foreach ($directory in @('ApplicationProgramsFolder','DesktopFolder')) {
    if ($directory -notin $shortcutDirectories) { throw "The MSI is missing its $directory shortcut." }
}
foreach ($shortcut in $shortcuts) {
    $shortcutName = $shortcut.Values[2].Split('|')[-1]
    if ($shortcutName -ne 'Direct2dCad Avalonia' -or
        $shortcut.Values[3] -ne '[INSTALLFOLDER]Direct2dCad.Avalonia.exe' -or
        $shortcut.Values[4] -ne 'Direct2dCadAvaloniaIcon') {
        throw 'An MSI shortcut has the wrong display name, target, or icon.'
    }
}

$expectedFiles = @(Get-ChildItem -LiteralPath $payload -File | ForEach-Object Name | Sort-Object)
$msiFiles = @(ReadMsiRows "SELECT ``FileName`` FROM ``File``" 1 | ForEach-Object { $_.Values[0].Split('|')[-1] } | Sort-Object)
if ($msiFiles.Count -ne $expectedFiles.Count -or (Compare-Object $expectedFiles $msiFiles).Count -ne 0) {
    throw 'The MSI payload file table differs from the validated NativeAOT payload.'
}
foreach ($required in @('Direct2dCad.Avalonia.exe','av_libglesv2.dll','libHarfBuzzSharp.dll','libSkiaSharp.dll')) {
    if ($required -notin $msiFiles) { throw "The MSI is missing NativeAOT payload file $required." }
}
if ('Direct2dCad.Ole.Native.dll' -in $msiFiles) { throw 'The MSI contains the removed C++ bridge.' }

$result = [ordered]@{
    releaseVersion = $ReleaseVersion
    installerProductVersion = ReadMsiProperty 'ProductVersion'
    displayVersion = ReadMsiProperty 'ARPVERSION'
    productName = ReadMsiProperty 'ProductName'
    runtime = 'NativeAOT; no separately installed .NET runtime required'
    installDirectorySelection = $true
    icon = ReadMsiProperty 'ARPPRODUCTICON'
    upgradeCode = $expectedUpgradeCode
    shortcuts = @($shortcuts | ForEach-Object { [ordered]@{directory=$_.Values[1];name=$_.Values[2].Split('|')[-1];target=$_.Values[3];icon=$_.Values[4]} })
    payloadFiles = $msiFiles.Count
    signed = $false
    msiSha256 = (Get-FileHash -LiteralPath $msiPath -Algorithm SHA256).Hash
}
$reportPath = Join-Path (Split-Path $msiPath -Parent) 'avalonia-installer-validation.json'
$result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $reportPath -Encoding utf8
$result | ConvertTo-Json -Depth 6
