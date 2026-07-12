namespace Direct2dCad.CommandLine;

public sealed class CadCommandLineRegistry
{
    private readonly Dictionary<string, ICadCommandLineHandler> _handlers =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ICadCommandLineHandler> _orderedHandlers = [];

    public IReadOnlyList<ICadCommandLineHandler> Handlers => _orderedHandlers;

    public void Register(ICadCommandLineHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        var names = EnumerateNames(handler.Descriptor).ToArray();
        foreach (var name in names)
        {
            if (_handlers.TryGetValue(name, out var existing) && !ReferenceEquals(existing, handler))
                throw new InvalidOperationException($"Command name or alias '{name}' is already registered.");
        }

        if (!_orderedHandlers.Contains(handler))
            _orderedHandlers.Add(handler);

        foreach (var name in names)
            _handlers[name] = handler;
    }

    public bool TryResolve(string name, out ICadCommandLineHandler? handler) =>
        _handlers.TryGetValue(CadCommandLineSyntax.NormalizeCommandName(name), out handler);

    public IReadOnlyList<string> Complete(string prefix, int maximumCount = 12)
    {
        var normalizedPrefix = CadCommandLineSyntax.NormalizeCommandName(prefix);
        return _orderedHandlers
            .Where(handler => EnumerateNames(handler.Descriptor)
                .Any(name => name.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase)))
            .Select(handler => handler.Descriptor.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, maximumCount))
            .ToArray();
    }

    private static IEnumerable<string> EnumerateNames(CadCommandLineDescriptor descriptor)
    {
        yield return CadCommandLineSyntax.NormalizeCommandName(descriptor.Name);
        foreach (var alias in descriptor.Aliases.Split(
                     ',',
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return CadCommandLineSyntax.NormalizeCommandName(alias);
        }
    }
}
