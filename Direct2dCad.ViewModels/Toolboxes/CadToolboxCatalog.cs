using System.Diagnostics.CodeAnalysis;

namespace Direct2dCad.ViewModels.Toolboxes;

/// <summary>Receives the toolbox view-model types in display order; each host maps them onto its docking library.</summary>
public interface ICadToolboxRegistrar
{
    void Add<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TToolbox>()
        where TToolbox : CadToolboxViewModelBase;
}

/// <summary>The toolboxes both desktop hosts show. Order is the sidebar button order.</summary>
public static class CadToolboxCatalog
{
    public static void Register(ICadToolboxRegistrar registrar)
    {
        ArgumentNullException.ThrowIfNull(registrar);
        registrar.Add<DocumentExplorerToolboxViewModel>();
        registrar.Add<LayersToolboxViewModel>();
        registrar.Add<BlocksToolboxViewModel>();
        registrar.Add<EntityPropertiesToolboxViewModel>();
        registrar.Add<DrawingRecoveryToolboxViewModel>();
        registrar.Add<EntitySearchToolboxViewModel>();
        registrar.Add<SelectionFilterToolboxViewModel>();
        registrar.Add<CommandLineToolboxViewModel>();
        registrar.Add<MessageToolboxViewModel>();
        registrar.Add<AiAssistantToolboxViewModel>();
    }
}
