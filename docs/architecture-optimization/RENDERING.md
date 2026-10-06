# Rendering boundaries and resource policy

Implemented 2026-10-06. No publication or performance-speedup claim is implied.

## Boundaries and ownership

- `ICadRenderSessionFactory` creates a document-owned `ICadRenderSession`; `CadDocumentViewModel` disposes it. WPF registers the Direct2D factory and binds its native image surface. Native surface/device types stay in the platform/backend code.
- ViewModels and ViewModels.Services reference the neutral Rendering contracts rather than Rendering.Direct2D. Text metrics use `ICadTextMetrics`; OLE draw/release payloads are neutral owned-data contracts. Tests can construct document VMs without loading a native renderer.
- Construction failure rolls back the owned renderer and any created OLE controller. Disposal attempts all native owners and releases budget leases even if a resource cleanup fails.
- Measuring text returns `CadTextBoundsMeasurement` values without mutating document entities. `CadEditor.ApplyDerivedTextBounds` validates the measured text/height/style inputs, applies bounds, and publishes `IsDerivedGeometry` changes so spatial indexes, dependent blocks, and rendering update together. Derived changes do not reset the active drawing tool or interrupt editor history coalescing. Dirty-set/combined changes keep the derived flag only when every nonempty constituent is derived.

## Resource policy

The WPF factory shares `CadRenderResourceBudget.Shared`: a default 1 GiB process allowance and 512 MiB maximum document allowance. These are injectable policy values, not preallocations. Open documents share the process allowance; each document divides its allowance among its foreground renderer and active parallel workers. Closing a document/worker returns the allowance. Quota-change notifications schedule owner-thread cache maintenance.

The backend bounds the **estimated retained optimization-cache bytes at completed frame and cache-preparation boundaries**. Accounting includes scene tiles, command lists, block definition lists, geometry realizations, hatch tiles, image bitmaps, OLE tiles, transient image bitmaps, grid tiles, and transient group command lists. Document/process diagnostics sum all registered main/worker renderers. `RENDERSTATS` displays these aggregate estimates and allowances, along with grid/transient breakdowns.

On pressure, dependent retained command lists are released before bitmap leases. Images are evicted in least-recent-use order and reloaded on demand; the document's source pixels remain intact. Large visible images can still render as temporary frame resources. Immediate rendering preserves appearance when retained caches are evicted. Rebuilding the same over-budget cache is suppressed for that scene/view; edits, a different scene/view, device resets, or a larger allowance permit preparation again.

This is not a hard process working-set or driver VRAM cap. Native command-list and realization sizes are estimates. In-flight drawing/preparation, base entity geometries, brushes/text layouts, CPU document/source buffers, and presentation/frame surfaces are outside this optimization-cache allowance. Idle documents converge to a changed quota when their scheduled owner-thread maintenance runs. Those limits are explicit so the diagnostics are not mistaken for physical memory measurements.

## Verification

- Neutral quota/lifetime tests: 2 passed. Covers multiple documents, main/worker sharing, aggregate estimates, close/release, and invalid disposed registration.
- Render-session boundary tests: 2 passed. Covers injected fake renderer, VM ownership, and no Direct2D/Vortice/SharpGen assembly references in ViewModels and Services.
- Native focused regression run: 6 passed. Covers repeated exact pixel comparisons after image eviction in serial, shared-context, and multiple-device modes; grid/transient-list budget accounting; retained-cache recovery after changing scenes; and pure text measurement followed by Editor application.
- Native tests use the host's Automatic device selection. A forced-WARP fixture could not create the existing D3D9 shared presentation surface on this machine (E_INVALIDARG before drawing), so these results do not certify WARP support.
- Focused implementation run: **214 passed, 1 skipped, 215 total** (Debug, 2026-10-06). The coordinating final Release run also passed **214, with 1 skipped**; exact TRX and binary hashes are in [the final validation record](../validation/2026-10-06/architecture/README.md).

No monitor FPS, input latency, GPU utilization, or physical display performance was measured. The final solution build passed with zero warnings/errors; the seven selected real-window workflows also passed, as recorded in the final validation record.
