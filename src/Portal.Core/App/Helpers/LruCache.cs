namespace Portal.Core.App.Helpers;

public sealed class LruCache<TKey, TValue>(int capacity, IEqualityComparer<TKey>? comparer = null)
    where TKey : notnull
{
    private readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value)>> _entries = new(comparer);
    private readonly Lock _lock = new();
    private readonly LinkedList<(TKey Key, TValue Value)> _usage = new();

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _entries.Count;
            }
        }
    }

    public bool TryGetValue(TKey key, out TValue? value)
    {
        lock (_lock)
        {
            if (!_entries.TryGetValue(key, out var node))
            {
                value = default;
                return false;
            }

            _usage.Remove(node);
            _usage.AddFirst(node);
            value = node.Value.Value;
            return true;
        }
    }

    public void Set(TKey key, TValue value)
    {
        lock (_lock)
        {
            if (_entries.Remove(key, out var existing)) _usage.Remove(existing);

            var newNode = _usage.AddFirst((key, value));
            _entries[key] = newNode;

            if (_entries.Count <= capacity) return;
            var oldestNode = _usage.Last!;
            _usage.RemoveLast();
            _entries.Remove(oldestNode.Value.Key);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
            _usage.Clear();
        }
    }
}