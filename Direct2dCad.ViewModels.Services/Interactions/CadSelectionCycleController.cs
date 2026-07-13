using Direct2dCad.Db;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Editor;
using Direct2dCad.Editor.Commands;

namespace Direct2dCad.ViewModels.Services.Interactions;

internal sealed class CadSelectionCycleController
{
    private EntityId[] _baseSelection = [];
    private EntityId[] _candidates = [];
    private CadSelectionMode _selectionMode = CadSelectionMode.Replace;
    private int _currentIndex;

    public void Begin(CadSelectionCycleSeed? seed)
    {
        if (seed is null || seed.Candidates.Count == 0)
        {
            Clear();
            return;
        }

        _baseSelection = seed.BaseSelection.Distinct().ToArray();
        _candidates = seed.Candidates.Distinct().ToArray();
        _selectionMode = seed.SelectionMode;
        _currentIndex = 0;
    }

    public bool Cycle(
        CadEditor editor,
        bool backwards,
        Func<CadEntity, bool> selectionFilter)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(selectionFilter);

        if (_candidates.Length == 0)
            return false;

        var direction = backwards ? -1 : 1;
        for (var offset = 1; offset <= _candidates.Length; offset++)
        {
            var index = PositiveModulo(_currentIndex + direction * offset, _candidates.Length);
            var candidate = _candidates[index];
            if (!editor.Document.TryGetEntity(candidate, out var entity) ||
                entity is null ||
                entity.IsErased ||
                !entity.IsVisible ||
                !selectionFilter(entity))
            {
                continue;
            }

            _currentIndex = index;
            editor.Execute(new SetSelectionCommand(
                CreateSelection(candidate),
                backwards ? "Selection Cycle Previous" : "Selection Cycle Next"));
            return true;
        }

        Clear();
        return false;
    }

    public void Clear()
    {
        _baseSelection = [];
        _candidates = [];
        _selectionMode = CadSelectionMode.Replace;
        _currentIndex = 0;
    }

    private IReadOnlyList<EntityId> CreateSelection(EntityId candidate)
    {
        if (_selectionMode == CadSelectionMode.Replace)
            return [candidate];

        var selection = _baseSelection.ToHashSet();
        switch (_selectionMode)
        {
            case CadSelectionMode.Add:
                selection.Add(candidate);
                break;
            case CadSelectionMode.Remove:
                selection.Remove(candidate);
                break;
            case CadSelectionMode.Toggle:
                if (!selection.Remove(candidate))
                    selection.Add(candidate);
                break;
        }

        return selection.ToArray();
    }

    private static int PositiveModulo(int value, int divisor)
    {
        var result = value % divisor;
        return result < 0 ? result + divisor : result;
    }
}
