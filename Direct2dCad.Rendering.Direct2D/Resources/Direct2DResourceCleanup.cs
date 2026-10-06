namespace Direct2dCad.Rendering.Direct2D.Resources;

internal static class Direct2DResourceCleanup
{
    internal static void Run(params Action[] releases)
    {
        List<Exception>? failures = null;
        foreach (var release in releases)
        {
            try { release(); }
            catch (Exception error) { (failures ??= []).Add(error); }
        }
        if (failures is not null)
            throw new AggregateException("One or more render resources failed to release.", failures);
    }
}
