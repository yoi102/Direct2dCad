using Direct2dCad.AI;

namespace Direct2dCad.AI.Tests;

public sealed class JsonAiAssistantSettingsStoreTests
{
    [Fact]
    public void Clone_NormalizesCopyWithoutMutatingSource()
    {
        var source = new AiAssistantSettings
        {
            Endpoint = "http://localhost:1234/v1/",
            Model = " local-model ",
            CodexReasoningEffort = "HIGH"
        };

        var clone = source.Clone();

        Assert.Equal("http://localhost:1234/v1/", source.Endpoint);
        Assert.Equal(" local-model ", source.Model);
        Assert.Equal("HIGH", source.CodexReasoningEffort);
        Assert.Equal("http://localhost:1234/v1", clone.Endpoint);
        Assert.Equal("local-model", clone.Model);
        Assert.Equal("high", clone.CodexReasoningEffort);
    }

    [Fact]
    public void SaveAndLoad_RoundTripsNormalizedSettings()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"Direct2dCad.AI.Tests.{Guid.NewGuid():N}");
        var filePath = Path.Combine(directory, "settings.json");
        try
        {
            var store = new JsonAiAssistantSettingsStore(filePath);
            store.Save(new AiAssistantSettings
            {
                Provider = AiAssistantProvider.Codex,
                Endpoint = "http://localhost:1234/v1/",
                Model = " local-model ",
                Temperature = 9,
                EnableCadTools = false,
                ContextWindowTokens = int.MaxValue,
                CodexExecutablePath = " codex-custom ",
                CodexModel = " codex-model ",
                CodexReasoningEffort = "HIGH",
                CodexServiceTier = "FAST"
            });

            var loaded = store.Load();

            Assert.Equal(AiAssistantProvider.Codex, loaded.Provider);
            Assert.Equal("http://localhost:1234/v1", loaded.Endpoint);
            Assert.Equal("local-model", loaded.Model);
            Assert.Equal(2, loaded.Temperature);
            Assert.False(loaded.EnableCadTools);
            Assert.Equal(AiAssistantSettings.MaximumContextWindowTokens, loaded.ContextWindowTokens);
            Assert.Equal("codex-custom", loaded.CodexExecutablePath);
            Assert.Equal("codex-model", loaded.CodexModel);
            Assert.Equal("high", loaded.CodexReasoningEffort);
            Assert.Equal("fast", loaded.CodexServiceTier);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
