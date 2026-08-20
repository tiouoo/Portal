using Portal.Localization;

namespace Portal.Core.Module.Ipc;

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
            "launch" or "--launch" => ParseLaunchCli(args, out command, out error),
            _ => PortalCliParseStatus.NotACommand
        };
    }

    public static string GetUsageText()
    {
        return CommonLanguageManager.Instance.ipc_usageText.CurrentValue();
    }

    public static string GetHeadlessUsageText()
    {
        return CommonLanguageManager.Instance.desktop_cli_usageText.CurrentValue();
    }

    private static PortalCliParseStatus ParseInstallCli(string[] args, out PortalCommand? command, out string? error)
    {
        command = null;
        error = null;
        if (args.Length < 2)
        {
            error = CommonLanguageManager.Instance.ipc_installNeedsSubcommand.CurrentValue();
            return PortalCliParseStatus.Error;
        }

        var sub = args[1].ToLowerInvariant();
        if (sub is not ("vanilla" or "loader" or "modpack"))
        {
            error = string.Format(CommonLanguageManager.Instance.ipc_unknownInstallSubcommand.CurrentValue(), args[1]);
            return PortalCliParseStatus.Error;
        }

        if (!TryParseOptions(args, 2, out var positionals, out var options, out error))
            return PortalCliParseStatus.Error;
        if (positionals.Count != 1)
        {
            error = sub == "modpack"
                ? CommonLanguageManager.Instance.ipc_installModpackNeedsOneArg.CurrentValue()
                : string.Format(CommonLanguageManager.Instance.ipc_installVersionNeedsOneArg.CurrentValue(), sub);
            return PortalCliParseStatus.Error;
        }

        if (sub == "modpack")
        {
            if (options.Loaders.Count > 0)
            {
                error = CommonLanguageManager.Instance.ipc_installModpackNoLoader.CurrentValue();
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
            error = CommonLanguageManager.Instance.ipc_fromVersionOnlyForModpack.CurrentValue();
            return PortalCliParseStatus.Error;
        }

        if (sub == "loader" && options.Loaders.Count == 0)
        {
            error = CommonLanguageManager.Instance.ipc_installLoaderNeedsLoader.CurrentValue();
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
        error = string.Format(CommonLanguageManager.Instance.ipc_unknownProvider.CurrentValue(), provider);
        return false;
    }

    private static PortalCliParseStatus ParseLaunchCli(string[] args, out PortalCommand? command, out string? error)
    {
        command = null;
        if (!TryParseOptions(args, 1, out var positionals, out var options, out error))
            return PortalCliParseStatus.Error;
        if (positionals.Count != 1 || options.Loaders.Count > 0)
        {
            error = CommonLanguageManager.Instance.ipc_launchNeedsOneArg.CurrentValue();
            return PortalCliParseStatus.Error;
        }

        if (!string.IsNullOrWhiteSpace(options.WorldFolder) && !string.IsNullOrWhiteSpace(options.ServerAddress))
        {
            error = CommonLanguageManager.Instance.ipc_worldServerMutuallyExclusive.CurrentValue();
            return PortalCliParseStatus.Error;
        }

        if (options.ServerPort != null && string.IsNullOrWhiteSpace(options.ServerAddress))
        {
            error = CommonLanguageManager.Instance.ipc_portNeedsServer.CurrentValue();
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
                        error = string.Format(CommonLanguageManager.Instance.ipc_invalidPortRange.CurrentValue(), name);
                        return false;
                    }

                    serverPort = parsedPort;
                    break;
                default:
                    if (name.StartsWith('-'))
                    {
                        error = string.Format(CommonLanguageManager.Instance.ipc_unknownArgument.CurrentValue(), token);
                        return false;
                    }

                    positionals.Add(token);
                    break;
            }
        }

        options = new CliOptions(folder, instanceId, loaders, provider, packVersion, worldFolder, serverAddress,
            serverPort);
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
            error = string.Format(CommonLanguageManager.Instance.ipc_missingArgumentValue.CurrentValue(), name);
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
        var query = string.Empty;
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
            error = CommonLanguageManager.Instance.ipc_uriMissingCommand.CurrentValue();
            return PortalCliParseStatus.Error;
        }

        switch (segments[0].ToLowerInvariant())
        {
            case "launch":
            {
                var id = segments.Length > 1 ? segments[1] : GetValue(parameters, "id", "instance");
                if (string.IsNullOrWhiteSpace(id))
                {
                    error = CommonLanguageManager.Instance.ipc_uriLaunchMissingId.CurrentValue();
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
                    error = CommonLanguageManager.Instance.ipc_worldServerMutuallyExclusiveUri.CurrentValue();
                    return PortalCliParseStatus.Error;
                }

                if (serverPort != null && string.IsNullOrWhiteSpace(serverAddress))
                {
                    error = CommonLanguageManager.Instance.ipc_portNeedsServerUri.CurrentValue();
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
                var sub = segments.Length > 1
                    ? segments[1].ToLowerInvariant()
                    : GetValue(parameters, "type")?.ToLowerInvariant();
                switch (sub)
                {
                    case "vanilla" or "loader":
                    {
                        var version = GetValue(parameters, "version", "v");
                        if (string.IsNullOrWhiteSpace(version))
                        {
                            error = string.Format(CommonLanguageManager.Instance.ipc_uriInstallMissingVersion.CurrentValue(), sub);
                            return PortalCliParseStatus.Error;
                        }

                        var loaders = GetValues(parameters, "loader")
                            .SelectMany(value => value.Split(',',
                                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                            .Select(ParseLoaderSpec).ToList();
                        if (sub == "loader" && loaders.Count == 0)
                        {
                            error = CommonLanguageManager.Instance.ipc_uriInstallLoaderNeedsLoader.CurrentValue();
                            return PortalCliParseStatus.Error;
                        }

                        command = new PortalCommand
                        {
                            Kind = loaders.Count > 0
                                ? PortalCommandKind.DownloadLoader
                                : PortalCommandKind.DownloadVanilla,
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
                            error = CommonLanguageManager.Instance.ipc_uriInstallModpackMissingSource.CurrentValue();
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
                        error = CommonLanguageManager.Instance.ipc_uriInstallNeedsSubcommand.CurrentValue();
                        return PortalCliParseStatus.Error;
                }
            }
            default:
                error = string.Format(CommonLanguageManager.Instance.ipc_uriUnknownCommand.CurrentValue(), segments[0]);
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

    private static string? GetValue(List<KeyValuePair<string, string>> parameters, params string[] names)
    {
        return parameters.FirstOrDefault(pair => names.Contains(pair.Key, StringComparer.OrdinalIgnoreCase)).Value is
            { Length: > 0 } value
            ? value
            : null;
    }

    private static IEnumerable<string> GetValues(List<KeyValuePair<string, string>> parameters, string name)
    {
        return parameters.Where(pair => string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair.Value).Where(value => value.Length > 0);
    }

    private static int? ParsePort(string? value, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (int.TryParse(value, out var port) && port is > 0 and <= 65535)
            return port;
        error = string.Format(CommonLanguageManager.Instance.ipc_invalidPortRangeReceived.CurrentValue(), value);
        return null;
    }

    private sealed record CliOptions(
        string? Folder,
        string? InstanceId,
        List<PortalLoaderSpec> Loaders,
        string? Provider,
        string? PackVersion,
        string? WorldFolder,
        string? ServerAddress,
        int? ServerPort);
}