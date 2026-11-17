using System.Collections.Concurrent;
using System.ComponentModel;
using System.Resources;

namespace Direct2dCad.Client.Common.Attributes;

public class LocalizedDescriptionAttribute : DescriptionAttribute
{
    // Attribute instances are recreated on every reflection lookup; share one manager per resource type.
    private static readonly ConcurrentDictionary<Type, ResourceManager> ResourceManagers = new();

    private readonly ResourceManager resourceManager;
    private readonly string resourceKey;

    public LocalizedDescriptionAttribute(string resourceKey, Type resourceType)
    {
        ArgumentNullException.ThrowIfNull(resourceKey);
        ArgumentNullException.ThrowIfNull(resourceType);
        resourceManager = ResourceManagers.GetOrAdd(resourceType, static type => new ResourceManager(type));
        this.resourceKey = resourceKey;
    }

    public override string Description
    {
        get
        {
            string? description = resourceManager.GetString(resourceKey);
            return string.IsNullOrWhiteSpace(description) ? string.Format("[[{0}]]", resourceKey) : description;
        }
    }
}
