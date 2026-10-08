using ConsoleFX;
using System.Net.NetworkInformation;

namespace JumpShell;

internal static class Program
{
    private enum MainMenuShortcut
    {
        None,
        Add,
        Remove,
        Connect,
        Help
    }

    private static readonly ConfigurationStore Store = new();
    private static AppConfiguration _configuration = new();

    private static readonly string Version = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    private static async Task Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--version")
        {
            Console.WriteLine($"Jumpshell v{Version}");
            return;
        }

        Console.CancelKeyPress += (_, _) => Console.Write(Screen.AlternativeBuffer.Reset);
        Console.Write(Screen.AlternativeBuffer.Enable);
        try
        {
            try
            {
                _configuration = Store.Load();
                while (true)
                {
                    Console.Clear();
                    var (hostChoice, shortcut, hostMenuUnderlay) = SelectHost();
                    if (shortcut == MainMenuShortcut.Add)
                    {
                        AddHost(hostMenuUnderlay);
                        continue;
                    }

                    if (shortcut == MainMenuShortcut.Help)
                    {
                        ShowHelp(hostMenuUnderlay);
                        continue;
                    }

                    var selectedHost = hostChoice is null ? null : FindHostName(hostChoice);
                    if (shortcut == MainMenuShortcut.Remove)
                    {
                        if (selectedHost is not null)
                        {
                            RemoveHost(selectedHost, hostMenuUnderlay);
                        }
                        continue;
                    }

                    if (shortcut == MainMenuShortcut.Connect)
                    {
                        if (selectedHost is not null)
                        {
                            await ShowConnectionOptionsAsync(selectedHost, hostMenuUnderlay);
                        }
                        continue;
                    }

                    if (hostChoice is null)
                    {
                        return;
                    }

                    switch (hostChoice)
                    {
                        default:
                            var hostname = selectedHost;
                            if (hostname is not null)
                            {
                                await ConnectAsync(hostname);
                            }
                            break;
                    }
                }
            }
            catch (Exception exception)
            {
                Console.WriteLine($"JumpShell error: {exception.Message}");
                Console.WriteLine("Press any key to exit.");
                Console.ReadKey(true);
            }
        }
        finally
        {
            Console.Write(Screen.AlternativeBuffer.Reset);
        }
    }

    private static (string? Selection, MainMenuShortcut Shortcut, GridMenu? Underlay) SelectHost()
    {
        var items = _configuration.Connections.Keys.Order(StringComparer.OrdinalIgnoreCase)
            .Select(host => host).ToList();
        if (items.Count == 0)
        {
            items.Add("No saved hosts");
        }

        const string title = "Hosts";
        var footerText = $"? to show help   Esc to exit   v{Version}";
        var menu = CreateMenu(
            title,
            items,
            _configuration.Configuration.MenuOptions.Columns,
            footerText: footerText);
        var shortcut = MainMenuShortcut.None;
        menu.ShortcutHandler = keyInfo =>
        {
            shortcut = keyInfo.Key switch
            {
                ConsoleKey.A => MainMenuShortcut.Add,
                ConsoleKey.C => MainMenuShortcut.Connect,
                ConsoleKey.R or ConsoleKey.Delete => MainMenuShortcut.Remove,
                _ when keyInfo.KeyChar == '?' => MainMenuShortcut.Help,
                _ => MainMenuShortcut.None
            };
            return shortcut != MainMenuShortcut.None;
        };
        var selection = menu.Run();
        var underlay = menu.WasShortcutPressed
            ? CreateMenu(
                title,
                items,
                _configuration.Configuration.MenuOptions.Columns,
                selectedItem: selection,
                isDimmed: true,
                footerText: footerText)
            : null;
        return (menu.WasCancelled ? null : selection, shortcut, underlay);
    }

    private static void AddHost(GridMenu? underlay)
    {
        const string hostnamePrompt = "Enter hostname:";
        var hostname = ReadText(
            hostnamePrompt,
            underlay,
            startRow: Math.Max(0, (Console.WindowHeight - 3) / 2),
            borderInset: 2,
            padding: 2);
        if (string.IsNullOrWhiteSpace(hostname) || FindHostName(hostname) is not null)
        {
            return;
        }

        const string operatingSystemTitle = "Select operating system";
        List<string> operatingSystems = ["Linux", "Windows"];
        var operatingSystem = SelectMenu(
            operatingSystemTitle,
            operatingSystems,
            2,
            underlay,
            startRow: Math.Max(0, (Console.WindowHeight - 3) / 2),
            borderInset: GetCenteredBorderInset(operatingSystemTitle, operatingSystems, padding: 2),
            padding: 2);
        if (operatingSystem is null)
        {
            return;
        }

        try
        {
            using var ping = new Ping();
            var reply = ping.Send(hostname, 2000);
            if (reply.Status != IPStatus.Success && !Confirm($"Unable to ping {hostname}. Continue connecting?"))
            {
                return;
            }
        }
        catch (PingException)
        {
            if (!Confirm($"Unable to ping {hostname}. Continue connecting?"))
            {
                return;
            }
        }

        _configuration.Connections[hostname] = new HostConfiguration { OperatingSystem = operatingSystem };
        Store.Save(_configuration);
    }

    private static void RemoveHost(string hostname, GridMenu? underlay)
    {
        var title = $"Remove {hostname}?";
        var confirmation = SelectMenu(
            title,
            ["Cancel", "Remove"],
            1,
            underlay,
            selectedItem: "Cancel",
            startRow: Math.Max(0, (Console.WindowHeight - 4) / 2),
            borderInset: GetCenteredBorderInset(title, ["Cancel", "Remove"], padding: 2),
            padding: 2);
        if (confirmation == "Remove")
        {
            _configuration.Connections.Remove(hostname);
            Store.Save(_configuration);
        }
    }

    private static void ShowHelp(GridMenu? underlay)
    {
        const string title = "JumpShell Help";
        var items = new List<string>
        {
            "A  Add a hostname",
            "R / Delete  Remove selected hostname",
            "C  Open selected host options",
            "?  Show this help",
            "Enter  Connect to selected host",
            "Esc  Exit JumpShell",
            "Close"
        };
        SelectMenu(
            title,
            items,
            1,
            underlay,
            selectedItem: "Close",
            startRow: Math.Max(0, (Console.WindowHeight - items.Count - 2) / 2),
            borderInset: GetCenteredBorderInset(title, items));
    }

    private static int GetCenteredBorderInset(string title, List<string> items, int padding = 1)
    {
        var titleWidth = title.Length + (2 * padding) + 4;
        var contentWidth = items.Max(item => item.Length) + (2 * padding) + 2;
        var popupWidth = Math.Max(titleWidth, contentWidth);
        return Math.Max(0, (Console.WindowWidth - popupWidth) / 2);
    }

    private static async Task ShowConnectionOptionsAsync(string hostname, GridMenu? underlay)
    {
        var host = _configuration.Connections[hostname];
        var actions = new List<string>
        {
            "Connect",
        };
        if (!host.OperatingSystem.Equals("Windows", StringComparison.OrdinalIgnoreCase))
        {
            actions.Add("Connect via SSH");
            actions.Add("Connect via VSCode");
            actions.Add("Set connection method");
        }

        actions.Add("Clear saved SSH host key");
        actions.Add("Set or clear SSH key");
        actions.Add("Add user for this connection");
        
        if (host.AdditionalUsers.Count > 0)
        {
            actions.Add("Remove user from this connection");
        }

        actions.Add("Set connection default user");
        actions.Add("Add global username");

        if (_configuration.Configuration.Usernames.Count > 0)
        {
            actions.Add("Remove global username");
        }

        actions.Add("Open config file");
        actions.Add("Return");
        var title = $"Options: {hostname}";

        while (true)
        {
            var action = SelectMenu(
            title,
                actions,
                1,
                underlay: underlay,
            startRow: Math.Max(0, (Console.WindowHeight - actions.Count - 2) / 2),
            borderInset: GetCenteredBorderInset(title, actions, padding: 2),
            padding: 2);
            switch (action)
            {
                case "Connect":
                    await ConnectAsync(hostname);
                    return;
                case "Connect via SSH":
                    await ConnectAsync(hostname, "SSH");
                    return;
                case "Connect via VSCode":
                    await ConnectAsync(hostname, "VSCode");
                    return;
                case "Add user for this connection":
                    AddConnectionUser(host);
                    break;
                case "Add global username":
                    AddGlobalUsername();
                    break;
                case "Set connection default user":
                    SetDefaultUser(hostname, host);
                    break;
                case "Set connection method":
                    SetConnectionMethod(host);
                    break;
                case "Remove user from this connection":
                    RemoveConnectionUser(host);
                    break;
                case "Remove global username":
                    RemoveGlobalUsername();
                    break;
                case "Open config file":
                    await OpenConfigAsync();
                    break;
                case "Clear saved SSH host key":
                    await ClearKnownHostKeyAsync(hostname, underlay);
                    break;
                case "Set or clear SSH key":
                    SetSshKey(host);
                    break;
                default:
                    return;
            }
        }
    }

    private static async Task ClearKnownHostKeyAsync(string hostname, GridMenu? underlay)
    {
        var title = $"Clear saved SSH key for {hostname}?";
        var confirmation = SelectMenu(
            title,
            ["Cancel", "Clear key"],
            1,
            underlay,
            selectedItem: "Cancel",
            startRow: Math.Max(0, (Console.WindowHeight - 4) / 2),
            borderInset: GetCenteredBorderInset(title, ["Cancel", "Clear key"], padding: 2),
            padding: 2);
        if (confirmation != "Clear key")
        {
            return;
        }

        await ExternalCommands.ClearKnownHostKeyAsync(hostname);
    }

    private static async Task ConnectAsync(string hostname, string? methodOverride = null)
    {
        var host = _configuration.Connections[hostname];
        var username = SelectUser(hostname, host);
        if (username is null)
        {
            return;
        }

        if (host.OperatingSystem.Equals("Windows", StringComparison.OrdinalIgnoreCase))
        {
            Console.Clear();
            var connectionMethod = host.WindowsConnectionMethod ?? _configuration.Configuration.WindowsConnectionMethod;
            if (connectionMethod.Equals("powershell-remoting", StringComparison.OrdinalIgnoreCase))
            {
                await ExternalCommands.ConnectPowerShellRemotingAsync(hostname, username);
            }
            else if (connectionMethod.Equals("ssh-shell", StringComparison.OrdinalIgnoreCase))
            {
                var exitCode = await ExternalCommands.ConnectRemotePowerShellShellAsync(hostname, username, host);
                if (exitCode != 0)
                {
                    ShowMessage($"SSH session to {hostname} exited with code {exitCode}.", clearScreen: false);
                }
            }
            else
            {
                ShowMessage($"Unsupported Windows connection method '{connectionMethod}'. Use 'ssh-shell' or 'powershell-remoting'.");
            }
            return;
        }

        var connectionType = methodOverride ?? host.ConnectionMethod;
        if (connectionType is null ||
            (!connectionType.Equals("SSH", StringComparison.OrdinalIgnoreCase) &&
             !connectionType.Equals("VSCode", StringComparison.OrdinalIgnoreCase)))
        {
            connectionType = SelectMenu(
                $"Connect to {hostname} via...",
                ["SSH", "VSCode"],
                2,
                selectedItem: host.ConnectionMethod);
            if (connectionType is null)
            {
                return;
            }

            host.ConnectionMethod = connectionType;
            Store.Save(_configuration);
        }

        if (connectionType.Equals("SSH", StringComparison.OrdinalIgnoreCase))
        {
            if (_configuration.Configuration.UsePasswordState)
            {
                ShowMessage("PasswordState support is not part of this build yet. Set configuration.use_password_state to false to use your normal OpenSSH identities.");
                return;
            }

            Console.Clear();
            var exitCode = await ExternalCommands.ConnectSshAsync(hostname, username, host);
            if (exitCode != 0)
            {
                ShowMessage($"SSH session to {hostname} exited with code {exitCode}.", clearScreen: false);
            }
            return;
        }

        await OpenVsCodeAsync(hostname, username);
    }

    private static string? SelectUser(string hostname, HostConfiguration host)
    {
        if (!string.IsNullOrWhiteSpace(host.DefaultUser))
        {
            return host.DefaultUser;
        }

        var assignedUsers = GetAssignedUsers(host);
        if (assignedUsers.Count == 1)
        {
            return assignedUsers[0];
        }

        while (true)
        {
            assignedUsers = GetAssignedUsers(host);
            var items = assignedUsers.ToList();
            items.Add("Add user for this connection");
            items.Add("Add global username");
            if (host.AdditionalUsers.Count > 0)
            {
                items.Add("Remove user from this connection");
            }
            if (_configuration.Configuration.Usernames.Count > 0)
            {
                items.Add("Remove global username");
            }

            var selected = SelectMenu(
                $"Select user for {hostname}",
                items,
                Math.Max(1, items.Count),
                selectedItem: host.DefaultUser);
            if (selected is null)
            {
                return null;
            }

            if (selected == "Add user for this connection")
            {
                var username = AddConnectionUser(host);
                if (username is not null)
                {
                    return username;
                }
            }
            else if (selected == "Add global username")
            {
                return AddGlobalUsername();
            }
            else if (selected == "Remove user from this connection")
            {
                RemoveConnectionUser(host);
            }
            else if (selected == "Remove global username")
            {
                RemoveGlobalUsername();
            }
            else
            {
                return selected;
            }
        }
    }

    private static List<string> GetAssignedUsers(HostConfiguration host) =>
        _configuration.Configuration.Usernames
            .Concat(host.AdditionalUsers)
            .Append(host.DefaultUser ?? "")
            .Where(username => !string.IsNullOrWhiteSpace(username))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string? AddConnectionUser(HostConfiguration host)
    {
        var username = ReadText("Enter username for this connection:").Trim();
        if (string.IsNullOrWhiteSpace(username))
        {
            return null;
        }

        if (!GetAssignedUsers(host).Contains(username, StringComparer.OrdinalIgnoreCase))
        {
            host.AdditionalUsers.Add(username);
            Store.Save(_configuration);
        }

        return username;
    }

    private static string? AddGlobalUsername()
    {
        var username = ReadText("Enter global username:").Trim();
        if (string.IsNullOrWhiteSpace(username))
        {
            return null;
        }

        if (!_configuration.Configuration.Usernames.Contains(username, StringComparer.OrdinalIgnoreCase))
        {
            _configuration.Configuration.Usernames.Add(username);
            _configuration.Configuration.Usernames = _configuration.Configuration.Usernames
                .Order(StringComparer.OrdinalIgnoreCase).ToList();
            Store.Save(_configuration);
        }

        return username;
    }

    private static void SetDefaultUser(string hostname, HostConfiguration host)
    {
        var users = GetAssignedUsers(host);
        var username = users.Count switch
        {
            0 => AddConnectionUser(host),
            1 => users[0],
            _ => SelectMenu($"Default user for {hostname}", users, users.Count, selectedItem: host.DefaultUser)
        };

        if (username is not null)
        {
            host.DefaultUser = username;
            Store.Save(_configuration);
        }
    }

    private static void SetConnectionMethod(HostConfiguration host)
    {
        var method = SelectMenu(
            "Default connection method",
            ["SSH", "VSCode"],
            2,
            selectedItem: host.ConnectionMethod);
        if (method is not null)
        {
            host.ConnectionMethod = method;
            Store.Save(_configuration);
        }
    }

    private static void SetSshKey(HostConfiguration host)
    {
        var currentKey = host.SshKey ?? "not set";
        var path = ReadText($"SSH private key path (blank clears; current: {currentKey}):").Trim();
        host.SshKey = string.IsNullOrWhiteSpace(path) ? null : path;
        Store.Save(_configuration);
    }

    private static void RemoveConnectionUser(HostConfiguration host)
    {
        var username = SelectMenu(
            "User to remove from this connection",
            host.AdditionalUsers,
            Math.Max(1, host.AdditionalUsers.Count));
        if (username is null)
        {
            return;
        }

        host.AdditionalUsers.RemoveAll(item => item.Equals(username, StringComparison.OrdinalIgnoreCase));
        if (host.DefaultUser?.Equals(username, StringComparison.OrdinalIgnoreCase) == true &&
            !_configuration.Configuration.Usernames.Contains(username, StringComparer.OrdinalIgnoreCase))
        {
            host.DefaultUser = null;
        }
        Store.Save(_configuration);
    }

    private static void RemoveGlobalUsername()
    {
        var username = SelectMenu(
            "Global username to remove",
            _configuration.Configuration.Usernames,
            Math.Max(1, _configuration.Configuration.Usernames.Count));
        if (username is null)
        {
            return;
        }

        _configuration.Configuration.Usernames.RemoveAll(item => item.Equals(username, StringComparison.OrdinalIgnoreCase));
        _configuration.Configuration.Credentials.RemoveAll(item => item.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
        foreach (var host in _configuration.Connections.Values)
        {
            if (host.DefaultUser?.Equals(username, StringComparison.OrdinalIgnoreCase) == true &&
                !host.AdditionalUsers.Contains(username, StringComparer.OrdinalIgnoreCase))
            {
                host.DefaultUser = null;
            }
        }
        Store.Save(_configuration);
    }

    private static async Task OpenVsCodeAsync(string hostname, string username)
    {
        var workspaces = ExternalCommands.FindVsCodeWorkspaces(hostname);
        if (workspaces.Count == 0)
        {
            await ExternalCommands.OpenVsCodeAsync(hostname.ToLowerInvariant(), username.ToLowerInvariant());
            return;
        }

        var items = workspaces.Select(workspace => workspace.Name).ToList();
        items.Add("New Workspace");
        var selected = SelectMenu("Open Workspace", items, Math.Max(1, items.Count));
        if (selected is null)
        {
            return;
        }

        var workspace = workspaces.FirstOrDefault(item => item.Name == selected);
        await ExternalCommands.OpenVsCodeAsync(hostname.ToLowerInvariant(), username.ToLowerInvariant(), workspace?.Path);
    }

    private static async Task OpenConfigAsync()
    {
        var editor = Environment.GetEnvironmentVariable("EDITOR");
        if (!string.IsNullOrWhiteSpace(editor))
        {
            await ExternalCommands.RunAsync(editor, [Store.Path]);
        }
        else if (OperatingSystem.IsWindows())
        {
            await ExternalCommands.RunAsync("notepad", [Store.Path]);
        }
        else
        {
            await ExternalCommands.RunAsync("code", [Store.Path]);
        }
    }

    private static string? FindHostName(string displayName) =>
        _configuration.Connections.Keys.FirstOrDefault(host => host.Equals(displayName, StringComparison.OrdinalIgnoreCase));

    private static string? SelectMenu(
        string title,
        List<string> items,
        int columns,
        GridMenu? underlay = null,
        string? selectedItem = null,
        int startRow = 0,
        int borderInset = 0,
        bool isDimmed = false,
        int padding = 1,
        string? footerText = null)
    {
        var options = CreateMenu(title, items, columns, underlay, selectedItem, startRow, borderInset, isDimmed, padding, footerText);
        var selection = options.Run();
        return options.WasCancelled ? null : selection;
    }

    private static GridMenu CreateMenu(
        string title,
        List<string> items,
        int columns,
        GridMenu? underlay = null,
        string? selectedItem = null,
        int startRow = 0,
        int borderInset = 0,
        bool isDimmed = false,
        int padding = 1,
        string? footerText = null)
    {
        var options = new GridMenu(
            items,
            columns: columns,
            padding: padding,
            fullWidth: true,
            border: true,
            title: title,
            startRow: startRow,
            borderInset: borderInset,
            isDimmed: isDimmed)
        {
            UnderlayMenu = underlay,
            FooterText = footerText
        };
        var menuColors = _configuration.Configuration.MenuOptions;
        options.SetColor(
            selectedItemColor: ToAnsiColor(menuColors.SelectionColor, Foreground.Red, isDimmed),
            defaultColor: ToAnsiColor(menuColors.TextColor, Foreground.White, isDimmed),
            borderColor: ToAnsiColor(menuColors.BorderColor, Foreground.Blue, isDimmed),
            titleColor: ToAnsiColor(menuColors.TitleColor, Foreground.Cyan, isDimmed));
        options.SetSelectedItem(selectedItem);
        return options;
    }

    private static string ReadText(
        string prompt,
        GridMenu? underlay = null,
        int startRow = 0,
        int borderInset = 0,
        int padding = 1)
    {
        var input = new TextInput(
            prompt,
            padding: padding,
            fullWidth: true,
            border: true,
            startRow: startRow,
            borderInset: borderInset,
            promptInline: true)
        {
            UnderlayMenu = underlay
        };
        input.SetColor(
            promptColor: ToAnsiColor(_configuration.Configuration.MenuOptions.TitleColor, Foreground.Cyan),
            inputColor: ToAnsiColor(_configuration.Configuration.MenuOptions.TextColor, Foreground.White),
            borderColor: ToAnsiColor(_configuration.Configuration.MenuOptions.BorderColor, Foreground.Blue));
        return input.Run().ToString() ?? "";
    }

    private static bool Confirm(string prompt) =>
        ReadText($"{prompt} Enter Y to continue:").Equals("Y", StringComparison.OrdinalIgnoreCase);

    private static string ToAnsiColor(string value, string fallback, bool dimmed = false)
    {
        if (value.Length != 7 || value[0] != '#')
        {
            return dimmed ? Foreground.BrightBlack : fallback;
        }

        try
        {
            var intensity = dimmed ? 0.65 : 1.0;
            var red = Convert.ToInt32(value.Substring(1, 2), 16);
            var green = Convert.ToInt32(value.Substring(3, 2), 16);
            var blue = Convert.ToInt32(value.Substring(5, 2), 16);
            return Foreground.RGB(
                (int)Math.Round(red * intensity),
                (int)Math.Round(green * intensity),
                (int)Math.Round(blue * intensity));
        }
        catch (FormatException)
        {
            return dimmed ? Foreground.BrightBlack : fallback;
        }
    }

    private static void ShowMessage(string message, bool clearScreen = true)
    {
        if (clearScreen)
        {
            Console.Clear();
        }
        else
        {
            Console.WriteLine();
        }
        Console.WriteLine(message);
        Console.WriteLine("Press any key to continue.");
        Console.ReadKey(true);
    }
}