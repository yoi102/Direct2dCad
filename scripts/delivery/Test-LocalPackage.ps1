param([Parameter(Mandatory)][string]$PackageDirectory,[Parameter(Mandatory)][string]$UpgradePackageDirectory,[string]$ResultsDirectory='TestResults/package-lifecycle')
$ErrorActionPreference='Stop'
$repoRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$testRoot=[IO.Path]::GetFullPath((Join-Path $repoRoot $ResultsDirectory))
if(Test-Path -LiteralPath $testRoot){throw 'Use a fresh test directory.'}
New-Item -ItemType Directory -Path $testRoot | Out-Null
$installRoot=Join-Path $testRoot 'installed'
$settings=Join-Path $testRoot 'user-settings.json';$recovery=Join-Path $testRoot 'recovery.d2cad'
[IO.File]::WriteAllText($settings,'settings-marker');[IO.File]::WriteAllText($recovery,'recovery-marker')
$rows=[Collections.Generic.List[object]]::new()
function Invoke-Checked($scriptPath,$arguments){
    & (Get-Command pwsh).Source -NoProfile -File $scriptPath @arguments
    if($LASTEXITCODE -ne 0){throw "Lifecycle script failed: $scriptPath"}
}
Invoke-Checked (Join-Path $PSScriptRoot 'Install-Local.ps1') @('-PackageDirectory',[IO.Path]::GetFullPath($PackageDirectory),'-InstallRoot',$installRoot)
$first=Get-Content -LiteralPath (Join-Path $installRoot 'current.json') -Raw | ConvertFrom-Json
$rows.Add(@{step='install';passed=$true;version=$first.version;appDirectory=$first.appDirectory})
Invoke-Checked (Join-Path $PSScriptRoot 'Install-Local.ps1') @('-PackageDirectory',[IO.Path]::GetFullPath($UpgradePackageDirectory),'-InstallRoot',$installRoot)
$second=Get-Content -LiteralPath (Join-Path $installRoot 'current.json') -Raw | ConvertFrom-Json
if($first.appDirectory -eq $second.appDirectory -or $first.version -eq $second.version){throw 'Upgrade did not activate a different version.'}
$rows.Add(@{step='upgrade';passed=$true;version=$second.version})
Invoke-Checked (Join-Path $PSScriptRoot 'Install-Local.ps1') @('-Rollback','-InstallRoot',$installRoot)
$restored=Get-Content -LiteralPath (Join-Path $installRoot 'current.json') -Raw | ConvertFrom-Json
if($restored.appDirectory -ne $first.appDirectory){throw 'Rollback did not restore the prior application.'}
$rows.Add(@{step='rollback';passed=$true;version=$restored.version})
$tampered=Join-Path $testRoot 'tampered-package';New-Item -ItemType Directory -Path $tampered | Out-Null
Copy-Item -LiteralPath (Join-Path $PackageDirectory 'app') -Destination (Join-Path $tampered 'app') -Recurse
$manifest=Get-Content -LiteralPath (Join-Path $PackageDirectory 'manifest.json') -Raw | ConvertFrom-Json
$manifest.files[0].sha256='0'*64;$manifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $tampered 'manifest.json') -Encoding utf8
& (Get-Command pwsh).Source -NoProfile -File (Join-Path $PSScriptRoot 'Install-Local.ps1') -PackageDirectory $tampered -InstallRoot $installRoot 2>&1 | Out-File -LiteralPath (Join-Path $testRoot 'tamper-rejection.txt')
if($LASTEXITCODE -eq 0){throw 'Tampered package was accepted.'}
if((Get-Content -LiteralPath (Join-Path $installRoot 'current.json') -Raw | ConvertFrom-Json).appDirectory -ne $first.appDirectory){throw 'Failed installation changed the active version.'}
$rows.Add(@{step='tamper rejection';passed=$true})
Invoke-Checked (Join-Path $PSScriptRoot 'Uninstall-Local.ps1') @('-InstallRoot',$installRoot)
if(Test-Path -LiteralPath $installRoot){throw 'Managed application directory remained after uninstall.'}
if([IO.File]::ReadAllText($settings) -ne 'settings-marker' -or [IO.File]::ReadAllText($recovery) -ne 'recovery-marker'){throw 'External settings/recovery markers changed.'}
$rows.Add(@{step='uninstall preserves external settings and recovery';passed=$true})
@{recordedUtc=[DateTimeOffset]::UtcNow.ToString('O');isolated=$true;rows=$rows;limitations='Same host, isolated paths; no shortcuts or associations. Does not replace clean-machine acceptance.'} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $testRoot 'lifecycle.json') -Encoding utf8
$rows | Format-Table
