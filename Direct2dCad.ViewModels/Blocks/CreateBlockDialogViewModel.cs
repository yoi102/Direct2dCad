using CommunityToolkit.Mvvm.ComponentModel;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Lang.Strings;
using Direct2dCad.ViewModels.Services.Platform;

namespace Direct2dCad.ViewModels.Blocks;

public partial class CreateBlockDialogViewModel : ObservableObject
{
    private readonly HashSet<string> _unavailableNames;

    public CreateBlockDialogViewModel(CreateBlockDialogRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _unavailableNames = request.UnavailableNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Name = request.SuggestedName;
        BasePointX = request.SuggestedBasePoint.X;
        BasePointY = request.SuggestedBasePoint.Y;
        SelectedEntityCount = request.SelectedEntityCount;
        NotifyValidationChanged();
    }

    public string Title => Strings.CreateBlock;
    public int SelectedEntityCount { get; }
    public bool IsValid => ValidationError is null;

    public string? ValidationError
    {
        get
        {
            var name = Name?.Trim();
            if (string.IsNullOrEmpty(name))
                return Localize("CreateBlockNameRequired");
            if (_unavailableNames.Contains(name))
                return Localize("BlockNameAlreadyExists");
            if (!double.IsFinite(BasePointX) || !double.IsFinite(BasePointY))
                return Localize("CreateBlockBasePointInvalid");
            return null;
        }
    }

    [ObservableProperty] public partial string Name { get; set; } = string.Empty;
    [ObservableProperty] public partial double BasePointX { get; set; }
    [ObservableProperty] public partial double BasePointY { get; set; }
    public CreateBlockDialogResult CreateResult()
    {
        if (!IsValid)
            throw new InvalidOperationException("The block settings are invalid.");

        return new CreateBlockDialogResult(
            Name.Trim(),
            new CadPointD(BasePointX, BasePointY));
    }

    partial void OnNameChanged(string value) => NotifyValidationChanged();
    partial void OnBasePointXChanged(double value) => NotifyValidationChanged();
    partial void OnBasePointYChanged(double value) => NotifyValidationChanged();
    private void NotifyValidationChanged()
    {
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(ValidationError));
    }

    private static string Localize(string key) =>
        Strings.ResourceManager.GetString(key, Strings.Culture) ?? key;
}
