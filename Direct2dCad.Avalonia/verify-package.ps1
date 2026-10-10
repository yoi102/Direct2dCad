param([Parameter(Mandatory)][string]$PackageDirectory)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$portable = (Resolve-Path -LiteralPath $PackageDirectory).Path
$archive = Join-Path $repo '.artifacts/Direct2dCad-Avalonia-win-x64.zip'
$manifestPath = Join-Path $portable 'manifest.json'
$manifest = @(Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json)
if ($manifest.File -contains 'Direct2dCad.Ole.Native.dll') { throw 'Package contains the removed custom C++ bridge.' }
$hashes = @{}
foreach ($entry in $manifest) {
    if ([IO.Path]::GetFileName($entry.File) -ne $entry.File) { throw 'Manifest contains a non-local filename.' }
    $file = Join-Path $portable $entry.File
    $actualHash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
    if ($actualHash -ne $entry.Sha256) { throw "Portable checksum mismatch: $($entry.File)" }
    $hashes[$entry.File] = $actualHash
}
$hashes['manifest.json'] = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash
if ((Get-ChildItem -LiteralPath $portable -File).Count -ne $hashes.Count) { throw 'Portable contains files outside its manifest.' }
$evidence = Get-Content -LiteralPath (Join-Path $portable 'validation.json') -Raw | ConvertFrom-Json
if (!$evidence.NativeAot -or $evidence.Failure -or $evidence.ExecutableSha256 -ne $hashes['Direct2dCad.Avalonia.exe'] -or
    $evidence.Gpu.ExecutableSha256 -ne $evidence.ExecutableSha256 -or $evidence.Gpu.Failure -or $evidence.Gpu.SharedFrames -lt 12 -or $evidence.Gpu.CpuReadbacks -ne 0) {
    throw 'Validation does not certify this module set.'
}
$zip = [IO.Compression.ZipFile]::OpenRead($archive)
try {
    $files = @($zip.Entries | Where-Object Name)
    if ($files.Count -ne $hashes.Count) { throw 'ZIP contains a different file count.' }
    foreach ($entry in $files) {
        $expectedName = [IO.Path]::GetFileName($portable) + '/' + $entry.Name
        if ($entry.FullName.Replace('\', '/') -ne $expectedName -or !$hashes.ContainsKey($entry.Name)) { throw "Unexpected ZIP member: $($entry.FullName)" }
        $stream = $entry.Open()
        try { $memberHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) } finally { $stream.Dispose() }
        if ($memberHash -ne $hashes[$entry.Name]) { throw "ZIP checksum mismatch: $($entry.Name)" }
    }
} finally { $zip.Dispose() }
$zipHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
$result = [ordered]@{ TimestampUtc = [DateTimeOffset]::UtcNow; PackageDirectory = $portable; ExecutableSha256 = $evidence.ExecutableSha256; ZipSha256 = $zipHash; PortableFilesChecked = $manifest.Count; ZipMembersChecked = $files.Count; NativeChecks = $evidence.Passed.Count; SharedGpuFrames = $evidence.Gpu.SharedFrames; Failure = $null }
$result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $repo '.artifacts/avalonia-package-verification.json') -Encoding utf8
@("$zipHash  Direct2dCad-Avalonia-win-x64.zip", "$($evidence.ExecutableSha256)  $([IO.Path]::GetFileName($portable))/Direct2dCad.Avalonia.exe") | Set-Content -LiteralPath (Join-Path $repo '.artifacts/Direct2dCad-Avalonia-SHA256SUMS.txt') -Encoding utf8
$result | ConvertTo-Json
