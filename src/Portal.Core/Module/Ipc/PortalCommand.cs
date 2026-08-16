namespace Portal.Module.Ipc;

public enum PortalCommandKind
{
    DownloadVanilla,
    DownloadLoader,
    DownloadModpack,
    Launch,
    ShowMainWindow
}

public sealed class PortalCommand
{
    public PortalCommandKind Kind { get; set; }

        public string? Version { get; set; }

        public List<PortalLoaderSpec> Loaders { get; set; } = [];

        public string? Source { get; set; }

        public string? Provider { get; set; }

        public string? PackVersion { get; set; }

        public string? Folder { get; set; }

        public string? InstanceId { get; set; }

        public string? WorldFolder { get; set; }

        public string? ServerAddress { get; set; }

        public int? ServerPort { get; set; }
}

public sealed record PortalLoaderSpec(string Kind, string? Version);
