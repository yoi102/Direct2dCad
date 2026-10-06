# Command execution, history, and gestures

Updated: 2026-10-06

## Changes

Document and editor histories remain separate. Document edits continue through
`ICadCommand` and `CadDocumentCommandManager`; viewport and selection operations
continue through `ICadEditorCommand` and `CadEditorCommandManager`.

- Document `Execute`, `ExecuteInBatch`, and `ExecuteRange` share execution guards
  and retained-payload accounting. Batch commands no longer fall back to a fixed
  256-byte history cost, and `ExecuteRange` rejects compatibility read-only
  documents before invoking commands. Undo and redo also reject a document that
  has become read-only. A query-only atomic scope remains allowed; edits inside
  it must use the guarded batch entry point.
- Both history managers now apply their configured command-count and byte
  limits. Limits expire complete older batches. The newest command or batch is
  retained even if it exceeds the soft budget, preserving undo and atomic
  rollback. Zero disables the corresponding limit. Changing settings applies
  on the next successful command in the owning manager.
- The retained-payload estimator supports editor commands and traverses private
  fields declared by base classes. Previous selection arrays stored by
  `SelectionCommandBase` therefore count toward the editor budget. Estimates
  describe retained managed command payload, not process RSS, the document's
  own retained entities, or GPU/native resources.
- A pan gesture coalesces successful live updates into one history entry using
  a per-gesture ID. Each movement still publishes editor-state updates for
  rendering. A new gesture, another editor command, undo/redo, or a document
  change breaks coalescing. The original undo state and combined redo movement
  are preserved; history cost is refreshed after a merge.

## Failure contract

`ICadCommand` now documents that validation must complete before document
mutation, or the command must compensate its own changes before throwing.
Managers can reverse already completed commands, but cannot infer an undo
operation for an arbitrary command that threw halfway through execution.

`DeleteLayerCommand` now invokes the document's protected-layer validation and
deletion before removing the layer priority. Attempting to delete the default
layer preserves its explicit priority, history position, and change version.
The adjacent deletion commands for styles, line types, hatch patterns, and
blocks already validate their protected/referenced objects before document
mutation. Reviewing multi-property edits found another concrete partial-write
case: `SetBlockReferenceTransformCommand` now validates position, rotation, and
both scale axes before changing any component.

This is not a universal snapshot transaction around all commands. New command
implementations must honor the failure contract and provide an appropriate
regression test for fallible multi-step edits.

## Verification

- `Direct2dCad.Editor.Tests`: **87 / 87 passed** in Release. Regression coverage
  includes all four document execution routes on read-only documents, query-only
  atomic scopes, retained image payload and whole-batch expiry, failed protected
  layer deletion, independent editor/document histories, inherited selection
  payloads, and editor batch retention. The 10 `DerivedTextBoundsTests` cases
  additionally verify accepted metrics update text and block-reference spatial
  indices without changing undo/redo history, stale/deleted text measurements
  are ignored, empty/derived/real change combinations preserve the correct
  origin through dirty-set drains and deferred publication, and derived
  geometry does not split an active pan gesture's undo entry.
- `Direct2dCad.Commands.Tests`: **106 / 106 passed** in Release, including invalid
  block scales that must leave the entire transform unchanged.
- `Direct2dCad.ViewModels.Services.Tests`: **99 / 99 passed** in Release. Pan
  tests cover per-move notification, one undo per gesture, redo, gesture
  boundaries, history expiry, interleaved commands, stationary input, and
  branching after undo.

No graphical/manual interaction acceptance is claimed by these unit tests.

## Dependency regression gate

`scripts/testing/Test-Architecture.ps1` reads the solution and project XML before
the regression build. It checks that solution projects and project references
exist, that referenced projects belong to the solution, and that the graph has
no cycles. It checks transitive dependencies as well as direct edges:

- Core projects cannot depend on Application, presentation projects, WPF/OLE,
  or the Direct2D implementation.
- Application cannot depend on ViewModels, ViewModels.Services, Client.Common,
  WPF/OLE, or the concrete Direct2D implementation.
- ViewModels and ViewModels.Services cannot depend on WPF/OLE or the concrete
  Direct2D implementation.
- Protected layers cannot enable WPF/Windows Forms or reach projects declaring
  native Vortice/SharpDX packages or Windows Desktop framework dependencies.

The gate conservatively checks all declared conditional project edges. The
repository currently declares literal references in project files; nonliteral
reference paths fail with an explanation rather than being silently skipped.
This is a project dependency check, not a full MSBuild import evaluator or a
source-code heuristic for DI ownership. Scope ownership remains covered by the
factory/lifetime tests.

The current solution passed with **41 projects and 122 project references**.
`Test-ArchitectureGuard.ps1` also passed **11 isolated graph fixtures**, covering
a valid graph and deliberate missing/unlisted references, a cycle, forbidden
direct/transitive edges, a native package, and WPF opt-in. The fixture runner
uses and removes only a fresh verified temporary directory. Both checks run
from `Run-Regression.ps1`, including its `-NoBuild` mode.
