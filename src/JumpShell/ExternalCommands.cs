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
        var background = host.TerminalColor?.Background;
        if (!string.IsNullOrWhiteSpace(background) && IsHexColor(background))
        {
            var setup = await RunAsync("ssh", [target, "mkdir -p ~/.bashrc.d && cat > ~/.bashrc.d/background_color && sed -i 's/\\r$//' ~/.bashrc.d/background_color"], BackgroundColorScript);
            if (setup != 0)
            {
                return setup;
            }

            return await RunAsync("ssh", ["-tt", target, $"export WT_BG_COLOR='{background}'; exec \"$SHELL\" -l"]);
        }

        return await RunAsync("ssh", [target]);
    }

    public static Task<int> ConnectPowerShellRemotingAsync(string hostname, string username) =>
        RunAsync("pwsh", ["-NoLogo", "-NoExit", "-Command", $"Enter-PSSession -HostName '{EscapePowerShell(hostname)}' -UserName '{EscapePowerShell(username)}'"]);

    public static Task<int> ConnectRemotePowerShellShellAsync(string hostname, string username) =>
        RunAsync("ssh", ["-tt", $"{username}@{hostname}", "pwsh"]);

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

    private static string EscapePowerShell(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}

internal sealed record RemoteWorkspace(string Name, string Path);