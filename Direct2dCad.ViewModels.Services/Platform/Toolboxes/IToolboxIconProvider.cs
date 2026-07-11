namespace Direct2dCad.ViewModels.Services.Platform;

public interface IToolboxIconProvider
{

    object Explorer { get; }
    object Layers { get; }
    object Terminal { get; }
    object Search { get; }
    object Filter { get; }
    object Git { get; }
    object Problems { get; }

}
