using Sideport.Orchestrator;

namespace Sideport.Orchestrator.Tests;

/// <summary>
/// Coverage for the durable input-IPA store that keeps the scheduler able to
/// re-sign unattended after a restart wipes the ephemeral upload path.
/// </summary>
public class IpaStoreTests
{
    private static string TempRoot() =>
        Path.Combine(Path.GetTempPath(), "sideport-ipastore-tests", Guid.NewGuid().ToString("N"));

    private static async Task<string> WriteFileAsync(byte[] bytes)
    {
        string path = Path.Combine(TempRoot(), "CertCountdown.ipa");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, bytes);
        return path;
    }

    [Fact]
    public async Task StoreAsync_CopiesIntoStore_AndSurvivesSourceDeletion()
    {
        string root = TempRoot();
        string source = await WriteFileAsync([1, 2, 3, 4]);

        var store = new IpaStore(root);
        string durable = await store.StoreAsync("UDID-1", "com.example.certcountdown", source);

        Assert.True(File.Exists(durable));
        Assert.StartsWith(Path.GetFullPath(root), durable);

        File.Delete(source); // the ephemeral upload path is wiped (pod restart)

        Assert.True(File.Exists(durable));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, await File.ReadAllBytesAsync(durable));
    }

    [Fact]
    public async Task StoreAsync_WhenSourceIsAlreadyTheDurableCopy_IsNoOp()
    {
        var store = new IpaStore(TempRoot());
        string source = await WriteFileAsync([9]);

        string durable = await store.StoreAsync("UDID-1", "com.example.app", source);
        // Re-register pointing at the durable copy itself: must not throw or truncate.
        string again = await store.StoreAsync("UDID-1", "com.example.app", durable);

        Assert.Equal(durable, again);
        Assert.True(File.Exists(durable));
        Assert.Equal(new byte[] { 9 }, await File.ReadAllBytesAsync(durable));
    }

    [Fact]
    public async Task StoreAsync_NewVersion_OverwritesPriorCopy()
    {
        var store = new IpaStore(TempRoot());

        string v1 = await store.StoreAsync("UDID-1", "com.example.app", await WriteFileAsync([1]));
        string v2 = await store.StoreAsync("UDID-1", "com.example.app", await WriteFileAsync([2, 2]));

        Assert.Equal(v1, v2);
        Assert.Equal(new byte[] { 2, 2 }, await File.ReadAllBytesAsync(v2));
    }

    [Fact]
    public async Task Remove_DeletesStoredIpa()
    {
        var store = new IpaStore(TempRoot());
        string durable = await store.StoreAsync("UDID-1", "com.example.app", await WriteFileAsync([7]));

        store.Remove("UDID-1", "com.example.app");

        Assert.False(File.Exists(durable));
    }

    [Fact]
    public void PathFor_RejectsTraversalIdentifiers() =>
        Assert.Throws<ArgumentException>(() => new IpaStore(TempRoot()).PathFor("..", "x"));
}
