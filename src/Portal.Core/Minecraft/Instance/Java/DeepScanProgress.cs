namespace Portal.Core.Minecraft.Instance.Java;

public record DeepScanProgress(
    long DirectoriesScanned,
    long DirectoriesQueued,
    int JavasFound,
    string CurrentStatus);
