namespace Portal.Module.Ipc;

public enum PortalCliParseStatus
{
        NotACommand,
    Help,
    Error,
    Command
}

public static class PortalCommandParser
{
    public const string UriScheme = "portal";

    public static PortalCliParseStatus Parse(string[] args, out PortalCommand? command, out string? error)
    {
        command = null;
        error = null;
        if (args.Length == 0) return PortalCliParseStatus.NotACommand;

        var first = args[0].Trim();
        if (first is "help" or "--help" or "-h" or "/?" or "-?")
            return PortalCliParseStatus.Help;

        if (first.StartsWith(UriScheme + ":", StringComparison.OrdinalIgnoreCase))
            return ParseUri(first, out command, out error);

        return first.ToLowerInvariant() switch
        {
            "install" or "download" => ParseInstallCli(args, out command, out error),
            "launch" => ParseLaunchCli(args, out command, out error),
            _ => PortalCliParseStatus.NotACommand
        };
    }

    public static string GetUsageText() =>
        """
        Portal 命令行用法：
          Portal.Desktop.exe install vanilla <版本> [--folder <文件夹>] [--id <实例ID>]
          Portal.Desktop.exe install loader <版本> --loader <加载器[@版本]> [--loader ...] [--folder <文件夹>] [--id <实例ID>]
          Portal.Desktop.exe install modpack <路径|链接|项目名或ID> [--from modrinth|curseforge] [--version <版本或fileId>] [--folder <文件夹>] [--id <实例ID>]
          Portal.Desktop.exe launch <实例ID> [--folder <文件夹>] [--world <世界文件夹>]
          Portal.Desktop.exe launch <实例ID> [--folder <文件夹>] [--server <服务器地址>] [--port <端口>]
          Portal.Desktop.exe help

        加载器：fabric / forge / neoforge / quilt / optifine（可用 @ 指定版本，如 fabric@0.16.9）
        整合包：可传本地文件、直链，或 Modrinth / CurseForge 的项目名称、slug、项目 ID；
                不指定 --version 时安装最新版本（--version 为 Modrinth 版本 ID/版本号或 CurseForge fileId，--file 等价）。
        文件夹：启动器内已添加的 Minecraft 文件夹名称或路径；不传时使用默认（第一个）文件夹。
        启动时未指定文件夹则启动第一个匹配该实例 ID 的实例。
        世界：--world 传世界在 saves 目录下的文件夹名，启动后直接进入该世界（同名世界以文件夹名区分）。
        服务器：--server 传服务器地址，--port 传端口（缺省 25565），启动后直接进入该服务器。
        --world 与 --server 互斥。

        等价的 portal:// 协议形式（注册协议后浏览器可直接调用）：
          portal://install/vanilla?version=1.21.8&folder=B&id=my-1.21.8
          portal://install/loader?version=1.21.8&loader=fabric&loader=optifine
          portal://install/modpack?source=fabulously-optimized&from=modrinth&version=cZY3Bvs9
          portal://install/modpack?source=https%3A%2F%2Fexample.com%2Fpack.mrpack&folder=B
          portal://launch?id=26.2&folder=B
          portal://launch?id=26.2&folder=B&world=New%20World
          portal://launch?id=26.2&folder=B&server=play.example.com&port=25565
        """;

    private static PortalCliParseStatus ParseInstallCli(string[] args, out PortalCommand? command, out string? error)
    {
        command = null;
        error = null;
        if (args.Length < 2)
        {
            error = "install 需要子命令：vanilla / loader / modpack。";
            return PortalCliParseStatus.Error;
        }

        var sub = args[1].ToLowerInvariant();
        if (sub is not ("vanilla" or "loader" or "modpack"))
        {
            error = $"未知的 install 子命令“{args[1]}”，支持：vanilla / loader / modpack。";
            return PortalCliParseStatus.Error;
        }

        if (!TryParseOptions(args, 2, out var positionals, out var options, out error))
            return PortalCliParseStatus.Error;
        if (positionals.Count != 1)
        {
            error = sub == "modpack"
                ? "install modpack 需要且仅需要一个参数：整合包路径、链接或项目名称/ID。"
                : $"install {sub} 需要且仅需要一个参数：Minecraft 版本号。";
            return PortalCliParseStatus.Error;
        }

        if (sub == "modpack")
        {
            if (options.Loaders.Count > 0)
            {
                error = "install modpack 不支持 --loader 参数。";
                return PortalCliParseStatus.Error;
            }
            if (!TryValidateProvider(options.Provider, out error)) return PortalCliParseStatus.Error;
            command = new PortalCommand
            {
                Kind = PortalCommandKind.DownloadModpack,
                Source = positionals[0],
                Provider = options.Provider?.ToLowerInvariant(),
                PackVersion = options.PackVersion,
                Folder = options.Folder,
                InstanceId = options.InstanceId
            };
            return PortalCliParseStatus.Command;
        }

        if (options.Provider is not null || options.PackVersion is not null)
        {
            error = $"--from / --version 仅用于 install modpack。";
            return PortalCliParseStatus.Error;
        }
        if (sub == "loader" && options.Loaders.Count == 0)
        {
            error = "install loader 至少需要一个 --loader 参数。";
            return PortalCliParseStatus.Error;
        }

        command = new PortalCommand
        {
            Kind = options.Loaders.Count > 0 ? PortalCommandKind.DownloadLoader : PortalCommandKind.DownloadVanilla,
            Version = positionals[0],
            Loaders = options.Loaders,
            Folder = options.Folder,
            InstanceId = options.InstanceId
        };
        return PortalCliParseStatus.Command;
    }

    private static bool TryValidateProvider(string? provider, out string? error)
    {
        error = null;
        if (provider is null || provider.ToLowerInvariant() is "modrinth" or "curseforge") return true;
        error = $"未知的整合包平台“{provider}”，支持：modrinth / curseforge。";
        return false;
    }

    private static PortalCliParseStatus ParseLaunchCli(string[] args, out PortalCommand? command, out string? error)
    {
        command = null;
        if (!TryParseOptions(args, 1, out var positionals, out var options, out error))
            return PortalCliParseStatus.Error;
        if (positionals.Count != 1 || options.Loaders.Count > 0)
        {
            error = "launch 需要且仅需要一个参数：要启动的实例 ID（可加 --folder 指定文件夹）。";
            return PortalCliParseStatus.Error;
        }

        if (!string.IsNullOrWhiteSpace(options.WorldFolder) && !string.IsNullOrWhiteSpace(options.ServerAddress))
        {
            error = "--world 与 --server 不能同时指定。";
            return PortalCliParseStatus.Error;
        }
        if (options.ServerPort != null && string.IsNullOrWhiteSpace(options.ServerAddress))
        {
            error = "--port 需要配合 --server 使用。";
            return PortalCliParseStatus.Error;
        }

        command = new PortalCommand
        {
            Kind = PortalCommandKind.Launch,
            InstanceId = positionals[0],
            Folder = options.Folder,
            WorldFolder = options.WorldFolder,
            ServerAddress = options.ServerAddress,
            ServerPort = options.ServerPort
        };
        return PortalCliParseStatus.Command;
    }

    private sealed record CliOptions(string? Folder, string? InstanceId, List<PortalLoaderSpec> Loaders,
        string? Provider, string? PackVersion, string? WorldFolder, string? ServerAddress, int? ServerPort);

    private static bool TryParseOptions(string[] args, int start, out List<string> positionals, out CliOptions options,
        out string? error)
    {
        positionals = [];
        string? folder = null;
        string? instanceId = null;
        string? provider = null;
        string? packVersion = null;
        string? worldFolder = null;
        string? serverAddress = null;
        int? serverPort = null;
        var loaders = new List<PortalLoaderSpec>();
        options = null!;
        error = null;

        for (var index = start; index < args.Length; index++)
        {
            var token = args[index];
            string? inlineValue = null;
            var name = token;
            if (token.StartsWith('-'))
            {
                var equals = token.IndexOf('=');
                if (equals > 0)
                {
                    name = token[..equals];
                    inlineValue = token[(equals + 1)..];
                }
            }

            switch (name.ToLowerInvariant())
            {
                case "--folder" or "-f" or "--dir":
                    if (!TryReadValue(args, ref index, inlineValue, name, out folder, out error)) return false;
                    break;
                case "--id":
                    if (!TryReadValue(args, ref index, inlineValue, name, out instanceId, out error)) return false;
                    break;
                case "--loader" or "-l":
                    if (!TryReadValue(args, ref index, inlineValue, name, out var loaderValue, out error)) return false;
                    loaders.Add(ParseLoaderSpec(loaderValue!));
                    break;
                case "--from" or "--platform":
                    if (!TryReadValue(args, ref index, inlineValue, name, out provider, out error)) return false;
                    break;
                case "--version" or "-v" or "--file":
                    if (!TryReadValue(args, ref index, inlineValue, name, out packVersion, out error)) return false;
                    break;
                case "--world":
                    if (!TryReadValue(args, ref index, inlineValue, name, out worldFolder, out error)) return false;
                    break;
                case "--server":
                    if (!TryReadValue(args, ref index, inlineValue, name, out serverAddress, out error)) return false;
                    break;
                case "--port":
                    if (!TryReadValue(args, ref index, inlineValue, name, out var portValue, out error)) return false;
                    if (!int.TryParse(portValue, out var parsedPort) || parsedPort is <= 0 or > 65535)
                    {
                        error = $"参数 {name} 需要 1-65535 之间的端口号。";
                        return false;
                    }
                    serverPort = parsedPort;
                    break;
                default:
                    if (name.StartsWith('-'))
                    {
                        error = $"未知参数“{token}”。";
                        return false;
                    }
                    positionals.Add(token);
                    break;
            }
        }

        options = new CliOptions(folder, instanceId, loaders, provider, packVersion, worldFolder, serverAddress, serverPort);
        return true;
    }

    private static bool TryReadValue(string[] args, ref int index, string? inlineValue, string name, out string? value,
        out string? error)
    {
        error = null;
        if (inlineValue is not null)
        {
            value = inlineValue;
            return true;
        }
        if (index + 1 >= args.Length)
        {
            value = null;
            error = $"参数 {name} 缺少值。";
            return false;
        }
        value = args[++index];
        return true;
    }

    private static PortalLoaderSpec ParseLoaderSpec(string value)
    {
        var separator = value.IndexOf('@');
        return separator > 0
            ? new PortalLoaderSpec(value[..separator].Trim(), value[(separator + 1)..].Trim())
            : new PortalLoaderSpec(value.Trim(), null);
    }

        private static PortalCliParseStatus ParseUri(string raw, out PortalCommand? command, out string? error)
    {
        command = null;
        error = null;

        var rest = raw[(UriScheme.Length + 1)..];
        if (rest.StartsWith("//")) rest = rest[2..];
        string query = string.Empty;
        var queryIndex = rest.IndexOf('?');
        if (queryIndex >= 0)
        {
            query = rest[(queryIndex + 1)..];
            rest = rest[..queryIndex];
        }

        var segments = rest.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.UnescapeDataString).ToArray();
        var parameters = ParseQuery(query);

        if (segments.Length == 0)
        {
            error = "portal:// 链接缺少命令，例如 portal://launch?id=xxx。";
            return PortalCliParseStatus.Error;
        }

        switch (segments[0].ToLowerInvariant())
        {
            case "launch":
            {
                var id = segments.Length > 1 ? segments[1] : GetValue(parameters, "id", "instance");
                if (string.IsNullOrWhiteSpace(id))
                {
                    error = "portal://launch 缺少实例 ID，例如 portal://launch?id=xxx。";
                    return PortalCliParseStatus.Error;
                }
                var worldFolder = GetValue(parameters, "world");
                var serverAddress = GetValue(parameters, "server", "address");
                var serverPort = ParsePort(GetValue(parameters, "port"), out var portError);
                if (portError is not null)
                {
                    error = portError;
                    return PortalCliParseStatus.Error;
                }
                if (!string.IsNullOrWhiteSpace(worldFolder) && !string.IsNullOrWhiteSpace(serverAddress))
                {
                    error = "world 与 server 不能同时指定。";
                    return PortalCliParseStatus.Error;
                }
                if (serverPort != null && string.IsNullOrWhiteSpace(serverAddress))
                {
                    error = "port 需要配合 server 使用。";
                    return PortalCliParseStatus.Error;
                }
                command = new PortalCommand
                {
                    Kind = PortalCommandKind.Launch,
                    InstanceId = id,
                    Folder = GetValue(parameters, "folder", "dir"),
                    WorldFolder = worldFolder,
                    ServerAddress = serverAddress,
                    ServerPort = serverPort
                };
                return PortalCliParseStatus.Command;
            }
            case "install" or "download":
            {
                var sub = segments.Length > 1 ? segments[1].ToLowerInvariant() : GetValue(parameters, "type")?.ToLowerInvariant();
                switch (sub)
                {
                    case "vanilla" or "loader":
                    {
                        var version = GetValue(parameters, "version", "v");
                        if (string.IsNullOrWhiteSpace(version))
                        {
                            error = $"portal://install/{sub} 缺少 version 参数。";
                            return PortalCliParseStatus.Error;
                        }
                        var loaders = GetValues(parameters, "loader")
                            .SelectMany(value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                            .Select(ParseLoaderSpec).ToList();
                        if (sub == "loader" && loaders.Count == 0)
                        {
                            error = "portal://install/loader 至少需要一个 loader 参数。";
                            return PortalCliParseStatus.Error;
                        }
                        command = new PortalCommand
                        {
                            Kind = loaders.Count > 0 ? PortalCommandKind.DownloadLoader : PortalCommandKind.DownloadVanilla,
                            Version = version,
                            Loaders = loaders,
                            Folder = GetValue(parameters, "folder", "dir"),
                            InstanceId = GetValue(parameters, "id")
                        };
                        return PortalCliParseStatus.Command;
                    }
                    case "modpack":
                    {
                        var source = GetValue(parameters, "source", "url", "path", "project");
                        if (string.IsNullOrWhiteSpace(source))
                        {
                            error = "portal://install/modpack 缺少 source（整合包路径、链接或项目名称/ID）参数。";
                            return PortalCliParseStatus.Error;
                        }
                        var provider = GetValue(parameters, "from", "platform");
                        if (!TryValidateProvider(provider, out error)) return PortalCliParseStatus.Error;
                        command = new PortalCommand
                        {
                            Kind = PortalCommandKind.DownloadModpack,
                            Source = source,
                            Provider = provider?.ToLowerInvariant(),
                            PackVersion = GetValue(parameters, "version", "v", "file"),
                            Folder = GetValue(parameters, "folder", "dir"),
                            InstanceId = GetValue(parameters, "id")
                        };
                        return PortalCliParseStatus.Command;
                    }
                    default:
                        error = "portal://install 需要子命令 vanilla / loader / modpack。";
                        return PortalCliParseStatus.Error;
                }
            }
            default:
                error = $"未知的 portal:// 命令“{segments[0]}”，支持 install / launch。";
                return PortalCliParseStatus.Error;
        }
    }

    private static List<KeyValuePair<string, string>> ParseQuery(string query)
    {
        var result = new List<KeyValuePair<string, string>>();
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            var key = separator >= 0 ? pair[..separator] : pair;
            var value = separator >= 0 ? pair[(separator + 1)..] : string.Empty;
            
            result.Add(new KeyValuePair<string, string>(
                Uri.UnescapeDataString(key).Trim(),
                Uri.UnescapeDataString(value)));
        }
        return result;
    }

    private static string? GetValue(List<KeyValuePair<string, string>> parameters, params string[] names) =>
        parameters.FirstOrDefault(pair => names.Contains(pair.Key, StringComparer.OrdinalIgnoreCase)).Value is
            { Length: > 0 } value
            ? value
            : null;

    private static IEnumerable<string> GetValues(List<KeyValuePair<string, string>> parameters, string name) =>
        parameters.Where(pair => string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair.Value).Where(value => value.Length > 0);

    private static int? ParsePort(string? value, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (int.TryParse(value, out var port) && port is > 0 and <= 65535)
            return port;
        error = $"port 需要 1-65535 之间的端口号，收到“{value}”。";
        return null;
    }
}
