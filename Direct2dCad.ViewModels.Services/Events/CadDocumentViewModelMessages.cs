using Direct2dCad.Db;
using Direct2dCad.Editor.Commands;

namespace Direct2dCad.ViewModels.Services.Events;

public interface ICadDocumentViewModelMessageSource;

public interface IEditorTabDocumentSummaryMessageSource;

public sealed record CadDocumentInteractionStateChangedMessage(
    ICadDocumentViewModelMessageSource DocumentViewModel,
    bool ClearBlockDefinitionSelection = false);

public sealed record CadDocumentViewSettingsChangedMessage(ICadDocumentViewModelMessageSource DocumentViewModel);

public sealed record CadSelectionFilterChangedMessage(ICadDocumentViewModelMessageSource DocumentViewModel);

public sealed record CadBlockDefinitionSelectionChangedMessage(
    ICadDocumentViewModelMessageSource DocumentViewModel,
    BlockId? BlockId);

public sealed record CadCommandActivityMessage(
    ICadDocumentViewModelMessageSource DocumentViewModel,
    string DocumentName,
    CadCommandActivity Activity);

public sealed record CadInteractionActivityMessage(
    ICadDocumentViewModelMessageSource DocumentViewModel,
    string DocumentName,
    string Name);

public sealed record EditorTabDocumentSummaryChangedMessage(IEditorTabDocumentSummaryMessageSource EditorTabViewModel);
