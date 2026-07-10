using Direct2dCad.Db;
using Direct2dCad.ViewModels.Services.ViewServices;

namespace Direct2dCad.ViewModels.Services.Events;

/// <summary>Published by the UI OLE host when an embedded server saves its object.</summary>
public sealed record CadOleObjectUpdatedMessage(
    Guid SessionId,
    EntityId EntityId,
    CadOleImportData? Data,
    bool IsPersisted);
