using Locora.Infrastructure.Persistence;
using Xunit;

namespace Locora.Infrastructure.Tests;

public sealed class JsonFileStoreTests
{
    [Fact]
    public async Task WriteAsync_CreatesDirectoryAndReadAsyncRestoresCamelCaseJson()
    {
        var root = Path.Combine(Path.GetTempPath(), "locora-json-store-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "nested", "settings.json");
        var store = new JsonFileStore();

        await store.WriteAsync(path, new SampleSettings("nginx", 80));

        var json = await File.ReadAllTextAsync(path);
        var settings = await store.ReadAsync<SampleSettings>(path);

        Assert.Contains("\"serviceName\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"ServiceName\"", json, StringComparison.Ordinal);
        Assert.Equal(new SampleSettings("nginx", 80), settings);
    }

    private sealed record SampleSettings(string ServiceName, int Port);
}
