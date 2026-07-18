using fNbt;
using Portal.Core.Minecraft.Classes;

namespace Portal.Core.Minecraft.Services;

public sealed class WorldScoreboardService
{
    private const string ScoreboardRelativePath = "data/minecraft/scoreboard.dat";

    public Task<WorldScoreboard?> LoadAsync(string worldPath, CancellationToken cancellationToken = default) =>
        Task.Run(() => Load(worldPath, cancellationToken), cancellationToken);

    public Task SaveAsync(string worldPath, WorldScoreboard scoreboard, CancellationToken cancellationToken = default) =>
        Task.Run(() => Save(worldPath, scoreboard, cancellationToken), cancellationToken);

    private static WorldScoreboard? Load(string worldPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = Path.Combine(worldPath, ScoreboardRelativePath);
        if (!File.Exists(path)) return null;

        var file = new NbtFile();
        file.LoadFromFile(path);
        var data = file.RootTag["data"] as NbtCompound;
        if (data == null) return null;

        var objectives = (data["Objectives"] as NbtList)?.OfType<NbtCompound>()
            .Select(x => new WorldScoreboardObjective(GetString(x, "Name"), GetString(x, "CriteriaName"), GetString(x, "DisplayName")))
            .Where(x => !string.IsNullOrWhiteSpace(x.Name)).ToArray() ?? [];
        var scores = (data["PlayerScores"] as NbtList)?.OfType<NbtCompound>()
            .Select(x => new WorldScoreboardScore(GetString(x, "Objective"), GetString(x, "Name"), GetString(x, "display"),
                (x["Score"] as NbtInt)?.Value ?? 0, (x["Locked"] as NbtByte)?.Value != 0))
            .Where(x => !string.IsNullOrWhiteSpace(x.Objective) && !string.IsNullOrWhiteSpace(x.Name)).ToArray() ?? [];
        return new WorldScoreboard(objectives, scores);
    }

    private static void Save(string worldPath, WorldScoreboard scoreboard, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = Path.Combine(worldPath, ScoreboardRelativePath);
        if (!File.Exists(path)) throw new FileNotFoundException("未找到积分榜数据文件。", path);

        var file = new NbtFile();
        file.LoadFromFile(path);
        var data = file.RootTag["data"] as NbtCompound ?? throw new InvalidDataException("积分榜数据文件不包含 data 标签。");
        ReplaceList(data, "Objectives", scoreboard.Objectives.Select(x =>
        {
            var objective = new NbtCompound();
            objective.Add(new NbtString("Name", x.Name));
            objective.Add(new NbtString("CriteriaName", x.CriteriaName));
            objective.Add(new NbtString("DisplayName", x.DisplayName));
            return objective;
        }));
        ReplaceList(data, "PlayerScores", scoreboard.Scores.Select(x =>
        {
            var score = new NbtCompound();
            score.Add(new NbtString("Objective", x.Objective));
            score.Add(new NbtString("Name", x.Name));
            score.Add(new NbtInt("Score", x.Score));
            if (!string.IsNullOrWhiteSpace(x.DisplayName)) score.Add(new NbtString("display", x.DisplayName));
            if (x.Locked) score.Add(new NbtByte("Locked", 1));
            return score;
        }));
        file.SaveToFile(path, NbtCompression.None);
    }

    private static void ReplaceList(NbtCompound data, string name, IEnumerable<NbtCompound> values)
    {
        var list = new NbtList(name, NbtTagType.Compound);
        foreach (var value in values) list.Add(value);
        if (data[name] != null) data.Remove(name);
        data.Add(list);
    }

    private static string GetString(NbtCompound parent, string name) => (parent[name] as NbtString)?.Value ?? string.Empty;
}
