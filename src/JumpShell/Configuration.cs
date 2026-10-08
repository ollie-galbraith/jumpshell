using System.Globalization;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace JumpShell;

internal sealed class AppConfiguration
{
    public GlobalConfiguration Configuration { get; set; } = new();
    public Dictionary<string, HostConfiguration> Connections { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class GlobalConfiguration
{
    public bool UsePasswordState { get; set; }
    public string? PasswordList { get; set; }
    public string? GlobalSshKeypairId { get; set; }
    public bool SeparateSshKeyPairs { get; set; }
    public string WindowsConnectionMethod { get; set; } = "ssh-shell";
    public MenuConfiguration MenuOptions { get; set; } = new();
    public List<string> Usernames { get; set; } = [];
    public List<UserConfiguration> Credentials { get; set; } = [];
}

internal sealed class MenuConfiguration
{
    public int Columns { get; set; } = 6;
    public string TitleColor { get; set; } = "#15dbad";
    public string SelectionColor { get; set; } = "#ff0000";
    public string BorderColor { get; set; } = "#054873";
    public string TextColor { get; set; } = "#808080";
}

internal sealed class UserConfiguration
{
    public string Username { get; set; } = "";
    public string? PasswordId { get; set; }
}

internal sealed class HostConfiguration
{
    public string OperatingSystem { get; set; } = "Linux";
    public string? DefaultUser { get; set; }
    public string? Alias { get; set; }
    public string? SshKey { get; set; }
    public List<string> AdditionalUsers { get; set; } = [];
    public string ConnectionMethod { get; set; } = "SSH";
    public string? WindowsConnectionMethod { get; set; }
    public TerminalColorConfiguration? TerminalColor { get; set; }
    public string? PasswordId { get; set; }
    public List<string> PublicKeySet { get; set; } = [];
}

internal sealed class TerminalColorConfiguration
{
    public string? Background { get; set; }
}

internal sealed class ConfigurationStore
{
    private readonly ISerializer _serializer = new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull | DefaultValuesHandling.OmitEmptyCollections)
        .Build();

    public string Path { get; }

    public ConfigurationStore()
    {
        Path = System.IO.Path.Combine(PlatformPaths.UserConfigurationDirectory, "JumpShell", "config.yaml");
    }

    public AppConfiguration Load()
    {
        if (!File.Exists(Path))
        {
            var initial = new AppConfiguration();
            Save(initial);
            return initial;
        }

        var yaml = new YamlStream();
        using (var reader = File.OpenText(Path))
        {
            yaml.Load(reader);
        }

        if (yaml.Documents.Count == 0 || yaml.Documents[0].RootNode is not YamlMappingNode root)
        {
            return new AppConfiguration();
        }

        var result = new AppConfiguration();
        var configNode = Find(root, "configuration") as YamlMappingNode;
        if (configNode is not null)
        {
            result.Configuration.UsePasswordState = ReadBool(configNode, "use_password_state");
            result.Configuration.PasswordList = OptionalScalar(Find(configNode, "password_list"));
            result.Configuration.GlobalSshKeypairId = OptionalScalar(Find(configNode, "global_ssh_keypair_id"));
            result.Configuration.SeparateSshKeyPairs = ReadBool(configNode, "seperate_ssh_key_pairs") || ReadBool(configNode, "separate_ssh_key_pairs");
            result.Configuration.WindowsConnectionMethod = ReadString(configNode, "windows_connection_method", "ssh-shell");
            result.Configuration.MenuOptions.Columns = ReadInt(configNode, "menu_options", "columns", 6);
            result.Configuration.MenuOptions.TitleColor = ReadString(configNode, "menu_options", "title_color", result.Configuration.MenuOptions.TitleColor);
            result.Configuration.MenuOptions.SelectionColor = ReadString(configNode, "menu_options", "selection_color", result.Configuration.MenuOptions.SelectionColor);
            result.Configuration.MenuOptions.BorderColor = ReadString(configNode, "menu_options", "border_color", result.Configuration.MenuOptions.BorderColor);
            result.Configuration.MenuOptions.TextColor = ReadString(configNode, "menu_options", "text_color", result.Configuration.MenuOptions.TextColor);

            var usernames = Find(configNode, "usernames") as YamlSequenceNode;
            if (usernames is not null)
            {
                result.Configuration.Usernames.AddRange(usernames.Children.OfType<YamlScalarNode>()
                    .Select(node => node.Value).OfType<string>().Where(value => !string.IsNullOrWhiteSpace(value)));
            }

            var credentials = Find(configNode, "credentials") as YamlSequenceNode;
            if (credentials is not null)
            {
                foreach (var credential in credentials.Children.OfType<YamlMappingNode>())
                {
                    var username = Scalar(Find(credential, "username"));
                    if (!string.IsNullOrWhiteSpace(username))
                    {
                        result.Configuration.Usernames.Add(username);
                        result.Configuration.Credentials.Add(new UserConfiguration
                        {
                            Username = username,
                            PasswordId = OptionalScalar(Find(credential, "password_id"))
                        });
                    }
                }
            }
        }

        ReadConnections(Find(root, "connections"), result.Connections);
        result.Configuration.Usernames = result.Configuration.Usernames
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
        return result;
    }

    public void Save(AppConfiguration configuration)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.WriteAllText(Path, _serializer.Serialize(configuration));
    }

    private void ReadConnections(YamlNode? node, Dictionary<string, HostConfiguration> connections)
    {
        if (node is YamlMappingNode mapping)
        {
            foreach (var entry in mapping.Children)
            {
                var hostname = Scalar(entry.Key);
                if (!string.IsNullOrWhiteSpace(hostname))
                {
                    connections[hostname] = ReadHost(entry.Value as YamlMappingNode);
                }
            }
        }
        else if (node is YamlSequenceNode sequence)
        {
            foreach (var hostEntry in sequence.Children.OfType<YamlMappingNode>())
            {
                foreach (var entry in hostEntry.Children)
                {
                    var hostname = Scalar(entry.Key);
                    if (!string.IsNullOrWhiteSpace(hostname))
                    {
                        connections[hostname] = ReadHost(entry.Value as YamlMappingNode);
                    }
                }
            }
        }
    }

    private HostConfiguration ReadHost(YamlMappingNode? node)
    {
        if (node is null)
        {
            return new HostConfiguration();
        }

        var host = new HostConfiguration
        {
            OperatingSystem = ReadString(node, "operating_system", "Linux"),
            DefaultUser = OptionalScalar(Find(node, "default_user")),
            Alias = OptionalScalar(Find(node, "alias")),
            SshKey = OptionalScalar(Find(node, "ssh_key")),
            ConnectionMethod = OptionalScalar(Find(node, "connection_method")) ?? "SSH",
            WindowsConnectionMethod = OptionalScalar(Find(node, "windows_connection_method")),
            PasswordId = OptionalScalar(Find(node, "password_id"))
        };

        if (Find(node, "additional_users") is YamlSequenceNode additionalUsers)
        {
            host.AdditionalUsers = additionalUsers.Children.OfType<YamlScalarNode>()
                .Select(item => item.Value).OfType<string>()
                .Where(value => !string.IsNullOrWhiteSpace(value)).ToList();
        }

        var terminalColor = Find(node, "terminal_color") as YamlMappingNode;
        var background = OptionalScalar(Find(terminalColor, "background"));
        host.TerminalColor = background is null ? null : new TerminalColorConfiguration { Background = background };

        if (Find(node, "public_key_set") is YamlSequenceNode publicKeySet)
        {
            host.PublicKeySet = publicKeySet.Children.OfType<YamlScalarNode>()
                .Select(item => item.Value).OfType<string>().Where(value => !string.IsNullOrWhiteSpace(value)).ToList();
        }

        return host;
    }

    private static YamlNode? Find(YamlMappingNode? node, string key) =>
        node?.Children.FirstOrDefault(pair => string.Equals(Scalar(pair.Key), key, StringComparison.OrdinalIgnoreCase)).Value;

    private static string? Scalar(YamlNode? node) => (node as YamlScalarNode)?.Value;

    private static string? OptionalScalar(YamlNode? node) =>
        Scalar(node) is { Length: > 0 } value ? value : null;

    private static string ReadString(YamlMappingNode node, string key, string fallback) =>
        Scalar(Find(node, key)) is { Length: > 0 } value ? value : fallback;

    private static string ReadString(YamlMappingNode node, string parentKey, string key, string fallback) =>
        Find(node, parentKey) is YamlMappingNode parent ? ReadString(parent, key, fallback) : fallback;

    private static bool ReadBool(YamlMappingNode node, string key) =>
        bool.TryParse(Scalar(Find(node, key)), out var value) && value;

    private static int ReadInt(YamlMappingNode node, string parentKey, string key, int fallback) =>
        Find(node, parentKey) is YamlMappingNode parent &&
        int.TryParse(Scalar(Find(parent, key)), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
}

internal static class PlatformPaths
{
    public static string UserConfigurationDirectory
    {
        get
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(home))
            {
                home = Environment.GetEnvironmentVariable("HOME") ?? Environment.CurrentDirectory;
            }

            if (OperatingSystem.IsWindows())
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return string.IsNullOrWhiteSpace(appData)
                    ? System.IO.Path.Combine(home, "AppData", "Roaming")
                    : appData;
            }

            if (OperatingSystem.IsMacOS())
            {
                return System.IO.Path.Combine(home, "Library", "Application Support");
            }

            var xdgConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            return string.IsNullOrWhiteSpace(xdgConfigHome)
                ? System.IO.Path.Combine(home, ".config")
                : xdgConfigHome;
        }
    }
}