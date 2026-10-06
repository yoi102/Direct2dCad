# Document service lifetime

The application root no longer resolves disposable document view models. Each
`IEditorTabFactory.Create` call opens an independent DI scope and resolves its
`EditorTabViewModel` and `CadDocumentViewModel` from that scope. The returned tab
owns the scope; the factory retains neither the tab nor the scope.

## Ownership and entry points

- `EditorTabViewModel` and `CadDocumentViewModel` use scoped registrations.
- `IEditorTabFactory` is a singleton and depends only on `IServiceScopeFactory`.
- New drawings, templates, native-file opens, DXF import, recovery, and the CAD
  workspace adapter create tabs through the factory.
- Loading and initialization use the factory's optional initialization callback.
  A failed resolution or callback disposes the scope before propagating failure.
- Tab disposal releases subscriptions, save/recovery operations and render
  resources, then disposes the scope in a `finally` block. Reentrant disposal by
  DI is harmless because the tab and document both have disposal guards.
- The shell is disposable and releases all still-open documents at application
  shutdown. Explicitly closed documents are already released.
- WPF enables DI scope validation so accidental root resolution fails promptly.
  The unused editor/session registration chain is no longer installed by WPF;
  document creation has one production entry point.

The root provider does not track child scopes. A tab/scope reference cycle has no
root owner once the tab is closed and removed from the workspace. Disposing the
scope therefore permits collection of the document graph while the application
and other documents remain open.

## Verification

The final focused Release run on 2026-10-06 passed **36/36 tests**, with no failures
or skips. This includes the original 25 document/factory/close workflow cases,
existing OLE lifecycle checks, and five render-session failure regressions.

`EditorTabFactoryTests` exercises the production registrations and factory with
the platform-independent test render session. It checks document collection with
the root provider and another live tab still alive, independent scopes, rejection
of root resolution, failed initialization, failed dependency resolution, and
shutdown disposal. `MainViewModelLifecycleTests` also covers new/template creation
without global service location. `RenderSessionLifetimeTests` injects constructor,
OLE close/release, and renderer cleanup failures while tracking a real budget
lease. The session and lease are released even when earlier cleanup fails;
multiple failures are reported together only after all cleanup owners run.
Repeated disposal is harmless after failure. Existing close/save/recovery and OLE
workflows remain part of the focused validation command:

```powershell
dotnet test Direct2dCad.ViewModels.Tests/Direct2dCad.ViewModels.Tests.csproj -c Release --filter "FullyQualifiedName~RenderSessionLifetimeTests|FullyQualifiedName~EditorTabFactoryTests|FullyQualifiedName~MainViewModelLifecycleTests|FullyQualifiedName~RecoveryCloseWorkflowTests|FullyQualifiedName~CadOleSessionControllerTests" -m:1 -v minimal
```

The tests validate managed ownership and application behavior. They do not claim
to measure a large real drawing's GPU memory or a long-running interactive WPF
session. Such measurements are separate from this lifecycle regression check.
