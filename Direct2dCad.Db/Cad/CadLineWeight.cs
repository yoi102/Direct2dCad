namespace Direct2dCad.Db.Cad;

public readonly record struct CadLineWeight
{
    public const double DefaultMillimeters = 0.25;

    public static readonly CadLineWeight ByLayer = new(-1);
    public static readonly CadLineWeight Default = new(DefaultMillimeters);

    public bool IsByLayer => Value < 0;

    /// <summary>
    /// Gets the plotted line width in millimeters, or -1 for ByLayer.
    /// </summary>
    public double Value { get; }

    public double? ExplicitMillimeters => IsByLayer ? null : Value;

    public CadLineWeight(double Value)
    {
        if (double.IsNaN(Value) || double.IsInfinity(Value))
            throw new ArgumentOutOfRangeException(nameof(Value));

        if (Value < 0 && Value != -1)
            throw new ArgumentOutOfRangeException(nameof(Value), "Line weight must be millimeters or -1 for ByLayer.");

        this.Value = Value;
    }

    public override string ToString() => IsByLayer ? "ByLayer" : Value.ToString("0.###");
}
