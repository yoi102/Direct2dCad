using Direct2dCad.AI;

namespace Direct2dCad.AI.Tests;

public sealed class JsonAiAssistantSettingsStoreTests
{
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
                Endpoint = "http://localhost:1234/v1/",
                Model = " local-model ",
                Temperature = 9,
                EnableCadTools = false,
                ContextWindowTokens = int.MaxValue
            });

            var loaded = store.Load();

            Assert.Equal("http://localhost:1234/v1", loaded.Endpoint);
            Assert.Equal("local-model", loaded.Model);
            Assert.Equal(2, loaded.Temperature);
            Assert.False(loaded.EnableCadTools);
            Assert.Equal(AiAssistantSettings.MaximumContextWindowTokens, loaded.ContextWindowTokens);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
