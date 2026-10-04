#requires -Version 7.4
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $AssemblyDirectory,
    [Parameter(Mandatory)][string] $DocumentPath,
    [Parameter(Mandatory)][string] $OutputDirectory,
    [string] $Label = 'current',
    [ValidateRange(1, 10)][int] $Iterations = 3,
    [ValidateRange(16, 256)][int] $FrameCount = 64
)

$ErrorActionPreference = 'Stop'
$bundle = (Resolve-Path -LiteralPath $AssemblyDirectory).Path
$source = (Resolve-Path -LiteralPath $DocumentPath).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($output) | Out-Null
$sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
Add-Type -AssemblyName PresentationCore
foreach ($name in @('Direct2dCad.Db', 'Direct2dCad.Rendering', 'Direct2dCad.IO', 'Direct2dCad.Benchmarks')) {
    $loaded = [Reflection.Assembly]::LoadFrom((Join-Path $bundle ($name + '.dll')))
    if ($name -eq 'Direct2dCad.Benchmarks') { $benchmarks = $loaded }
}
$factory = $benchmarks.GetType('Direct2dCad.Benchmarks.BenchmarkDocumentFactory')
$sessionType = $benchmarks.GetType('Direct2dCad.Benchmarks.BenchmarkRenderSession')
$fromDocument = $factory.GetMethod('FromDocument')
$storage = [Direct2dCad.IO.CadDocumentStorage]::new()
$rows = [Collections.Generic.List[object]]::new()
$frames = [Collections.Generic.List[object]]::new()
$images = [Collections.Generic.List[object]]::new()

function Get-Percentile([double[]] $Values, [double] $Fraction) {
    $ordered = @($Values | Sort-Object)
    return $ordered[[int][Math]::Floor(($ordered.Count - 1) * $Fraction)]
}

function Capture-Image($HostRenderer, [string] $Name) {
    $pixels = $HostRenderer.GetType().GetMethod('CaptureBackBufferPixels',
        [Reflection.BindingFlags]'Instance,NonPublic').Invoke($HostRenderer, @())
    $bitmap = [Windows.Media.Imaging.BitmapSource]::Create(1600, 900, 96, 96,
        [Windows.Media.PixelFormats]::Bgra32, $null, $pixels, 1600 * 4)
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $path = Join-Path $output ($Name + '.png')
    $stream = [IO.File]::Create($path)
    try { $encoder.Save($stream) } finally { $stream.Dispose() }
    $images.Add([ordered]@{ name = $Name; path = $path;
        pixel_sha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([byte[]]$pixels)) })
}

for ($iteration = 1; $iteration -le $Iterations; $iteration++) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $document = $storage.Load($source)
    $loadMs = $watch.Elapsed.TotalMilliseconds
    $data = $fromDocument.Invoke($null, [object[]]@($document))
    $openStarted = [Diagnostics.Stopwatch]::GetTimestamp()
    $session = [Activator]::CreateInstance($sessionType,
        [object[]]@($data, $true, $null, $null, $null, $null, 2, $true, 1600, 900))
    try {
        $renderer = $session.RenderHost
        $view = $session.Viewport
        $firstMs = $null
        do {
            $pending = $renderer.PrepareRenderCacheStep()
            if (!$renderer.HasPresentedScene -and $renderer.IsInitialViewReady) {
                $renderer.Render([Direct2dCad.Rendering.CadRenderInvalidation]::Full, $true)
                if ($renderer.HasPresentedScene) {
                    $firstMs = [Diagnostics.Stopwatch]::GetElapsedTime($openStarted).TotalMilliseconds
                }
            }
            if ([Diagnostics.Stopwatch]::GetElapsedTime($openStarted).TotalSeconds -gt 120) {
                throw 'Resource preparation exceeded 120 seconds.'
            }
            [Threading.Thread]::Yield() | Out-Null
        } while ($pending)
        $completeMs = [Diagnostics.Stopwatch]::GetElapsedTime($openStarted).TotalMilliseconds
        $session.WarmUp()
        $zoom = $view.Zoom
        $offset = $view.Offset
        if ($iteration -eq 1) { Capture-Image $renderer 'fitted' }
        $dirty = [Direct2dCad.Rendering.CadRenderInvalidation]::FromScreenRect(
            [Direct2dCad.Rendering.CadScreenRect]::new(680, 390, 240, 120))
        foreach ($mode in @('warm-full', 'overlay-dirty', 'pan', 'zoom')) {
            $view.SetView($zoom, $offset)
            $renderer.Render([Direct2dCad.Rendering.CadRenderInvalidation]::Full, $true)
            for ($index = 0; $index -lt $FrameCount; $index++) {
                $allocated = [GC]::GetAllocatedBytesForCurrentThread()
                $started = [Diagnostics.Stopwatch]::GetTimestamp()
                if ($mode -eq 'pan') {
                    $view.PanScreen([Direct2dCad.Db.Geometry.CadVectorD]::new(
                        $(if ($index -lt $FrameCount / 2) { 4 } else { -4 }), 0))
                }
                if ($mode -eq 'zoom') {
                    $view.ZoomAt([Direct2dCad.Db.Geometry.CadPointD]::new(800, 450),
                        $(if ($index -lt $FrameCount / 2) { 1.1 } else { 1 / 1.1 }))
                }
                if ($mode -eq 'overlay-dirty') { $renderer.Render($dirty, $false) }
                else { $renderer.Render([Direct2dCad.Rendering.CadRenderInvalidation]::Full, $true) }
                $duration = [Diagnostics.Stopwatch]::GetElapsedTime($started).TotalMilliseconds
                $allocated = [GC]::GetAllocatedBytesForCurrentThread() - $allocated
                $frames.Add([ordered]@{ iteration = $iteration; mode = $mode; step = $index + 1;
                    zoom = $view.Zoom; elapsed_ms = $duration; allocated_bytes = $allocated;
                    statistics = $renderer.RenderStatistics })
                if ($iteration -eq 1 -and $mode -eq 'zoom' -and $index + 1 -eq $FrameCount / 2) {
                    Capture-Image $renderer 'zoomed'
                }
            }
        }
        $row = [ordered]@{ iteration = $iteration; load_ms = $loadMs;
            first_visible_present_ms = $firstMs; all_resources_ready_ms = $completeMs;
            entity_count = $document.Entities.Count; using_warp = $renderer.UsingWarp;
            types = @($document.Entities.Values | Group-Object { $_.GetType().Name } |
                ForEach-Object { [ordered]@{ type = $_.Name; count = $_.Count } }) }
        $rows.Add($row)
        $row | ConvertTo-Json -Depth 4 -Compress | Write-Output
    } finally { $session.Dispose() }
}

$summaries = foreach ($mode in @('warm-full', 'overlay-dirty', 'pan', 'zoom')) {
    $measured = @($frames | Where-Object { $_.mode -eq $mode })
    [ordered]@{ mode = $mode; frame_count = $measured.Count;
        median_ms = Get-Percentile @($measured.elapsed_ms) .5;
        mean_ms = ($measured.elapsed_ms | Measure-Object -Average).Average;
        p95_ms = Get-Percentile @($measured.elapsed_ms) .95;
        max_ms = ($measured.elapsed_ms | Measure-Object -Maximum).Maximum;
        median_allocated_bytes = Get-Percentile @($measured.allocated_bytes) .5;
        realization_builds = ($measured.statistics.GeometryRealizationBuildCount | Measure-Object -Sum).Sum }
}
if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $sourceHash) {
    throw 'The input document changed while the benchmark was running.'
}
$report = [ordered]@{ label = $Label; recorded_at = [DateTimeOffset]::UtcNow;
    document_path = $source; document_sha256 = $sourceHash; file_bytes = (Get-Item -LiteralPath $source).Length;
    rendering_assembly_sha256 = (Get-FileHash (Join-Path $bundle 'Direct2dCad.Rendering.Direct2D.dll')).Hash;
    machine = $env:COMPUTERNAME; runtime = [Environment]::Version.ToString(); surface = '1600x900';
    rows = $rows.ToArray(); summaries = @($summaries); images = $images.ToArray(); frames = $frames.ToArray();
    limitations = 'Native offscreen Direct2D host, LOD enabled. CPU wall-clock includes EndDraw and a shared-texture Present callback, not GPU timestamps or WPF input/composition latency. First Present starts after decode and fitted-document descriptor creation; includes viewport, index, device and resource preparation. Thread.Yield owner loop; no WPF Dispatcher pacing. The dirty overlay scenario has no selected entities or transients. Source documents are read-only. Run each assembly version in a fresh STA PowerShell process. PowerShell invocation overhead is present equally in both versions.' }
$report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $output 'measurements.json') -Encoding utf8
$summaries | ConvertTo-Json -Depth 3 -Compress | Write-Output
