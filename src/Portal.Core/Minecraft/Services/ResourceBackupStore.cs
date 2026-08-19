namespace Portal.Core.Minecraft.Services;

/// <summary>
/// 管理资源更新/回滚的归档文件（"后悔药"）。
///
/// 命名约定（与 AXOLOTL 一致，只保留最近一个版本）：
/// - 更新 A → B 时，旧文件 A 被归档为 "B_A.old"（swap 备份），并清理其它 "A_*.old"。
/// - 回滚时把当前活动文件与新备份互换：活动 C + 备份 "C_B.old" → 活动 B + 备份 "B_C.old"，
///   因此可以反复回滚（C → B → C → B ...）。
/// - 若新旧文件名相同（仅内容变化），退化为 replace 备份 "{base}.rollback-{ts}.old"，只能回滚一次。
/// </summary>
public static class ResourceBackupStore
{
    private const string DisabledSuffix = ".disabled";
    private const string OldSuffix = ".old";
    private const string RollbackMarker = ".rollback-";

    public static bool IsBackupFile(string path)
    {
        return path.EndsWith(OldSuffix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>去掉 .disabled 后缀，得到用于命名/匹配的基名（保留扩展名）。</summary>
    public static string NormalizeBase(string fileName)
    {
        return fileName.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase)
            ? fileName[..^DisabledSuffix.Length]
            : fileName;
    }

    /// <summary>
    /// 更新时归档旧文件。返回归档路径；若无需归档（目标不存在）返回 null。
    /// </summary>
    public static string? ArchiveForUpdate(string oldFilePath, string newFileName)
    {
        var oldName = Path.GetFileName(oldFilePath);
        var folder = Path.GetDirectoryName(oldFilePath) ?? string.Empty;
        var oldBase = NormalizeBase(oldName);
        var newBase = NormalizeBase(newFileName);
        if (newBase.Length == 0 || oldBase.Length == 0 || !File.Exists(oldFilePath))
            return null;

        string backupName;
        if (string.Equals(oldBase, newBase, StringComparison.OrdinalIgnoreCase))
        {
            backupName = $"{oldBase}{RollbackMarker}{DateTime.Now:yyyyMMddHHmmssfff}.old";
        }
        else
        {
            backupName = $"{newBase}_{oldBase}{OldSuffix}";
            RemoveStaleBackups(folder, oldBase, keep: backupName);
            RemoveStaleReplaceBackups(folder, oldBase);
        }

        var backupPath = Path.Combine(folder, backupName);
        if (File.Exists(backupPath))
            File.Delete(backupPath);
        File.Move(oldFilePath, backupPath);
        Touch(backupPath);
        return backupPath;
    }

    /// <summary>当前活动文件是否有可回滚的备份。</summary>
    public static bool HasRollback(string activeFilePath)
    {
        return FindRollbackBackup(activeFilePath) is not null;
    }

    /// <summary>
    /// 回滚：把活动文件与最近一份备份互换。返回回滚后活动文件的路径；无备份返回 null。
    /// </summary>
    public static string? Rollback(string activeFilePath)
    {
        if (!File.Exists(activeFilePath))
            return null;
        var activeName = Path.GetFileName(activeFilePath);
        var folder = Path.GetDirectoryName(activeFilePath) ?? string.Empty;
        var activeBase = NormalizeBase(activeName);
        var disabled = activeName.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase);

        var swapBackup = FindSwapBackup(folder, activeBase);
        if (swapBackup is not null)
        {
            var restoredBase = Path.GetFileName(swapBackup)[(activeBase.Length + 1)..^OldSuffix.Length];
            if (restoredBase.Length > 0 && !string.Equals(restoredBase, activeBase, StringComparison.OrdinalIgnoreCase))
                return Swap(folder, activeFilePath, activeBase, disabled, swapBackup, restoredBase);
        }

        var replaceBackup = FindReplaceBackup(folder, activeBase);
        if (replaceBackup is not null)
            return Replace(folder, activeFilePath, activeBase, disabled, replaceBackup);

        return null;
    }

    private static string Swap(string folder, string activeFilePath, string activeBase, bool disabled,
        string swapBackup, string restoredBase)
    {
        var restoredName = restoredBase + (disabled ? DisabledSuffix : string.Empty);
        var restoredPath = Path.Combine(folder, restoredName);
        var archiveName = $"{restoredBase}_{activeBase}{OldSuffix}";
        var archivePath = Path.Combine(folder, archiveName);
        var backupPath = Path.Combine(folder, swapBackup);

        if (File.Exists(archivePath))
            File.Delete(archivePath);
        File.Move(activeFilePath, archivePath);
        Touch(archivePath);
        File.Move(backupPath, restoredPath, true);
        RemoveStaleBackups(folder, activeBase, keep: archiveName);
        RemoveStaleReplaceBackups(folder, activeBase);
        return restoredPath;
    }

    private static string Replace(string folder, string activeFilePath, string activeBase, bool disabled,
        string replaceBackup)
    {
        var restoredName = activeBase + (disabled ? DisabledSuffix : string.Empty);
        var restoredPath = Path.Combine(folder, restoredName);
        var backupPath = Path.Combine(folder, replaceBackup);

        if (File.Exists(restoredPath))
            File.Delete(restoredPath);
        File.Move(backupPath, restoredPath);
        return restoredPath;
    }

    /// <summary>
    /// 批量判断一组活动文件中哪些存在可回滚备份（单次扫描目录，避免逐文件枚举）。
    /// </summary>
    public static HashSet<string> FindRollbackTargets(string folder, IEnumerable<string> activeFilePaths)
    {
        var backups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var path in Directory.EnumerateFiles(folder, "*" + OldSuffix))
                backups.Add(Path.GetFileName(path));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var activePath in activeFilePaths)
        {
            var baseName = NormalizeBase(Path.GetFileName(activePath));
            var swapPrefix = baseName + "_";
            var replacePrefix = baseName + RollbackMarker;
            foreach (var backup in backups)
            {
                if (backup.StartsWith(swapPrefix, StringComparison.OrdinalIgnoreCase) ||
                    backup.StartsWith(replacePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(activePath);
                    break;
                }
            }
        }

        return result;
    }

    private static string? FindRollbackBackup(string activeFilePath)
    {
        if (!File.Exists(activeFilePath))
            return null;
        var folder = Path.GetDirectoryName(activeFilePath) ?? string.Empty;
        var activeBase = NormalizeBase(Path.GetFileName(activeFilePath));
        return FindSwapBackup(folder, activeBase) ?? FindReplaceBackup(folder, activeBase);
    }

    private static string? FindSwapBackup(string folder, string activeBase)
    {
        return ScanBackups(folder, $"{activeBase}_", matchReplace: false);
    }

    private static string? FindReplaceBackup(string folder, string activeBase)
    {
        return ScanBackups(folder, $"{activeBase}{RollbackMarker}", matchReplace: true);
    }

    private static string? ScanBackups(string folder, string prefix, bool matchReplace)
    {
        string? best = null;
        var bestTime = DateTime.MaxValue;
        try
        {
            foreach (var path in Directory.EnumerateFiles(folder, "*" + OldSuffix))
            {
                var name = Path.GetFileName(path);
                if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                var time = File.GetLastWriteTimeUtc(path);
                if (best is null || time < bestTime ||
                    (time == bestTime && string.CompareOrdinal(name, best) < 0))
                {
                    best = name;
                    bestTime = time;
                }
            }
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        return best;
    }

    private static void RemoveStaleBackups(string folder, string baseName, string? keep)
    {
        RemoveMatching(folder, $"{baseName}_", keep, matchReplace: false);
    }

    private static void RemoveStaleReplaceBackups(string folder, string baseName)
    {
        RemoveMatching(folder, $"{baseName}{RollbackMarker}", keep: null, matchReplace: true);
    }

    private static void RemoveMatching(string folder, string prefix, string? keep, bool matchReplace)
    {
        try
        {
            foreach (var path in Directory.EnumerateFiles(folder, "*" + OldSuffix))
            {
                var name = Path.GetFileName(path);
                if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (keep is not null && string.Equals(name, keep, StringComparison.OrdinalIgnoreCase))
                    continue;
                try
                {
                    File.Delete(path);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void Touch(string path)
    {
        try
        {
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
