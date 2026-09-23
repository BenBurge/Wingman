using Wingman.Core.Models;
using Wingman.Core.Operations;

namespace Wingman.Core.Tests;

public class OperationQueueTests
{
    private static QueuedOperation Queued(string id, bool requiresElevation = false) => new(
        OperationKind.Install,
        new PackageRow(id, id, "1.0", null, "winget"),
        new OperationPlan(OperationKind.Install, new OperationRequest(id), requiresElevation, "", "", false));

    [Fact]
    public void Add_MultiplePackages_PreservesInsertionOrder()
    {
        var queue = new OperationQueue();
        queue.Add(Queued("A"));
        queue.Add(Queued("B"));
        queue.Add(Queued("C"));

        Assert.Equal(["A", "B", "C"], queue.Items.Select(i => i.Row.Id));
    }

    [Fact]
    public void Add_DuplicateId_ReplacesInPlace()
    {
        var queue = new OperationQueue();
        queue.Add(Queued("A"));
        queue.Add(Queued("B"));
        queue.Add(Queued("C"));

        var replacement = Queued("B", requiresElevation: true);
        queue.Add(replacement);

        Assert.Equal(["A", "B", "C"], queue.Items.Select(i => i.Row.Id));
        Assert.Same(replacement, queue.Items[1]);
    }

    [Fact]
    public void Add_DuplicateId_IsCaseInsensitive()
    {
        var queue = new OperationQueue();
        queue.Add(Queued("Git.Git"));

        queue.Add(Queued("git.git"));

        Assert.Equal(1, queue.Count);
        Assert.Equal("git.git", queue.Items[0].Row.Id);
    }

    [Fact]
    public void Remove_ExistingId_RemovesEntryAndReturnsTrue()
    {
        var queue = new OperationQueue();
        queue.Add(Queued("A"));
        queue.Add(Queued("B"));

        var removed = queue.Remove("A");

        Assert.True(removed);
        Assert.Equal(["B"], queue.Items.Select(i => i.Row.Id));
    }

    [Fact]
    public void Remove_MissingId_ReturnsFalseAndLeavesQueueUnchanged()
    {
        var queue = new OperationQueue();
        queue.Add(Queued("A"));

        var removed = queue.Remove("Missing");

        Assert.False(removed);
        Assert.Equal(1, queue.Count);
    }

    [Fact]
    public void Toggle_AbsentEntry_Adds()
    {
        var queue = new OperationQueue();

        queue.Toggle(Queued("A"));

        Assert.True(queue.Contains("A"));
    }

    [Fact]
    public void Toggle_PresentEntry_Removes()
    {
        var queue = new OperationQueue();
        queue.Add(Queued("A"));

        queue.Toggle(Queued("A"));

        Assert.False(queue.Contains("A"));
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void Clear_RemovesAllEntries()
    {
        var queue = new OperationQueue();
        queue.Add(Queued("A"));
        queue.Add(Queued("B"));

        queue.Clear();

        Assert.Equal(0, queue.Count);
        Assert.Empty(queue.Items);
    }

    [Fact]
    public void Contains_IsCaseInsensitive()
    {
        var queue = new OperationQueue();
        queue.Add(Queued("Git.Git"));

        Assert.True(queue.Contains("git.git"));
        Assert.True(queue.Contains("GIT.GIT"));
        Assert.False(queue.Contains("Other.Id"));
    }

    [Fact]
    public void ElevatedCount_CountsOnlyElevatedEntries()
    {
        var queue = new OperationQueue();
        queue.Add(Queued("A", requiresElevation: true));
        queue.Add(Queued("B", requiresElevation: false));
        queue.Add(Queued("C", requiresElevation: true));

        Assert.Equal(2, queue.ElevatedCount);
    }

    [Fact]
    public void Changed_FiresOnceForAdd()
    {
        var queue = new OperationQueue();
        var fireCount = 0;
        queue.Changed += () => fireCount++;

        queue.Add(Queued("A"));

        Assert.Equal(1, fireCount);
    }

    [Fact]
    public void Changed_FiresOnceForReplace()
    {
        var queue = new OperationQueue();
        queue.Add(Queued("A"));
        var fireCount = 0;
        queue.Changed += () => fireCount++;

        queue.Add(Queued("A", requiresElevation: true));

        Assert.Equal(1, fireCount);
    }

    [Fact]
    public void Changed_FiresOnceForSuccessfulRemove()
    {
        var queue = new OperationQueue();
        queue.Add(Queued("A"));
        var fireCount = 0;
        queue.Changed += () => fireCount++;

        queue.Remove("A");

        Assert.Equal(1, fireCount);
    }

    [Fact]
    public void Changed_DoesNotFireForNoOpRemove()
    {
        var queue = new OperationQueue();
        var fireCount = 0;
        queue.Changed += () => fireCount++;

        queue.Remove("Missing");

        Assert.Equal(0, fireCount);
    }

    [Fact]
    public void Changed_DoesNotFireForClearOfEmptyQueue()
    {
        var queue = new OperationQueue();
        var fireCount = 0;
        queue.Changed += () => fireCount++;

        queue.Clear();

        Assert.Equal(0, fireCount);
    }

    [Fact]
    public void Changed_FiresOnceForClearOfNonEmptyQueue()
    {
        var queue = new OperationQueue();
        queue.Add(Queued("A"));
        queue.Add(Queued("B"));
        var fireCount = 0;
        queue.Changed += () => fireCount++;

        queue.Clear();

        Assert.Equal(1, fireCount);
    }

    [Fact]
    public void Changed_FiresOnceForToggleAdd()
    {
        var queue = new OperationQueue();
        var fireCount = 0;
        queue.Changed += () => fireCount++;

        queue.Toggle(Queued("A"));

        Assert.Equal(1, fireCount);
    }

    [Fact]
    public void Changed_FiresOnceForToggleRemove()
    {
        var queue = new OperationQueue();
        queue.Add(Queued("A"));
        var fireCount = 0;
        queue.Changed += () => fireCount++;

        queue.Toggle(Queued("A"));

        Assert.Equal(1, fireCount);
    }
}
