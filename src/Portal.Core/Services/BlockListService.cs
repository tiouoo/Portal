using Newtonsoft.Json;
using Portal.Core.Const;
using Portal.Core.Minecraft.Classes;

namespace Portal.Core.Services;

public sealed class BlockListDocument
{
    public int Version { get; set; } = 1;
    public List<string> BlockedInstances { get; set; } = [];
    public List<string> BlockedRecentPlays { get; set; } = [];
}

public sealed class BlockListService
{
    private const string FileName = "BlockList.portal";
    private readonly string _path = Path.Combine(ConfigPath.UserDataRootPath, FileName);

    private bool _areRecentPlaysExpanded;

    private bool _showBlockedInstances;

    private bool _showBlockedRecentPlays;

    private BlockListService()
    {
        Document = Load();
    }

    public static BlockListService Instance { get; } = new();

    public BlockListDocument Document { get; }

    public bool AreRecentPlaysExpanded
    {
        get => _areRecentPlaysExpanded;
        set
        {
            if (_areRecentPlaysExpanded == value) return;
            _areRecentPlaysExpanded = value;
            UiStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool ShowBlockedInstances
    {
        get => _showBlockedInstances;
        set
        {
            if (_showBlockedInstances == value) return;
            _showBlockedInstances = value;
            UiStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool ShowBlockedRecentPlays
    {
        get => _showBlockedRecentPlays;
        set
        {
            if (_showBlockedRecentPlays == value) return;
            _showBlockedRecentPlays = value;
            UiStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? Changed;
    public event EventHandler? UiStateChanged;

    public static void Initialize()
    {
        _ = Instance;
    }

    public static string GetInstanceKey(MinecraftInstance instance)
    {
        return instance.InstanceFolderPath;
    }

    public static string GetRecentPlayKey(RecentPlayTarget target)
    {
        return $"{GetInstanceKey(target.Instance)}|{(int)target.Type}|{target.Id}";
    }

    public bool IsInstanceBlocked(MinecraftInstance instance)
    {
        return Document.BlockedInstances.Contains(GetInstanceKey(instance));
    }

    public bool IsRecentPlayBlocked(RecentPlayTarget target)
    {
        return IsInstanceBlocked(target.Instance) ||
               Document.BlockedRecentPlays.Contains(GetRecentPlayKey(target));
    }

    public void BlockInstance(MinecraftInstance instance)
    {
        var key = GetInstanceKey(instance);
        if (!Document.BlockedInstances.Contains(key))
        {
            Document.BlockedInstances.Add(key);
            Save();
        }
    }

    public void UnblockInstance(MinecraftInstance instance)
    {
        if (Document.BlockedInstances.Remove(GetInstanceKey(instance)))
            Save();
    }

    public void ToggleInstanceBlock(MinecraftInstance instance)
    {
        if (IsInstanceBlocked(instance))
            UnblockInstance(instance);
        else
            BlockInstance(instance);
    }

    public void BlockRecentPlay(RecentPlayTarget target)
    {
        var key = GetRecentPlayKey(target);
        if (!Document.BlockedRecentPlays.Contains(key))
        {
            Document.BlockedRecentPlays.Add(key);
            Save();
        }
    }

    public void UnblockRecentPlay(RecentPlayTarget target)
    {
        if (Document.BlockedRecentPlays.Remove(GetRecentPlayKey(target)))
            Save();
    }

    public void ToggleRecentPlayBlock(RecentPlayTarget target)
    {
        var instanceBlocked = IsInstanceBlocked(target.Instance);
        var recentPlayBlocked = Document.BlockedRecentPlays.Contains(GetRecentPlayKey(target));

        if (instanceBlocked || recentPlayBlocked)
        {
            if (instanceBlocked)
                Document.BlockedInstances.Remove(GetInstanceKey(target.Instance));
            if (recentPlayBlocked)
                Document.BlockedRecentPlays.Remove(GetRecentPlayKey(target));
            Save();
        }
        else
        {
            BlockRecentPlay(target);
        }
    }

    public bool HasBlockedInstances(IEnumerable<MinecraftInstance> instances)
    {
        return instances.Any(IsInstanceBlocked);
    }

    public bool HasBlockedRecentPlays(IEnumerable<RecentPlayTarget> targets)
    {
        return targets.Any(IsRecentPlayBlocked);
    }

    private void Save()
    {
        Directory.CreateDirectory(ConfigPath.UserDataRootPath);
        var tempPath = _path + ".tmp";
        File.WriteAllText(tempPath, JsonConvert.SerializeObject(Document, Formatting.Indented));
        File.Move(tempPath, _path, true);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private BlockListDocument Load()
    {
        try
        {
            return File.Exists(_path)
                ? JsonConvert.DeserializeObject<BlockListDocument>(File.ReadAllText(_path)) ?? new BlockListDocument()
                : new BlockListDocument();
        }
        catch
        {
            return new BlockListDocument();
        }
    }
}