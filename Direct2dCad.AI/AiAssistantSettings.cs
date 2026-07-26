namespace Direct2dCad.AI;

public sealed class AiAssistantSettings
{
    public const string DefaultEndpoint = "http://localhost:1234/v1";
    public const int DefaultContextWindowTokens = 8192;
    public const int MinimumContextWindowTokens = 4096;
    public const int MaximumContextWindowTokens = 262144;

    public string Endpoint { get; set; } = DefaultEndpoint;
    public string Model { get; set; } = string.Empty;
    public double Temperature { get; set; } = 0.2;
    public bool EnableCadTools { get; set; } = true;
    public int ContextWindowTokens { get; set; } = DefaultContextWindowTokens;

    public void Normalize()
    {
        Endpoint = string.IsNullOrWhiteSpace(Endpoint)
            ? DefaultEndpoint
            : Endpoint.Trim().TrimEnd('/');
        Model = Model?.Trim() ?? string.Empty;
        Temperature = double.IsFinite(Temperature)
            ? Math.Clamp(Temperature, 0, 2)
            : 0.2;
        ContextWindowTokens = Math.Clamp(
            ContextWindowTokens,
            MinimumContextWindowTokens,
            MaximumContextWindowTokens);
    }

    public AiAssistantSettings Clone()
    {
        Normalize();
        return new AiAssistantSettings
        {
            Endpoint = Endpoint,
            Model = Model,
            Temperature = Temperature,
            EnableCadTools = EnableCadTools,
            ContextWindowTokens = ContextWindowTokens
        };
    }
}
