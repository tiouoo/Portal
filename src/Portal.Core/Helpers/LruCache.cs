namespace Portal.Core.Helpers;

/// <summary>
/// 容量固定的 LRU 缓存，超出容量时淘汰最久未使用的条目。
/// </summary>
/// <remarks>
/// 用于那些"缓存丢失只会多查一次数据源"的场景，避免进程级字典无限增长。
/// 允许存放 <c>null</c> 值，用来表示"已确认不存在"。
/// </remarks>
public sealed class LruCache<TKey, TValue>(int capacity, IEqualityComparer<TKey>? comparer = null)
    where TKey : notnull
{
    private readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value)>> _entries = new(comparer);
    private readonly LinkedList<(TKey Key, TValue Value)> _usage = new();
    private readonly object _lock = new();

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
            if (_entries.Remove(key, out var existing))
                _usage.Remove(existing);

            _entries[key] = _usage.AddFirst((key, value));
            if (_entries.Count <= capacity)
                return;

            var oldest = _usage.Last!;
            _usage.RemoveLast();
            _entries.Remove(oldest.Value.Key);
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
