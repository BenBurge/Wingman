namespace Wingman.Core.Operations;

/// <summary>
/// The ordered batch of operations the TUI has queued, in the order winget will run them.
/// </summary>
public sealed class OperationQueue
{
    private readonly List<QueuedOperation> _items = [];

    public IReadOnlyList<QueuedOperation> Items => _items;

    public int Count => _items.Count;

    public int ElevatedCount
    {
        get
        {
            var count = 0;
            foreach (var item in _items)
            {
                if (item.Plan.RequiresElevation)
                {
                    count++;
                }
            }

            return count;
        }
    }

    public event Action? Changed;

    /// <summary>
    /// Adds <paramref name="op"/>, or replaces the existing entry for the same package id in
    /// place so re-queuing a package does not move it to the end of the run order.
    /// </summary>
    public void Add(QueuedOperation op)
    {
        var index = IndexOf(op.Row.Id);
        if (index >= 0)
        {
            _items[index] = op;
        }
        else
        {
            _items.Add(op);
        }

        Changed?.Invoke();
    }

    public bool Remove(string id)
    {
        var index = IndexOf(id);
        if (index < 0)
        {
            return false;
        }

        _items.RemoveAt(index);
        Changed?.Invoke();
        return true;
    }

    public void Toggle(QueuedOperation op)
    {
        if (Contains(op.Row.Id))
        {
            Remove(op.Row.Id);
        }
        else
        {
            Add(op);
        }
    }

    public void Clear()
    {
        if (_items.Count == 0)
        {
            return;
        }

        _items.Clear();
        Changed?.Invoke();
    }

    public bool Contains(string id) => IndexOf(id) >= 0;

    private int IndexOf(string id)
    {
        for (var i = 0; i < _items.Count; i++)
        {
            if (string.Equals(_items[i].Row.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }
}
