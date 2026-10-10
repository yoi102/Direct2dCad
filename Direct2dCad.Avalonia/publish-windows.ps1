param(
    [string]$ReleaseVersion = '0.1.6',
    [string]$InstallerVersion = '1.0.6',
    [switch]$SkipValidation,
    [switch]$HeadlessValidation)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$artifactRoot = Join-Path $repo '.artifacts'
$publish = Join-Path $artifactRoot ('avalonia-native-' + [Guid]::NewGuid().ToString('N'))
$validation = Join-Path $artifactRoot 'avalonia-native-validation'
$archive = Join-Path $artifactRoot 'Direct2dCad-Avalonia-win-x64.zip'
New-Item -ItemType Directory -Force -Path $artifactRoot, $validation | Out-Null
Push-Location $repo
try {
    & dotnet restore (Join-Path $PSScriptRoot 'Direct2dCad.Avalonia.csproj') --locked-mode -p:ContinuousIntegrationBuild=true -v minimal
    if ($LASTEXITCODE -ne 0) { throw 'Locked Avalonia restore failed.' }
    # The client declares win-x64 itself. A global -r would also rewrite every shared project's lock file.
    & dotnet publish (Join-Path $PSScriptRoot 'Direct2dCad.Avalonia.csproj') -c Release --no-restore -p:ContinuousIntegrationBuild=true `
        -p:Version=$ReleaseVersion -p:InformationalVersion=$ReleaseVersion -p:AssemblyVersion=$ReleaseVersion -p:FileVersion=$ReleaseVersion `
        -p:UseSharedCompilation=false -nodeReuse:false -m:1 -p:TrimmerSingleWarn=false -o $publish *> (Join-Path $artifactRoot 'avalonia-native-publish.log')
    if ($LASTEXITCODE -ne 0) { throw 'NativeAOT publication failed. See .artifacts/avalonia-native-publish.log.' }
    if (Test-Path -LiteralPath (Join-Path $publish 'Direct2dCad.Ole.Native.dll')) { throw 'Publication still contains the removed custom C++ bridge.' }
    if (!$SkipValidation) {
        $oldSettings = $env:DIRECT2DCAD_SETTINGS_DIRECTORY
        $oldRecovery = $env:DIRECT2DCAD_RECOVERY_DIRECTORY
        try {
            $run = Join-Path $validation ('runs/' + [Guid]::NewGuid().ToString('N'))
            New-Item -ItemType Directory -Path $run | Out-Null
            $env:DIRECT2DCAD_SETTINGS_DIRECTORY = Join-Path $run 'settings'
            $env:DIRECT2DCAD_RECOVERY_DIRECTORY = Join-Path $run 'recovery'
            $report = Join-Path $run 'report.json'
            $smokeFlag = if ($HeadlessValidation) { '--smoke-test-headless' } else { '--smoke-test' }
            $process = Start-Process -FilePath (Join-Path $publish 'Direct2dCad.Avalonia.exe') -ArgumentList @($smokeFlag, ('"' + $report + '"')) -WindowStyle Hidden -PassThru
            if (!$process.WaitForExit(60000)) { $process.Kill(); throw 'Native smoke exceeded 60 seconds.' }
            if ($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $report)) { throw "Native smoke failed. Report: $report" }
            $result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
            $hash = (Get-FileHash -LiteralPath (Join-Path $publish 'Direct2dCad.Avalonia.exe') -Algorithm SHA256).Hash
            if (!$result.NativeAot -or $result.Failure -or $result.ExecutableSha256 -ne $hash) { throw 'Native smoke evidence does not match the published executable.' }
            $gpuReport = Join-Path $run 'gpu-report.json'
            $gpuProcess = Start-Process -FilePath (Join-Path $publish 'Direct2dCad.Avalonia.exe') -ArgumentList @('--gpu-test-offscreen', ('"' + $gpuReport + '"')) -WindowStyle Hidden -PassThru
            if (!$gpuProcess.WaitForExit(60000)) { $gpuProcess.Kill(); throw 'Offscreen GPU validation exceeded 60 seconds.' }
            if ($gpuProcess.ExitCode -ne 0 -or !(Test-Path -LiteralPath $gpuReport)) { throw "Offscreen GPU validation failed. Report: $gpuReport" }
            $gpuResult = Get-Content -LiteralPath $gpuReport -Raw | ConvertFrom-Json
            if (!$gpuResult.NativeAot -or $gpuResult.Failure -or $gpuResult.ExecutableSha256 -ne $hash -or $gpuResult.SharedFrames -lt 12 -or $gpuResult.CpuReadbacks -ne 0 -or $gpuResult.ApplicationWindowsCreated) { throw 'Shared GPU evidence is incomplete or differs from the published executable.' }
            Copy-Item -LiteralPath $gpuReport -Destination (Join-Path $validation 'gpu-report.json') -Force
            Copy-Item -LiteralPath $report -Destination (Join-Path $validation 'report.json') -Force
            Write-Host "Native smoke: $($result.Passed.Count) checks passed; SHA256 $hash"
            Write-Host "Offscreen GPU: $($gpuResult.SharedFrames) actual Avalonia imported frames; zero CPU readbacks."
        } finally { $env:DIRECT2DCAD_SETTINGS_DIRECTORY = $oldSettings; $env:DIRECT2DCAD_RECOVERY_DIRECTORY = $oldRecovery }
    }
    # Keep previous running builds intact. Each module set gets its own output folder.
    $modules = Get-ChildItem -LiteralPath $publish -File | Where-Object { $_.Extension -in '.exe', '.dll' } | Sort-Object Name
    $moduleHashes = ($modules | ForEach-Object { $_.Name + ':' + (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }) -join "`n"
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $packageId = ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($moduleHashes)))).Replace('-', '').Substring(0, 12).ToLowerInvariant() } finally { $sha.Dispose() }
    $portable = Join-Path $artifactRoot ('Direct2dCad-Avalonia-win-x64-' + $packageId)
    New-Item -ItemType Directory -Force -Path $portable | Out-Null
    foreach ($module in $modules) {
        $destination = Join-Path $portable $module.Name
        if (!(Test-Path -LiteralPath $destination)) { Copy-Item -LiteralPath $module.FullName -Destination $destination }
        elseif ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $module.FullName -Algorithm SHA256).Hash) { throw "Portable module differs: $destination" }
    }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination $portable
    Copy-Item -LiteralPath (Join-Path $repo 'docs/AVALONIA-WINDOWS.md') -Destination $portable
    Copy-Item -LiteralPath (Join-Path $repo 'docs/AVALONIA-UI-PARITY.md') -Destination $portable
    Copy-Item -LiteralPath (Join-Path $repo 'docs/AVALONIA-UI-LIBRARIES.md') -Destination $portable
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'THIRD-PARTY-UI-LICENSES.txt') -Destination $portable
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'THIRD-PARTY-INTEROP-LICENSES.txt') -Destination $portable
    if (!$SkipValidation) {
        foreach ($preview in @('preview-light.png', 'preview-dark.png', 'dialog-light.png', 'dialog-dark.png','dock-auto-hide.png','dock-split.png','dock-guides.png','dock-drag.png','dock-floating-borderless.png','exit-confirmation.png','exit-unsaved.png','print-light.png','print-dark.png','radial-overlay.png','canvas-context-menu.png','canvas-scrollbar-pressed.png','print-scrollbar-pressed.png','property-compact.png','choices-light.png','choices-dark.png','choices-color-popup.png','choices-list-popup.png','file-menu-compact.png','layer-menu-compact.png','explicit-color-source.png','chinese-menu.png','chinese-unsaved-dialog.png')) {
            $previewPath = Join-Path $run $preview
            if (Test-Path -LiteralPath $previewPath) { Copy-Item -LiteralPath $previewPath -Destination $portable -Force }
        }
        $documentedEvidence = Join-Path $repo 'docs/avalonia-native-verification.json'
        $documentedResult = if (Test-Path -LiteralPath $documentedEvidence) { Get-Content -LiteralPath $documentedEvidence -Raw | ConvertFrom-Json } else { $null }
        if ($documentedResult.ExecutableSha256 -eq $hash) {
            Copy-Item -LiteralPath $documentedEvidence -Destination (Join-Path $portable 'validation.json') -Force
            $uiEvidencePath = Join-Path $repo 'docs/avalonia-ui-parity-verification.json'
            if (Test-Path -LiteralPath $uiEvidencePath) {
                $uiEvidence = Get-Content -LiteralPath $uiEvidencePath -Raw | ConvertFrom-Json
                if ($uiEvidence.ExecutableSha256 -eq $hash) {
                    Copy-Item -LiteralPath $uiEvidencePath -Destination $portable -Force
                    # Preserve the reviewed screenshot/report pair, even on a fresh run of the same EXE.
                    foreach ($previewEvidence in $uiEvidence.Screenshots) {
                        $reviewedPreview = Join-Path $repo $previewEvidence.SourcePath
                        if ([IO.Path]::GetFileName($previewEvidence.File) -ne $previewEvidence.File -or
                            (Get-FileHash -LiteralPath $reviewedPreview -Algorithm SHA256).Hash -ne $previewEvidence.Sha256) { throw 'Reviewed UI screenshot differs from its evidence.' }
                        Copy-Item -LiteralPath $reviewedPreview -Destination (Join-Path $portable $previewEvidence.File) -Force
                    }
                }
            }
        } else {
            $result | Add-Member -NotePropertyName Gpu -NotePropertyValue $gpuResult -Force
            $result | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $portable 'validation.json') -Encoding utf8
        }
    }
    Get-ChildItem -LiteralPath $portable -File | Where-Object { $_.Name -ne 'manifest.json' } | ForEach-Object { [PSCustomObject]@{ File = $_.Name; Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $portable 'manifest.json') -Encoding utf8
    Compress-Archive -LiteralPath $portable -DestinationPath $archive -Force
    if (!$SkipValidation) { & (Join-Path $PSScriptRoot 'verify-package.ps1') -PackageDirectory $portable }
    if (!$SkipValidation) {
        $releaseZip = Join-Path $artifactRoot "Direct2dCad-Avalonia-$ReleaseVersion-win-x64.zip"
        Copy-Item -LiteralPath $archive -Destination $releaseZip -Force
        & (Join-Path $PSScriptRoot 'build-installer.ps1') -PackageDirectory $portable -ReleaseVersion $ReleaseVersion -InstallerVersion $InstallerVersion
        if (!$?) { throw 'Avalonia MSI package was not created.' }
        $installer = Join-Path $artifactRoot "Direct2dCad-Avalonia-$ReleaseVersion-win-x64.msi"
        if (!(Test-Path -LiteralPath $installer -PathType Leaf)) { throw 'Avalonia MSI package was not created.' }
        $checksums = foreach ($file in @(Get-Item -LiteralPath $releaseZip,$installer)) {
            '{0}  {1}' -f (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant(),$file.Name
        }
        $checksumsPath = Join-Path $artifactRoot "SHA256SUMS-Avalonia-$ReleaseVersion.txt"
        $checksums | Set-Content -LiteralPath $checksumsPath -Encoding ascii
        Write-Host "ZIP: $releaseZip"
        Write-Host "MSI: $installer"
        Write-Host "SHA256: $checksumsPath"
    } else {
        Write-Warning 'Validation was skipped; no versioned release assets or MSI were created.'
    }
    Write-Host "Portable: $portable"
    if (!$SkipValidation) { Write-Host "ZIP: $releaseZip" }
} finally { Pop-Location }

