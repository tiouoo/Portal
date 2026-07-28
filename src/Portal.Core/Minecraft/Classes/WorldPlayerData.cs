namespace Portal.Core.Minecraft.Classes;

public sealed record WorldPlayerData(
    string FileName,
    string FilePath,
    string PlayerId,
    int DataVersion,
    int GameMode,
    float Health,
    int FoodLevel,
    float Saturation,
    int ExperienceLevel,
    int ExperienceTotal,
    float ExperienceProgress,
    string Dimension,
    double PositionX,
    double PositionY,
    double PositionZ,
    bool Invulnerable,
    bool MayFly,
    bool Flying,
    bool Instabuild);
