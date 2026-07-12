namespace Direct2dCad.Db.Cad;

public readonly record struct CadLineWeight
{
    public static readonly CadLineWeight ByLayer = new(-1);
    public static readonly CadLineWeight Default = new(1);

    public bool IsByLayer => Value < 0;
    public double Value { get; }
    public CadLineWeight(double Value)
    {
        if (double.IsNaN(Value) || double.IsInfinity(Value))
            throw new ArgumentOutOfRangeException(nameof(Value));

        if (Value < 0 && Value != -1)
            throw new ArgumentOutOfRangeException(nameof(Value), "Line weight must be non-negative or -1 for ByLayer.");

        this.Value = Value;
    }

    public override string ToString() => IsByLayer ? "ByLayer" : Value.ToString("0.###");
}
