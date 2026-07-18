namespace Portal.Core.Minecraft.Classes;

public sealed record WorldScoreboard(IReadOnlyList<WorldScoreboardObjective> Objectives,
    IReadOnlyList<WorldScoreboardScore> Scores);

public sealed record WorldScoreboardObjective(string Name, string CriteriaName, string DisplayName);

public sealed record WorldScoreboardScore(string Objective, string Name, string DisplayName, int Score, bool Locked);
