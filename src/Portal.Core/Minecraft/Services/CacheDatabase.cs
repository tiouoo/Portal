using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using Portal.Core.Helpers;
using Portal.Core.Minecraft.Classes;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Core.Minecraft.Services;

internal static class CacheDatabase
{
    private static readonly object InitializationLock = new();

    // 内存里只保留最近使用的一部分条目：SQLite 才是数据源，缓存未命中只是多查一次数据库，
    // 而无上限的字典会随着扫描过的模组数量一直增长。
    private const int ModCacheCapacity = 4096;
    private static readonly LruCache<uint, ModCacheEntry?> ModCache = new(ModCacheCapacity);

    private static readonly LruCache<string, ModCacheEntry?> ModSha1Cache =
        new(ModCacheCapacity, StringComparer.OrdinalIgnoreCase);
    private static bool _initialized;
    private static string DatabasePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "cc.tiouo.Portal", "Cache", "cache.db");

    public static ModCacheEntry? ReadMod(uint fingerprint)
    {
        if (ModCache.TryGetValue(fingerprint, out var cached)) return cached;

        ModCacheEntry? entry;
        try
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT display_name, description, icon_url, project_id, file_id, friendly_name, metadata_fetched, curseforge_slug,
                       friendly_name_is_wiki, metadata_source, modrinth_project_id, modrinth_version_id, modrinth_slug,
                       translated_description
                FROM mod_cache WHERE fingerprint = $fingerprint;
                """;
            command.Parameters.AddWithValue("$fingerprint", (long)fingerprint);
            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                entry = null;
            }
            else
            {
                entry = ReadModEntry(reader);
            }
        }
        catch (SqliteException exception)
        {
            Logger.Warning($"读取模组缓存失败（指纹：{fingerprint}），将按缓存未命中继续。{Environment.NewLine}{exception}");
            return null;
        }
        catch (IOException exception)
        {
            Logger.Warning($"读取模组缓存文件失败（指纹：{fingerprint}），将按缓存未命中继续。{Environment.NewLine}{exception}");
            return null;
        }

        ModCache.Set(fingerprint, entry);
        return entry;
    }

    public static ModCacheEntry? ReadMod(string sha1)
    {
        if (ModSha1Cache.TryGetValue(sha1, out var cached)) return cached;

        ModCacheEntry? entry;
        try
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT display_name, description, icon_url, project_id, file_id, friendly_name, metadata_fetched, curseforge_slug,
                       friendly_name_is_wiki, metadata_source, modrinth_project_id, modrinth_version_id, modrinth_slug,
                       translated_description
                FROM mod_cache WHERE sha1 = $sha1;
                """;
            command.Parameters.AddWithValue("$sha1", sha1);
            using var reader = command.ExecuteReader();
            entry = reader.Read() ? ReadModEntry(reader) : null;
        }
        catch (SqliteException exception)
        {
            Logger.Warning($"读取模组缓存失败（SHA-1：{sha1}），将按缓存未命中继续。{Environment.NewLine}{exception}");
            return null;
        }
        catch (IOException exception)
        {
            Logger.Warning($"读取模组缓存文件失败（SHA-1：{sha1}），将按缓存未命中继续。{Environment.NewLine}{exception}");
            return null;
        }

        ModSha1Cache.Set(sha1, entry);
        return entry;
    }

    public static void WriteMod(uint fingerprint, ModCacheEntry entry)
    {
        try
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO mod_cache (fingerprint, display_name, description, icon_url, project_id, file_id, friendly_name, metadata_fetched, curseforge_slug, friendly_name_is_wiki, metadata_source, modrinth_project_id, modrinth_version_id, modrinth_slug, translated_description)
                VALUES ($fingerprint, $displayName, $description, $iconUrl, $projectId, $fileId, $friendlyName, $metadataFetched, $curseForgeSlug, $isWikiFriendlyName, $metadataSource, $modrinthProjectId, $modrinthVersionId, $modrinthSlug, $translatedDescription)
                ON CONFLICT(fingerprint) DO UPDATE SET
                    display_name = excluded.display_name, description = excluded.description, icon_url = excluded.icon_url,
                    project_id = excluded.project_id, file_id = excluded.file_id, friendly_name = excluded.friendly_name,
                    metadata_fetched = excluded.metadata_fetched, curseforge_slug = excluded.curseforge_slug,
                    friendly_name_is_wiki = excluded.friendly_name_is_wiki, metadata_source = excluded.metadata_source,
                    modrinth_project_id = excluded.modrinth_project_id, modrinth_version_id = excluded.modrinth_version_id,
                    modrinth_slug = excluded.modrinth_slug, translated_description = excluded.translated_description;
                """;
            command.Parameters.AddWithValue("$fingerprint", (long)fingerprint);
            AddModParameters(command, entry);
            command.ExecuteNonQuery();
            ModCache.Set(fingerprint, entry);
        }
        catch (SqliteException exception)
        {
            Logger.Error($"写入模组缓存失败（指纹：{fingerprint}）。", exception);
        }
        catch (IOException exception)
        {
            Logger.Error($"写入模组缓存文件失败（指纹：{fingerprint}）。", exception);
        }
    }

    public static void WriteMod(string sha1, ModCacheEntry entry)
    {
        try
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO mod_cache (sha1, display_name, description, icon_url, project_id, file_id, friendly_name, metadata_fetched, curseforge_slug, friendly_name_is_wiki, metadata_source, modrinth_project_id, modrinth_version_id, modrinth_slug, translated_description)
                VALUES ($sha1, $displayName, $description, $iconUrl, $projectId, $fileId, $friendlyName, $metadataFetched, $curseForgeSlug, $isWikiFriendlyName, $metadataSource, $modrinthProjectId, $modrinthVersionId, $modrinthSlug, $translatedDescription)
                ON CONFLICT(sha1) DO UPDATE SET
                    display_name = excluded.display_name, description = excluded.description, icon_url = excluded.icon_url,
                    project_id = excluded.project_id, file_id = excluded.file_id, friendly_name = excluded.friendly_name,
                    metadata_fetched = excluded.metadata_fetched, curseforge_slug = excluded.curseforge_slug,
                    friendly_name_is_wiki = excluded.friendly_name_is_wiki, metadata_source = excluded.metadata_source,
                    modrinth_project_id = excluded.modrinth_project_id, modrinth_version_id = excluded.modrinth_version_id,
                    modrinth_slug = excluded.modrinth_slug, translated_description = excluded.translated_description;
                """;
            command.Parameters.AddWithValue("$sha1", sha1);
            AddModParameters(command, entry);
            command.ExecuteNonQuery();
            ModSha1Cache.Set(sha1, entry);
        }
        catch (SqliteException exception)
        {
            Logger.Error($"写入模组缓存失败（SHA-1：{sha1}）。", exception);
        }
        catch (IOException exception)
        {
            Logger.Error($"写入模组缓存文件失败（SHA-1：{sha1}）。", exception);
        }
    }

    public static void WriteMod(uint fingerprint, string sha1, ModCacheEntry entry)
    {
        try
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            // fingerprint and sha1 have independent unique constraints. Remove any stale rows
            // matched by either key before creating their single canonical cache entry.
            command.CommandText = "DELETE FROM mod_cache WHERE fingerprint = $fingerprint OR sha1 = $sha1;";
            command.Parameters.AddWithValue("$fingerprint", (long)fingerprint);
            command.Parameters.AddWithValue("$sha1", sha1);
            command.ExecuteNonQuery();

            command.Parameters.Clear();
            command.CommandText = """
                INSERT INTO mod_cache (fingerprint, sha1, display_name, description, icon_url, project_id, file_id, friendly_name, metadata_fetched, curseforge_slug, friendly_name_is_wiki, metadata_source, modrinth_project_id, modrinth_version_id, modrinth_slug, translated_description)
                VALUES ($fingerprint, $sha1, $displayName, $description, $iconUrl, $projectId, $fileId, $friendlyName, $metadataFetched, $curseForgeSlug, $isWikiFriendlyName, $metadataSource, $modrinthProjectId, $modrinthVersionId, $modrinthSlug, $translatedDescription);
                """;
            command.Parameters.AddWithValue("$fingerprint", (long)fingerprint);
            command.Parameters.AddWithValue("$sha1", sha1);
            AddModParameters(command, entry);
            command.ExecuteNonQuery();
            transaction.Commit();

            ModCache.Set(fingerprint, entry);
            ModSha1Cache.Set(sha1, entry);
        }
        catch (SqliteException exception)
        {
            Logger.Error($"写入模组缓存失败（指纹：{fingerprint}，SHA-1：{sha1}）。", exception);
        }
        catch (IOException exception)
        {
            Logger.Error($"写入模组缓存文件失败（指纹：{fingerprint}，SHA-1：{sha1}）。", exception);
        }
    }

    public static List<NewsEntry> ReadNews(NewsEdition edition)
    {
        try
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT title, version, type, image_url, content_path, id, published_at, short_text, needs_translation
                FROM news_cache_entry
                WHERE edition = $edition
                ORDER BY published_at DESC;
                """;
            command.Parameters.AddWithValue("$edition", edition.ToString());
            using var reader = command.ExecuteReader();
            var entries = new List<NewsEntry>();
            while (reader.Read())
            {
                entries.Add(new NewsEntry
                {
                    Title = reader.GetString(0),
                    Version = reader.GetString(1),
                    Type = reader.GetString(2),
                    ImageUrl = reader.GetString(3),
                    ContentPath = reader.GetString(4),
                    Id = reader.GetString(5),
                    Date = DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(6)).LocalDateTime,
                    ShortText = reader.GetString(7),
                    NeedsTranslation = reader.IsDBNull(8) ? null : reader.GetInt64(8) != 0,
                    Edition = edition
                });
            }

            return entries;
        }
        catch (SqliteException exception)
        {
            Logger.Warning($"读取 {edition} 新闻缓存失败，将使用空缓存。{Environment.NewLine}{exception}");
            return [];
        }
        catch (IOException exception)
        {
            Logger.Warning($"读取 {edition} 新闻缓存文件失败，将使用空缓存。{Environment.NewLine}{exception}");
            return [];
        }
    }

    public static void WriteNews(NewsEdition edition, IReadOnlyCollection<NewsEntry> entries)
    {
        try
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                DELETE FROM news_cache_entry WHERE edition = $edition;
                """;
            command.Parameters.AddWithValue("$edition", edition.ToString());
            command.ExecuteNonQuery();

            command.CommandText = """
                INSERT INTO news_cache_entry (
                    edition, id, title, version, type, image_url, content_path, published_at, short_text, needs_translation
                ) VALUES (
                    $edition, $id, $title, $version, $type, $imageUrl, $contentPath, $publishedAt, $shortText, $needsTranslation
                );
                """;
            foreach (var entry in entries)
            {
                command.Parameters.Clear();
                command.Parameters.AddWithValue("$edition", edition.ToString());
                command.Parameters.AddWithValue("$id", entry.Id);
                command.Parameters.AddWithValue("$title", entry.Title);
                command.Parameters.AddWithValue("$version", entry.Version);
                command.Parameters.AddWithValue("$type", entry.Type);
                command.Parameters.AddWithValue("$imageUrl", entry.ImageUrl);
                command.Parameters.AddWithValue("$contentPath", entry.ContentPath);
                command.Parameters.AddWithValue("$publishedAt", new DateTimeOffset(entry.Date).ToUnixTimeSeconds());
                command.Parameters.AddWithValue("$shortText", entry.ShortText);
                command.Parameters.AddWithValue("$needsTranslation", entry.NeedsTranslation.HasValue
                    ? entry.NeedsTranslation.Value ? 1 : 0
                    : DBNull.Value);
                command.ExecuteNonQuery();
            }

            transaction.Commit();
        }
        catch (SqliteException exception)
        {
            Logger.Error($"写入 {edition} 新闻缓存失败。", exception);
        }
        catch (IOException exception)
        {
            Logger.Error($"写入 {edition} 新闻缓存文件失败。", exception);
        }
    }

    /// <summary>
    /// 读取单条新闻详情正文缓存。仅在用户实际打开新闻详情时调用。
    /// </summary>
    public static NewsDetail? ReadNewsDetail(string id)
    {
        try
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT title, version, type, image_url, published_at, body, fetched_at, needs_translation
                FROM news_detail_cache WHERE id = $id;
                """;
            command.Parameters.AddWithValue("$id", id);
            using var reader = command.ExecuteReader();
            if (!reader.Read()) return null;
            return new NewsDetail
            {
                Id = id,
                Title = reader.GetString(0),
                Version = reader.GetString(1),
                Type = reader.GetString(2),
                ImageUrl = reader.GetString(3),
                Date = DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(4)).LocalDateTime,
                Body = reader.GetString(5),
                FetchedAt = DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(6)).LocalDateTime,
                NeedsTranslation = reader.IsDBNull(7) ? null : reader.GetInt64(7) != 0
            };
        }
        catch (SqliteException exception)
        {
            Logger.Warning($"读取新闻详情缓存失败（ID：{id}），将按缓存未命中继续。{Environment.NewLine}{exception}");
            return null;
        }
        catch (IOException exception)
        {
            Logger.Warning($"读取新闻详情缓存文件失败（ID：{id}），将按缓存未命中继续。{Environment.NewLine}{exception}");
            return null;
        }
    }

    /// <summary>写入/更新一条新闻详情正文缓存。</summary>
    public static void WriteNewsDetail(NewsDetail detail)
    {
        try
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO news_detail_cache (id, title, version, type, image_url, published_at, body, fetched_at, needs_translation)
                VALUES ($id, $title, $version, $type, $imageUrl, $publishedAt, $body, $fetchedAt, $needsTranslation)
                ON CONFLICT(id) DO UPDATE SET
                    title = excluded.title, version = excluded.version, type = excluded.type,
                    image_url = excluded.image_url, published_at = excluded.published_at,
                    body = excluded.body, fetched_at = excluded.fetched_at,
                    needs_translation = excluded.needs_translation;
                """;
            command.Parameters.AddWithValue("$id", detail.Id);
            command.Parameters.AddWithValue("$title", detail.Title);
            command.Parameters.AddWithValue("$version", detail.Version);
            command.Parameters.AddWithValue("$type", detail.Type);
            command.Parameters.AddWithValue("$imageUrl", detail.ImageUrl);
            command.Parameters.AddWithValue("$publishedAt", new DateTimeOffset(detail.Date).ToUnixTimeSeconds());
            command.Parameters.AddWithValue("$body", detail.Body);
            command.Parameters.AddWithValue("$fetchedAt", new DateTimeOffset(detail.FetchedAt).ToUnixTimeSeconds());
            command.Parameters.AddWithValue("$needsTranslation", detail.NeedsTranslation.HasValue
                ? detail.NeedsTranslation.Value ? 1 : 0
                : DBNull.Value);
            command.ExecuteNonQuery();
        }
        catch (SqliteException exception)
        {
            Logger.Error($"写入新闻详情缓存失败（ID：{detail.Id}）。", exception);
        }
        catch (IOException exception)
        {
            Logger.Error($"写入新闻详情缓存文件失败（ID：{detail.Id}）。", exception);
        }
    }

    private static SqliteConnection OpenConnection()
    {
        EnsureInitialized();
        var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=True");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA busy_timeout = 5000;";
        command.ExecuteNonQuery();
        return connection;
    }

    private static void EnsureInitialized()
    {
        lock (InitializationLock)
        {
            if (_initialized) return;
            Logger.Info($"正在初始化本地缓存数据库：{DatabasePath}");
            SQLitePCL.Batteries.Init();
            Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
            using var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=True");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA journal_mode = DELETE;
                PRAGMA busy_timeout = 5000;
                CREATE TABLE IF NOT EXISTS mod_cache (
                    fingerprint INTEGER PRIMARY KEY, display_name TEXT NULL, description TEXT NULL, icon_url TEXT NULL,
                    project_id INTEGER NULL, file_id INTEGER NULL, friendly_name TEXT NULL, metadata_fetched INTEGER NOT NULL,
                    curseforge_slug TEXT NULL, friendly_name_is_wiki INTEGER NOT NULL DEFAULT 0,
                    sha1 TEXT NULL UNIQUE, metadata_source TEXT NULL, modrinth_project_id TEXT NULL, modrinth_version_id TEXT NULL,
                    modrinth_slug TEXT NULL, translated_description TEXT NULL
                );
                CREATE TABLE IF NOT EXISTS news_cache_entry (
                    edition TEXT NOT NULL, id TEXT NOT NULL, title TEXT NOT NULL, version TEXT NOT NULL, type TEXT NOT NULL,
                    image_url TEXT NOT NULL, content_path TEXT NOT NULL, published_at INTEGER NOT NULL, short_text TEXT NOT NULL,
                    needs_translation INTEGER NULL, PRIMARY KEY (edition, id)
                );
                CREATE INDEX IF NOT EXISTS idx_news_cache_entry_edition_published_at
                    ON news_cache_entry (edition, published_at DESC);
                CREATE TABLE IF NOT EXISTS news_detail_cache (
                    id TEXT PRIMARY KEY, title TEXT NOT NULL, version TEXT NOT NULL, type TEXT NOT NULL,
                    image_url TEXT NOT NULL, published_at INTEGER NOT NULL, body TEXT NOT NULL, fetched_at INTEGER NOT NULL
                );
                """;
            command.ExecuteNonQuery();
            EnsureModCacheColumns(connection);
            EnsureNewsDetailColumns(connection);
            MigrateLegacyNews(connection);
            _initialized = true;
            Logger.Info("本地缓存数据库初始化完成。");
        }
    }

    private static void EnsureNewsDetailColumns(SqliteConnection connection)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(news_detail_cache);";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            columns.Add(reader.GetString(1));

        reader.Close();
        if (!columns.Contains("needs_translation"))
        {
            command.CommandText = "ALTER TABLE news_detail_cache ADD COLUMN needs_translation INTEGER NULL;";
            command.ExecuteNonQuery();
        }
    }

    private static void EnsureModCacheColumns(SqliteConnection connection)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(mod_cache);";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            columns.Add(reader.GetString(1));

        reader.Close();
        if (!columns.Contains("curseforge_slug"))
        {
            command.CommandText = "ALTER TABLE mod_cache ADD COLUMN curseforge_slug TEXT NULL;";
            command.ExecuteNonQuery();
        }
        if (!columns.Contains("friendly_name_is_wiki"))
        {
            command.CommandText = "ALTER TABLE mod_cache ADD COLUMN friendly_name_is_wiki INTEGER NOT NULL DEFAULT 0;";
            command.ExecuteNonQuery();
        }
        if (!columns.Contains("sha1"))
        {
            command.CommandText = "ALTER TABLE mod_cache ADD COLUMN sha1 TEXT NULL;";
            command.ExecuteNonQuery();
            command.CommandText = "CREATE UNIQUE INDEX IF NOT EXISTS idx_mod_cache_sha1 ON mod_cache (sha1);";
            command.ExecuteNonQuery();
        }
        if (!columns.Contains("metadata_source"))
        {
            command.CommandText = "ALTER TABLE mod_cache ADD COLUMN metadata_source TEXT NULL;";
            command.ExecuteNonQuery();
        }
        if (!columns.Contains("modrinth_project_id"))
        {
            command.CommandText = "ALTER TABLE mod_cache ADD COLUMN modrinth_project_id TEXT NULL;";
            command.ExecuteNonQuery();
        }
        if (!columns.Contains("modrinth_version_id"))
        {
            command.CommandText = "ALTER TABLE mod_cache ADD COLUMN modrinth_version_id TEXT NULL;";
            command.ExecuteNonQuery();
        }
        if (!columns.Contains("modrinth_slug"))
        {
            command.CommandText = "ALTER TABLE mod_cache ADD COLUMN modrinth_slug TEXT NULL;";
            command.ExecuteNonQuery();
        }
        if (!columns.Contains("translated_description"))
        {
            command.CommandText = "ALTER TABLE mod_cache ADD COLUMN translated_description TEXT NULL;";
            command.ExecuteNonQuery();
        }
    }

    private static ModCacheEntry ReadModEntry(SqliteDataReader reader) => new()
    {
        DisplayName = reader.IsDBNull(0) ? null : reader.GetString(0),
        Description = reader.IsDBNull(1) ? null : reader.GetString(1),
        IconUrl = reader.IsDBNull(2) ? null : reader.GetString(2),
        ProjectId = reader.IsDBNull(3) ? null : reader.GetInt32(3),
        FileId = reader.IsDBNull(4) ? null : reader.GetInt32(4),
        FriendlyName = reader.GetInt64(8) != 0 && !reader.IsDBNull(5) ? reader.GetString(5) : null,
        MetadataFetched = reader.GetInt64(6) != 0,
        CurseForgeSlug = reader.IsDBNull(7) ? null : reader.GetString(7),
        IsWikiFriendlyName = reader.GetInt64(8) != 0,
        Source = reader.IsDBNull(9) ? null : reader.GetString(9),
        ModrinthProjectId = reader.IsDBNull(10) ? null : reader.GetString(10),
        ModrinthVersionId = reader.IsDBNull(11) ? null : reader.GetString(11),
        ModrinthSlug = reader.IsDBNull(12) ? null : reader.GetString(12),
        TranslatedDescription = reader.IsDBNull(13) ? null : reader.GetString(13)
    };

    private static void AddModParameters(SqliteCommand command, ModCacheEntry entry)
    {
        command.Parameters.AddWithValue("$displayName", (object?)entry.DisplayName ?? DBNull.Value);
        command.Parameters.AddWithValue("$description", (object?)entry.Description ?? DBNull.Value);
        command.Parameters.AddWithValue("$iconUrl", (object?)entry.IconUrl ?? DBNull.Value);
        command.Parameters.AddWithValue("$projectId", (object?)entry.ProjectId ?? DBNull.Value);
        command.Parameters.AddWithValue("$fileId", (object?)entry.FileId ?? DBNull.Value);
        command.Parameters.AddWithValue("$friendlyName", (object?)entry.FriendlyName ?? DBNull.Value);
        command.Parameters.AddWithValue("$metadataFetched", entry.MetadataFetched == true ? 1 : 0);
        command.Parameters.AddWithValue("$curseForgeSlug", (object?)entry.CurseForgeSlug ?? DBNull.Value);
        command.Parameters.AddWithValue("$isWikiFriendlyName", entry.IsWikiFriendlyName ? 1 : 0);
        command.Parameters.AddWithValue("$metadataSource", (object?)entry.Source ?? DBNull.Value);
        command.Parameters.AddWithValue("$modrinthProjectId", (object?)entry.ModrinthProjectId ?? DBNull.Value);
        command.Parameters.AddWithValue("$modrinthVersionId", (object?)entry.ModrinthVersionId ?? DBNull.Value);
        command.Parameters.AddWithValue("$modrinthSlug", (object?)entry.ModrinthSlug ?? DBNull.Value);
        command.Parameters.AddWithValue("$translatedDescription", (object?)entry.TranslatedDescription ?? DBNull.Value);
    }

    private static void MigrateLegacyNews(SqliteConnection connection)
    {
        using var tableCommand = connection.CreateCommand();
        tableCommand.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'news_cache';";
        if (tableCommand.ExecuteScalar() == null) return;

        var cachedResponses = new List<(NewsEdition Edition, PatchNotesResponse Response)>();
        using (var selectCommand = connection.CreateCommand())
        {
            selectCommand.CommandText = "SELECT edition, content FROM news_cache;";
            using var reader = selectCommand.ExecuteReader();
            while (reader.Read())
            {
                if (!Enum.TryParse<NewsEdition>(reader.GetString(0), out var edition)) continue;
                PatchNotesResponse? response;
                try
                {
                    response = JsonConvert.DeserializeObject<PatchNotesResponse>(reader.GetString(1));
                }
                catch (JsonException exception)
                {
                    Logger.Error("迁移旧新闻缓存失败，旧缓存将保持不变。", exception);
                    return;
                }

                if (response == null) return;
                cachedResponses.Add((edition, response));
            }
        }

        using var transaction = connection.BeginTransaction();
        foreach (var (edition, response) in cachedResponses)
        {
            foreach (var entry in response.Entries)
            {
                using var insertCommand = connection.CreateCommand();
                insertCommand.Transaction = transaction;
                insertCommand.CommandText = """
                    INSERT OR REPLACE INTO news_cache_entry (
                        edition, id, title, version, type, image_url, content_path, published_at, short_text, needs_translation
                    ) VALUES (
                        $edition, $id, $title, $version, $type, $imageUrl, $contentPath, $publishedAt, $shortText, $needsTranslation
                    );
                    """;
                insertCommand.Parameters.AddWithValue("$edition", edition.ToString());
                insertCommand.Parameters.AddWithValue("$id", entry.Id);
                insertCommand.Parameters.AddWithValue("$title", entry.Title);
                insertCommand.Parameters.AddWithValue("$version", entry.Version);
                insertCommand.Parameters.AddWithValue("$type", string.IsNullOrEmpty(entry.Type) ? entry.PatchNoteType : entry.Type);
                insertCommand.Parameters.AddWithValue("$imageUrl", ToAbsoluteImageUrl(entry.Image?.Url));
                insertCommand.Parameters.AddWithValue("$contentPath", entry.ContentPath);
                insertCommand.Parameters.AddWithValue("$publishedAt", new DateTimeOffset(entry.Date.ToLocalTime()).ToUnixTimeSeconds());
                insertCommand.Parameters.AddWithValue("$shortText", entry.ShortText);
                insertCommand.Parameters.AddWithValue("$needsTranslation", entry.NeedsTranslation.HasValue
                    ? entry.NeedsTranslation.Value ? 1 : 0
                    : DBNull.Value);
                insertCommand.ExecuteNonQuery();
            }
        }

        using var dropCommand = connection.CreateCommand();
        dropCommand.Transaction = transaction;
        dropCommand.CommandText = "DROP TABLE news_cache;";
        dropCommand.ExecuteNonQuery();
        transaction.Commit();
    }

    private static string ToAbsoluteImageUrl(string? imageUrl) => string.IsNullOrEmpty(imageUrl)
        ? string.Empty
        : imageUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? imageUrl
            : "https://launchercontent.mojang.com" + imageUrl;
}
