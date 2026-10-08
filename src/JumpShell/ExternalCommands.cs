using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace JumpShell;

internal static class ExternalCommands
{
    private const string BackgroundColorScript = """
        #!/usr/bin/env bash
        if [[ $- == *i* ]] && [[ -n "$SSH_CONNECTION" ]]; then
            if [[ "${WT_BG_COLOR:-}" =~ ^#[0-9A-Fa-f]{6}$ ]]; then
                printf '\e]11;%s\a' "$WT_BG_COLOR"
                trap 'printf "\e]111\a"' EXIT
            fi
        fi
        """;

    public static async Task<int> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string? standardInput = null,
        bool suppressOutput = false)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            RedirectStandardInput = standardInput is not null,
            RedirectStandardOutput = suppressOutput,
            RedirectStandardError = suppressOutput
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
            if (standardInput is not null)
            {
                await process.StandardInput.WriteAsync(standardInput);
                process.StandardInput.Close();
            }

            var standardOutputTask = suppressOutput
                ? process.StandardOutput.ReadToEndAsync()
                : Task.FromResult(string.Empty);
            var standardErrorTask = suppressOutput
                ? process.StandardError.ReadToEndAsync()
                : Task.FromResult(string.Empty);
            await Task.WhenAll(process.WaitForExitAsync(), standardOutputTask, standardErrorTask);
            return process.ExitCode;
        }
        catch (Win32Exception exception)
        {
            if (!suppressOutput)
            {
                Console.Error.WriteLine($"Unable to run '{fileName}': {exception.Message}");
            }
            return 127;
        }
    }

    public static async Task<int> ConnectSshAsync(string hostname, string username, HostConfiguration host)
    {
        var target = $"{username}@{hostname}";
        var identityArguments = await GetIdentityArgumentsAsync(target, host);
        var background = host.TerminalColor?.Background;
        if (!string.IsNullOrWhiteSpace(background) && IsHexColor(background))
        {
            var setupArguments = new List<string>(identityArguments);
            setupArguments.AddRange([target, "mkdir -p ~/.bashrc.d && cat > ~/.bashrc.d/background_color && sed -i 's/\\r$//' ~/.bashrc.d/background_color"]);
            var setup = await RunAsync("ssh", setupArguments, BackgroundColorScript);
            if (setup != 0)
            {
                return setup;
            }

            var connectionArguments = new List<string>(identityArguments);
            connectionArguments.AddRange(["-tt", target, $"export WT_BG_COLOR='{background}'; exec \"$SHELL\" -l"]);
            return await RunAsync("ssh", connectionArguments);
        }

        var arguments = new List<string>(identityArguments);
        arguments.Add(target);
        return await RunAsync("ssh", arguments);
    }

    public static Task<int> ConnectPowerShellRemotingAsync(string hostname, string username) =>
        RunAsync("pwsh", ["-NoLogo", "-NoExit", "-Command", $"Enter-PSSession -HostName '{EscapePowerShell(hostname)}' -UserName '{EscapePowerShell(username)}'"]);

    public static async Task<int> ConnectRemotePowerShellShellAsync(string hostname, string username, HostConfiguration host)
    {
        var target = $"{username}@{hostname}";
        var arguments = await GetIdentityArgumentsAsync(target, host);
        arguments.AddRange(["-tt", $"{username}@{hostname}", "pwsh"]);
        return await RunAsync("ssh", arguments);
    }

    public static Task<int> ClearKnownHostKeyAsync(string hostname) =>
        RunAsync("ssh-keygen", ["-R", hostname], suppressOutput: true);

    public static Task<int> OpenVsCodeAsync(string hostname, string username, string? workspacePath = null)
    {
        var arguments = new List<string> { "--remote", $"ssh-remote+{username}@{hostname}" };
        if (!string.IsNullOrWhiteSpace(workspacePath))
        {
            arguments.Add(workspacePath);
        }

        return RunAsync("code", arguments);
    }

    public static IReadOnlyList<RemoteWorkspace> FindVsCodeWorkspaces(string hostname)
    {
        var storagePath = Path.Combine(PlatformPaths.UserConfigurationDirectory, "Code", "User", "globalStorage", "storage.json");
        if (!File.Exists(storagePath))
        {
            return [];
        }

        using var document = JsonDocument.Parse(File.ReadAllText(storagePath));
        if (!document.RootElement.TryGetProperty("profileAssociations", out var associations) ||
            !associations.TryGetProperty("workspaces", out var workspaces))
        {
            return [];
        }

        var results = new List<RemoteWorkspace>();
        foreach (var property in workspaces.EnumerateObject())
        {
            if (!property.Name.Contains(hostname, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var uri = property.Name;
            var hostIndex = uri.IndexOf(hostname, StringComparison.OrdinalIgnoreCase);
            var path = Uri.UnescapeDataString(uri[(hostIndex + hostname.Length)..]);
            var name = path.TrimEnd('/', '\\').Split('/', '\\').LastOrDefault();
            if (!string.IsNullOrWhiteSpace(name))
            {
                results.Add(new RemoteWorkspace(name, path));
            }
        }

        return results;
    }

    private static bool IsHexColor(string value) =>
        value.Length == 7 && value[0] == '#' && value[1..].All(Uri.IsHexDigit);

    private static async Task<List<string>> GetIdentityArgumentsAsync(string target, HostConfiguration host)
    {
        var arguments = new List<string>();
        if (string.IsNullOrWhiteSpace(host.SshKey))
        {
            return arguments;
        }

        var selectedIdentity = ExpandIdentityPath(host.SshKey);
        arguments.Add("-i");
        arguments.Add(selectedIdentity);

        var defaultIdentities = await GetConfiguredIdentityFilesAsync(target);
        var pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        foreach (var identity in defaultIdentities.Select(ExpandIdentityPath).Where(identity => !pathComparer.Equals(identity, selectedIdentity)))
        {
            arguments.Add("-i");
            arguments.Add(identity);
        }

        return arguments;
    }

    private static async Task<IReadOnlyList<string>> GetConfiguredIdentityFilesAsync(string target)
    {
        var startInfo = new ProcessStartInfo("ssh")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true
        };
        startInfo.ArgumentList.Add("-G");
        startInfo.ArgumentList.Add(target);

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
            {
                return [];
            }

            return output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                .Where(line => line.StartsWith("identityfile ", StringComparison.OrdinalIgnoreCase))
                .Select(line => line["identityfile ".Length..].Trim())
                .Where(path => !path.Equals("none", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        catch (Win32Exception)
        {
            return [];
        }
    }

    private static string ExpandIdentityPath(string path)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (path == "~")
        {
            return home;
        }

        if (path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal))
        {
            return Path.Combine(home, path[2..]);
        }

        return Environment.ExpandEnvironmentVariables(path);
    }

    private static string EscapePowerShell(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}

internal sealed record RemoteWorkspace(string Name, string Path);