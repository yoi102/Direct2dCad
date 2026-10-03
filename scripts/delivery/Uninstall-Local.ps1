param([string]$InstallRoot=(Join-Path $env:LOCALAPPDATA 'Programs/Direct2dCad'))
$ErrorActionPreference='Stop'
$installPath=[IO.Path]::GetFullPath($InstallRoot)
if($installPath -eq [IO.Path]::GetPathRoot($installPath) -or $installPath -eq [Environment]::GetFolderPath('UserProfile')){throw 'Unsafe uninstall root.'}
$pointer=Get-Content -LiteralPath (Join-Path $installPath 'current.json') -Raw | ConvertFrom-Json
if($pointer.appId -ne 'Direct2dCad'){throw 'This is not a managed Direct2dCad installation.'}
if((Get-Content -LiteralPath (Join-Path $installPath 'installation.json') -Raw | ConvertFrom-Json).appId -ne 'Direct2dCad'){throw 'Installation marker is missing.'}
if((Get-ChildItem -LiteralPath $installPath -Force | Where-Object Name -NotIn @('versions','current.json','previous.json','installation.json','Launch.ps1')).Count -gt 0){throw 'Installation contains unrelated files; refusing recursive removal.'}
if(@(Get-ChildItem -LiteralPath $installPath -Force -Recurse | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count -gt 0){throw 'Installation contains links; refusing recursive removal.'}
$versions=[IO.Path]::GetFullPath((Join-Path $installPath 'versions'))
$appPath=[IO.Path]::GetFullPath($pointer.appDirectory)
if(!$appPath.StartsWith($versions+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Installation marker escapes the version directory.'}
if(Get-Process -Name Direct2dCad -ErrorAction SilentlyContinue){throw 'Close Direct2dCad before uninstalling.'}
Remove-Item -LiteralPath $installPath -Recurse -Force
Write-Output 'Application files removed. User settings and recovery files were preserved.'
