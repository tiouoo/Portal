namespace Portal.Core.Minecraft.Classes;

public sealed record WorldGameRules(
    IReadOnlyDictionary<string, bool> BooleanRules,
    IReadOnlyDictionary<string, int> IntegerRules);