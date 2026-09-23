using Wingman.Core.History;
using Wingman.Core.Models;

namespace Wingman.Core.Tests;

public class HistoryStoreTests : IDisposable
{
    private readonly string _directoryPath =
        Path.Combine(Path.GetTempPath(), "wingman-tests", Guid.NewGuid().ToString("N"));

    private static readonly DateTimeOffset BaseTimestamp = new(2026, 3, 1, 9, 30, 0, TimeSpan.Zero);

    public void Dispose()
    {
        if (Directory.Exists(_directoryPath))
        {
            Directory.Delete(_directoryPath, recursive: true);
        }
    }

    private static OperationResult MakeResult(int exitCode = 0, params string[] log) =>
        new(exitCode, Succeeded: exitCode == 0, Duration: TimeSpan.FromSeconds(5), Log: log);

    [Fact]
    public void Append_ThenList_ReturnsEntryWithEqualFields()
    {
        var store = new HistoryStore(_directoryPath);
        var arguments = new[] { "install", "--id", "Package.Id", "--silent" };

        var appended = store.Append(
            BaseTimestamp,
            "install",
            "Package.Id",
            "Package Name",
            MakeResult(0, "line one", "line two"),
            arguments,
            batchId: null);

        var listed = Assert.Single(store.List());

        Assert.Equal(appended.Timestamp, listed.Timestamp);
        Assert.Equal("install", listed.Operation);
        Assert.Equal("Package.Id", listed.PackageId);
        Assert.Equal("Package Name", listed.PackageName);
        Assert.Equal(0, listed.ExitCode);
        Assert.True(listed.Succeeded);
        Assert.Equal(TimeSpan.FromSeconds(5), listed.Duration);
        Assert.Equal(arguments, listed.Arguments);
        Assert.Equal(appended.LogFileName, listed.LogFileName);
        Assert.Null(listed.BatchId);
    }

    [Fact]
    public void List_WithTwoAppends_ReturnsNewestFirst()
    {
        var store = new HistoryStore(_directoryPath);

        var older = store.Append(
            BaseTimestamp, "install", "Older.Id", "Older", MakeResult(), [], batchId: null);
        var newer = store.Append(
            BaseTimestamp.AddSeconds(1), "install", "Newer.Id", "Newer", MakeResult(), [], batchId: null);

        var listed = store.List();

        Assert.Equal(2, listed.Count);
        Assert.Equal(newer.PackageId, listed[0].PackageId);
        Assert.Equal(older.PackageId, listed[1].PackageId);
    }

    [Fact]
    public void ReadLog_RoundTripsTheLoggedLines()
    {
        var store = new HistoryStore(_directoryPath);

        var entry = store.Append(
            BaseTimestamp, "upgrade", "Package.Id", "Package Name",
            MakeResult(0, "downloading...", "installing...", "done"), [], batchId: null);

        var log = store.ReadLog(entry);

        Assert.Equal("downloading...\ninstalling...\ndone\n", log);
    }

    [Fact]
    public void ReadLog_WithMissingLogFile_ReturnsEmptyString()
    {
        var store = new HistoryStore(_directoryPath);
        var entry = store.Append(BaseTimestamp, "install", "Package.Id", "Package", MakeResult(), [], batchId: null);
        File.Delete(Path.Combine(_directoryPath, entry.LogFileName));

        Assert.Equal("", store.ReadLog(entry));
    }

    [Fact]
    public void Append_WithSameSecondCollision_AddsNumericSuffix()
    {
        var store = new HistoryStore(_directoryPath);

        var first = store.Append(BaseTimestamp, "install", "Package.Id", "Package", MakeResult(), [], batchId: null);
        var second = store.Append(BaseTimestamp, "install", "Package.Id", "Package", MakeResult(), [], batchId: null);
        var third = store.Append(BaseTimestamp, "install", "Package.Id", "Package", MakeResult(), [], batchId: null);

        Assert.DoesNotContain("-2", first.LogFileName);
        Assert.Contains("-2.log", second.LogFileName);
        Assert.Contains("-3.log", third.LogFileName);
        Assert.Equal(3, store.List().Count);
    }

    [Fact]
    public void Delete_RemovesBothFiles()
    {
        var store = new HistoryStore(_directoryPath);
        var entry = store.Append(BaseTimestamp, "install", "Package.Id", "Package", MakeResult(), [], batchId: null);
        var jsonPath = Path.ChangeExtension(Path.Combine(_directoryPath, entry.LogFileName), ".json");
        var logPath = Path.Combine(_directoryPath, entry.LogFileName);

        store.Delete(entry);

        Assert.False(File.Exists(jsonPath));
        Assert.False(File.Exists(logPath));
        Assert.Empty(store.List());
    }

    [Fact]
    public void Delete_WhenFilesAlreadyMissing_DoesNotThrow()
    {
        var store = new HistoryStore(_directoryPath);
        var entry = store.Append(BaseTimestamp, "install", "Package.Id", "Package", MakeResult(), [], batchId: null);
        store.Delete(entry);

        var exception = Record.Exception(() => store.Delete(entry));

        Assert.Null(exception);
    }

    [Fact]
    public void List_WithCorruptJsonFile_SkipsItAndReturnsTheRest()
    {
        var store = new HistoryStore(_directoryPath);
        store.Append(BaseTimestamp, "install", "Good.Id", "Good", MakeResult(), [], batchId: null);
        Directory.CreateDirectory(_directoryPath);
        File.WriteAllText(Path.Combine(_directoryPath, "corrupt-entry.json"), "{ not valid json ");

        var listed = store.List();

        var entry = Assert.Single(listed);
        Assert.Equal("Good.Id", entry.PackageId);
    }

    [Fact]
    public void Append_PrunesOldestEntriesBeyondMaxEntries()
    {
        var store = new HistoryStore(_directoryPath) { MaxEntries = 5 };

        for (var i = 0; i < 7; i++)
        {
            store.Append(
                BaseTimestamp.AddSeconds(i), "install", $"Package.{i}", $"Package {i}", MakeResult(), [], batchId: null);
        }

        var listed = store.List();

        Assert.Equal(5, listed.Count);
        Assert.DoesNotContain(listed, e => e.PackageId is "Package.0" or "Package.1");
        Assert.Contains(listed, e => e.PackageId == "Package.6");
    }

    [Fact]
    public void Append_WithPathUnsafeId_SanitizesFileNameButKeepsRawIdOnEntry()
    {
        var store = new HistoryStore(_directoryPath);
        const string unsafeId = @"ARP\Machine\X64\{5EC2AC66-1234-4B4D-9A9A-000000000000}";

        var entry = store.Append(BaseTimestamp, "uninstall", unsafeId, "Some App", MakeResult(), [], batchId: null);

        Assert.Equal(unsafeId, entry.PackageId);
        Assert.DoesNotContain('\\', entry.LogFileName);
        Assert.DoesNotContain('{', entry.LogFileName);
        Assert.DoesNotContain('}', entry.LogFileName);
        Assert.True(File.Exists(Path.Combine(_directoryPath, entry.LogFileName)));

        var listed = Assert.Single(store.List());
        Assert.Equal(unsafeId, listed.PackageId);
    }
}
