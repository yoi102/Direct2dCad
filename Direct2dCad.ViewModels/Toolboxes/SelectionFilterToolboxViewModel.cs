using System.Collections.ObjectModel;
using AvalonDock.Core;
using AvalonDock.Mvvm.CommunityToolkit;
using CommunityToolkit.Mvvm.ComponentModel;
using Direct2dCad.ViewModels.Services.Events;
using Direct2dCad.ViewModels.Services.Interactions;
using Direct2dCad.ViewModels.Services.Platform;
using MessagePipe;

namespace Direct2dCad.ViewModels.Toolboxes;

public partial class SelectionFilterToolboxViewModel : ObservableToolboxBase, IDisposable
{
    private CadDocumentViewModel? _documentViewModel;
    private readonly IDisposable _selectionFilterChangedSubscription;
    private bool _isSynchronizing;

    public SelectionFilterToolboxViewModel(
        IToolboxIconProvider toolboxIconProvider,
        ISubscriber<CadSelectionFilterChangedMessage> selectionFilterChangedSubscriber)
    {
        Title = GetLocalizedText("SelectionFilter", "Selection Filter");
        Zone = DockZone.RightTop;
        Icon = toolboxIconProvider.Filter;
        Shortcut = "Ctrl+Shift+F";
        IsOpenByDefault = false;
        ContentId = Id = Guid.NewGuid().ToString();
        CanClose = false;

        foreach (var descriptor in CadSelectionEntityTypeCatalog.All)
        {
            Types.Add(new SelectionFilterTypeItemViewModel(
                descriptor.EntityType,
                GetLocalizedText(descriptor.ResourceKey, descriptor.FallbackName),
                OnTypeEnabledChanged));
        }

        _selectionFilterChangedSubscription = selectionFilterChangedSubscriber.Subscribe(
            OnSelectionFilterChanged);
    }

    [ObservableProperty]
    public partial string ContentId { get; private set; }

    [ObservableProperty]
    public partial bool? AreAllTypesEnabled { get; set; } = true;

    public ObservableCollection<SelectionFilterTypeItemViewModel> Types { get; } = [];

    public void Attach(CadDocumentViewModel? documentViewModel)
    {
        _documentViewModel = documentViewModel;
        _isSynchronizing = true;
        try
        {
            foreach (var item in Types)
            {
                item.IsEnabled = documentViewModel is null ||
                                 documentViewModel.IsEntityTypeSelectionEnabled(item.EntityType);
            }
        }
        finally
        {
            _isSynchronizing = false;
        }

        RefreshHeaderState();
    }

    partial void OnAreAllTypesEnabledChanged(bool? value)
    {
        if (_isSynchronizing || value is not { } enabled)
            return;

        _isSynchronizing = true;
        try
        {
            foreach (var item in Types)
                item.IsEnabled = enabled;
        }
        finally
        {
            _isSynchronizing = false;
        }

        if (_documentViewModel is not null)
            _documentViewModel.ApplyDisabledSelectionEntityTypeKeys(
                enabled
                    ? []
                    : CadSelectionEntityTypeCatalog.All.Select(descriptor => descriptor.Key));

        RefreshHeaderState();
    }

    private void OnTypeEnabledChanged(SelectionFilterTypeItemViewModel item)
    {
        if (_isSynchronizing)
            return;

        _documentViewModel?.SetEntityTypeSelectionEnabled(item.EntityType, item.IsEnabled);
        RefreshHeaderState();
    }

    private void OnSelectionFilterChanged(CadSelectionFilterChangedMessage message)
    {
        if (ReferenceEquals(message.DocumentViewModel, _documentViewModel))
            Attach(_documentViewModel);
    }

    public void Dispose()
    {
        _selectionFilterChangedSubscription.Dispose();
    }

    private void RefreshHeaderState()
    {
        var enabledCount = Types.Count(item => item.IsEnabled);
        var headerState = enabledCount switch
        {
            0 => false,
            _ when enabledCount == Types.Count => true,
            _ => (bool?)null
        };

        _isSynchronizing = true;
        try
        {
            AreAllTypesEnabled = headerState;
        }
        finally
        {
            _isSynchronizing = false;
        }
    }

    private static string GetLocalizedText(string resourceKey, string fallback)
    {
        return Direct2dCad.Lang.Strings.Strings.ResourceManager.GetString(
                   resourceKey,
                   System.Globalization.CultureInfo.CurrentUICulture) ?? fallback;
    }
}

public partial class SelectionFilterTypeItemViewModel : ObservableObject
{
    private readonly Action<SelectionFilterTypeItemViewModel> _changed;

    public SelectionFilterTypeItemViewModel(
        Type entityType,
        string displayName,
        Action<SelectionFilterTypeItemViewModel> changed)
    {
        EntityType = entityType ?? throw new ArgumentNullException(nameof(entityType));
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        _changed = changed ?? throw new ArgumentNullException(nameof(changed));
    }

    public Type EntityType { get; }

    public string DisplayName { get; }

    [ObservableProperty]
    public partial bool IsEnabled { get; set; } = true;

    partial void OnIsEnabledChanged(bool value)
    {
        _changed(this);
    }
}
