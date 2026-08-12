using Direct2dCad.AI.Contracts;
using Direct2dCad.AI.LmStudio;

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
    public void Clone_NormalizesLegacyFlexServiceTierToDefault()
    {
        var source = new AiAssistantSettings { CodexServiceTier = "flex" };

        var clone = source.Clone();

        Assert.Equal("flex", source.CodexServiceTier);
        Assert.Equal(AiAssistantSettings.DefaultCodexServiceTier, clone.CodexServiceTier);
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

    [Fact]
    public void Load_ReturnsDefaultsWhenFileIsMissingOrMalformed()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"Direct2dCad.AI.Tests.{Guid.NewGuid():N}");
        var filePath = Path.Combine(directory, "settings.json");
        try
        {
            var store = new JsonAiAssistantSettingsStore(filePath);

            var missing = store.Load();
            Assert.Equal(AiAssistantSettings.DefaultEndpoint, missing.Endpoint);

            Directory.CreateDirectory(directory);
            File.WriteAllText(filePath, "{ not valid json }");

            var malformed = store.Load();
            Assert.Equal(AiAssistantSettings.DefaultEndpoint, malformed.Endpoint);
            Assert.Equal(AiAssistantProvider.LmStudio, malformed.Provider);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Normalize_ClampsInvalidValuesAndUsesSafeDefaults()
    {
        var settings = new AiAssistantSettings
        {
            Endpoint = " ",
            Temperature = double.NaN,
            ContextWindowTokens = int.MinValue,
            CodexExecutablePath = " ",
            CodexReasoningEffort = "unsupported",
            CodexServiceTier = "unsupported"
        };

        settings.Normalize();

        Assert.Equal(AiAssistantSettings.DefaultEndpoint, settings.Endpoint);
        Assert.Equal(0.2, settings.Temperature);
        Assert.Equal(AiAssistantSettings.MinimumContextWindowTokens, settings.ContextWindowTokens);
        Assert.Equal("codex", settings.CodexExecutablePath);
        Assert.Equal("medium", settings.CodexReasoningEffort);
        Assert.Equal(AiAssistantSettings.DefaultCodexServiceTier, settings.CodexServiceTier);
    }
}
