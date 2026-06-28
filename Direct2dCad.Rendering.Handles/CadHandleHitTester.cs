using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Rendering.Handles;

public sealed class CadHandleHitTester
{
    public bool TryHitGrip(
        CadHandleScene scene,
        Func<CadPointD, CadPointD> worldToScreen,
        CadPointD screen,
        out CadGripHandle grip)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(worldToScreen);

        grip = default!;
        var closestDistanceSquared = double.PositiveInfinity;

        foreach (var item in scene.Items.OfType<CadGripHandle>())
        {
            var screenPosition = worldToScreen(item.Position);
            var distanceSquared = screenPosition.DistanceSquaredTo(screen);
            var hitRadius = Math.Max(item.Style.Size * 0.5 + 4.0, 7.0);
            var hitRadiusSquared = hitRadius * hitRadius;

            if (distanceSquared > hitRadiusSquared || distanceSquared >= closestDistanceSquared)
                continue;

            closestDistanceSquared = distanceSquared;
            grip = item;
        }

        return closestDistanceSquared < double.PositiveInfinity;
    }
}
