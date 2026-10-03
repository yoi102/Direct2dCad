param([string]$PackageDirectory=$PSScriptRoot,[string]$InstallRoot=(Join-Path $env:LOCALAPPDATA 'Programs/Direct2dCad'),[switch]$Rollback,[switch]$CreateShortcut)
$ErrorActionPreference='Stop'
$installPath=[IO.Path]::GetFullPath($InstallRoot)
if($installPath -eq [IO.Path]::GetPathRoot($installPath) -or $installPath -eq [Environment]::GetFolderPath('UserProfile')){throw 'Unsafe installation root.'}
$pointerPath=Join-Path $installPath 'current.json'
$markerPath=Join-Path $installPath 'installation.json'
if(Test-Path -LiteralPath $installPath){
    if(!(Test-Path -LiteralPath $markerPath) -or (Get-Content -LiteralPath $markerPath -Raw | ConvertFrom-Json).appId -ne 'Direct2dCad'){throw 'Choose an empty new directory or a managed installation.'}
    if((Get-Item -LiteralPath $installPath).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Linked installation roots are unsupported.'}
}
function Assert-InstalledPointer($pointer){
    $versions=[IO.Path]::GetFullPath((Join-Path $installPath 'versions'))
    $app=[IO.Path]::GetFullPath($pointer.appDirectory)
    if($pointer.appId -ne 'Direct2dCad' -or !$app.StartsWith($versions+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or !(Test-Path -LiteralPath (Join-Path $app 'Direct2dCad.exe'))){throw 'Invalid installed version pointer.'}
    $installedManifest=Get-Content -LiteralPath (Join-Path (Split-Path -Parent $app) 'manifest.json') -Raw | ConvertFrom-Json
    foreach($entry in $installedManifest.files){
        $entryPath=[IO.Path]::GetFullPath((Join-Path $app $entry.path))
        if(!$entryPath.StartsWith($app+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or (Get-FileHash -LiteralPath $entryPath -Algorithm SHA256).Hash -ne $entry.sha256){throw 'Installed version failed hash verification.'}
    }
}
if($Rollback){
    $previous=Join-Path $installPath 'previous.json'
    if(!(Test-Path -LiteralPath $previous)){throw 'No previous version is available.'}
    $prior=Get-Content -LiteralPath $previous -Raw | ConvertFrom-Json
    Assert-InstalledPointer $prior
    $current=Get-Content -LiteralPath $pointerPath -Raw
    $previousContent=Get-Content -LiteralPath $previous -Raw
    [IO.File]::WriteAllText($pointerPath+'.tmp',$previousContent)
    [IO.File]::Move($pointerPath+'.tmp',$pointerPath,$true)
    [IO.File]::WriteAllText($previous,$current)
    Write-Output 'Rolled back. Settings and recovery files were preserved.'
    exit
}
$packagePath=[IO.Path]::GetFullPath($PackageDirectory)
$manifest=Get-Content -LiteralPath (Join-Path $packagePath 'manifest.json') -Raw | ConvertFrom-Json
if($manifest.appId -ne 'Direct2dCad' -or $manifest.version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+(?:-[A-Za-z0-9.]+)?$'){throw 'Invalid package identity.'}
$appPath=[IO.Path]::GetFullPath((Join-Path $packagePath 'app'))
if(@(Get-ChildItem -LiteralPath $appPath -File -Recurse).Count -ne @($manifest.files).Count){throw 'Package contains unlisted files.'}
foreach($file in $manifest.files){
    $source=[IO.Path]::GetFullPath((Join-Path $appPath $file.path))
    if(!$source.StartsWith($appPath+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Package path escapes app directory.'}
    if((Get-Item -LiteralPath $source).Length -ne $file.bytes -or (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $file.sha256){throw ('Package hash mismatch: '+$file.path)}
}
New-Item -ItemType Directory -Force -Path $installPath | Out-Null
[IO.File]::WriteAllText($markerPath,'{"appId":"Direct2dCad","schema":1}')
$versionsPath=[IO.Path]::GetFullPath((Join-Path $installPath 'versions'))
New-Item -ItemType Directory -Force -Path $versionsPath | Out-Null
$versionPath=[IO.Path]::GetFullPath((Join-Path $versionsPath ($manifest.version+'-'+[Guid]::NewGuid().ToString('N'))))
if(!$versionPath.StartsWith($versionsPath+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Version path escapes installation root.'}
New-Item -ItemType Directory -Path $versionPath | Out-Null
Copy-Item -LiteralPath $appPath -Destination (Join-Path $versionPath 'app') -Recurse
Copy-Item -LiteralPath (Join-Path $packagePath 'manifest.json') -Destination $versionPath
if(!(Test-Path -LiteralPath (Join-Path $versionPath 'app/Direct2dCad.exe'))){throw 'Application executable is missing.'}
if(Test-Path -LiteralPath $pointerPath){Assert-InstalledPointer (Get-Content -LiteralPath $pointerPath -Raw | ConvertFrom-Json);Copy-Item -LiteralPath $pointerPath -Destination (Join-Path $installPath 'previous.json') -Force}
$pointer=[ordered]@{appId='Direct2dCad';version=$manifest.version;appDirectory=(Join-Path $versionPath 'app')}
$pointer | ConvertTo-Json | Set-Content -LiteralPath ($pointerPath+'.tmp') -Encoding utf8
[IO.File]::Move($pointerPath+'.tmp',$pointerPath,$true)
$launcher=@'
$app=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'current.json') -Raw | ConvertFrom-Json
Start-Process -FilePath (Join-Path $app.appDirectory 'Direct2dCad.exe')
'@
[IO.File]::WriteAllText((Join-Path $installPath 'Launch.ps1'),$launcher)
if($CreateShortcut){
    $shell=New-Object -ComObject WScript.Shell
    $shortcut=$shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Desktop')) 'Direct2dCad.lnk'))
    $shortcut.TargetPath=(Get-Command pwsh).Source
    $shortcut.Arguments='-NoProfile -WindowStyle Hidden -File "'+(Join-Path $installPath 'Launch.ps1')+'"'
    $shortcut.Save()
}
Write-Output ('Installed '+$manifest.version+'. Settings and recovery files were preserved. File associations were not changed.')
