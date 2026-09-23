using Wingman.Core.State;

namespace Wingman.Core.Tests;

public class StateStoreTests : IDisposable
{
    private readonly string _directoryPath =
        Path.Combine(Path.GetTempPath(), "wingman-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directoryPath))
        {
            Directory.Delete(_directoryPath, recursive: true);
        }
    }

    [Fact]
    public void Load_WithNoFile_ReturnsDefaultsAndDoesNotCreateFile()
    {
        var store = new StateStore(_directoryPath);

        var state = store.Load();

        Assert.Null(state.LastCheck);
        Assert.Equal(0, state.UpdatesAvailable);
        Assert.Empty(state.UpdateIds);
        Assert.Equal("", state.LastBatchResult);
        Assert.False(state.LastBatchFailed);
        Assert.Equal("", state.LastError);
        Assert.False(state.Running);
        Assert.Null(state.LastBatch);
        Assert.False(File.Exists(store.FilePath));
    }

    [Fact]
    public void SaveThenLoad_RoundTripsEveryProperty()
    {
        var store = new StateStore(_directoryPath);
        var lastCheck = new DateTimeOffset(2026, 9, 23, 6, 0, 0, TimeSpan.Zero);
        var lastBatch = new DateTimeOffset(2026, 9, 23, 3, 0, 0, TimeSpan.Zero);
        var state = new WingmanState
        {
            LastCheck = lastCheck,
            UpdatesAvailable = 3,
            UpdateIds = ["Git.Git", "7zip.7zip"],
            LastBatchResult = "3 of 3 updated",
            LastBatchFailed = false,
            LastError = "winget timed out",
            Running = true,
            LastBatch = lastBatch,
        };

        store.Save(state);
        var loaded = store.Load();

        Assert.Equal(lastCheck, loaded.LastCheck);
        Assert.Equal(3, loaded.UpdatesAvailable);
        Assert.Equal(["Git.Git", "7zip.7zip"], loaded.UpdateIds);
        Assert.Equal("3 of 3 updated", loaded.LastBatchResult);
        Assert.False(loaded.LastBatchFailed);
        Assert.Equal("winget timed out", loaded.LastError);
        Assert.True(loaded.Running);
        Assert.Equal(lastBatch, loaded.LastBatch);
    }

    [Fact]
    public void Load_WithInvalidJsonFile_ReturnsDefaults()
    {
        var store = new StateStore(_directoryPath);
        Directory.CreateDirectory(_directoryPath);
        File.WriteAllText(store.FilePath, "{ not valid json ");

        var state = store.Load();

        Assert.Null(state.LastCheck);
        Assert.Equal(0, state.UpdatesAvailable);
        Assert.Empty(state.UpdateIds);
        Assert.False(state.Running);
    }

    [Fact]
    public void Update_AppliesChangeAndPersistsIt()
    {
        var store = new StateStore(_directoryPath);

        store.Update(state =>
        {
            state.UpdatesAvailable = 2;
            state.UpdateIds = ["Git.Git"];
            state.Running = true;
        });

        var loaded = store.Load();
        Assert.Equal(2, loaded.UpdatesAvailable);
        Assert.Equal(["Git.Git"], loaded.UpdateIds);
        Assert.True(loaded.Running);

        store.Update(state => state.Running = false);

        Assert.False(store.Load().Running);
    }

    [Fact]
    public void LastWriteTime_IsNullBeforeFirstSaveAndSetAfter()
    {
        var store = new StateStore(_directoryPath);

        Assert.Null(store.LastWriteTime);

        store.Save(new WingmanState());

        Assert.NotNull(store.LastWriteTime);
    }
}
