param([string]$OutputDirectory='TestResults/local-package',[string]$Version='0.1.3')
$ErrorActionPreference='Stop'
$repoRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$packageRoot=[IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDirectory))
if(Test-Path -LiteralPath $packageRoot){throw 'Use a new output directory so earlier evidence is preserved.'}
New-Item -ItemType Directory -Path $packageRoot | Out-Null
$appRoot=Join-Path $packageRoot 'app'
# The app project pins win-x64. Passing -r globally would change every library's
# restore graph and invalidate the neutral library lock files.
dotnet publish (Join-Path $repoRoot 'Direct2dCad.wpf/Direct2dCad.wpf.csproj') -c Release --self-contained true -p:RestoreLockedMode=true -p:Version=$Version -p:ContinuousIntegrationBuild=true -o $appRoot -v quiet
if($LASTEXITCODE -ne 0){throw 'Publish failed.'}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install-Local.ps1') -Destination $packageRoot
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Uninstall-Local.ps1') -Destination $packageRoot
Copy-Item -LiteralPath (Join-Path $repoRoot 'docs/DELIVERY.md') -Destination $packageRoot
$files=@(Get-ChildItem -LiteralPath $appRoot -File -Recurse | Sort-Object FullName | ForEach-Object {
    [ordered]@{path=[IO.Path]::GetRelativePath($appRoot,$_.FullName).Replace('\','/');bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
})
$commit=git -C $repoRoot rev-parse HEAD
$patch=git -C $repoRoot -c core.safecrlf=false diff --binary
$patchBytes=[Text.Encoding]::UTF8.GetBytes(($patch -join "`n"))
$deps=Get-Content -LiteralPath (Join-Path $appRoot 'Direct2dCad.deps.json') -Raw | ConvertFrom-Json
$sourceFiles=@(git -C $repoRoot ls-files --cached --others --exclude-standard | Sort-Object -Unique | ForEach-Object {
    $sourcePath=Join-Path $repoRoot $_
    if(Test-Path -LiteralPath $sourcePath -PathType Leaf){[ordered]@{path=$_;sha256=(Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash}}
})
$manifest=[ordered]@{appId='Direct2dCad';version=$Version;createdUtc=[DateTimeOffset]::UtcNow.ToString('O');sourceCommit=$commit;trackedDiffSha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($patchBytes));sourceFiles=$sourceFiles;sdk=(& dotnet --version);runtime='win-x64 self-contained';signed=$false;files=$files;dependencies=$deps.libraries}
$manifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $packageRoot 'manifest.json') -Encoding utf8
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zipPath=$packageRoot+'.zip'
[IO.Compression.ZipFile]::CreateFromDirectory($packageRoot,$zipPath)
Get-FileHash -LiteralPath $zipPath -Algorithm SHA256 | Format-List
Write-Output $packageRoot
