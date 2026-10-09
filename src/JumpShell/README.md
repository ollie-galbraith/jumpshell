# JumpShell (.NET)

This project builds a cross-platform JumpShell console executable using the existing ConsoleFX library.

## Build and publish

The executable targets .NET 10. Publish a self-contained, compressed single-file binary for each target platform. Compression reduces download size at the cost of some startup time:

```sh
dotnet publish src/JumpShell/JumpShell.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
dotnet publish src/JumpShell/JumpShell.csproj -c Release -r osx-arm64 --self-contained true -p:PublishSingleFile=true
dotnet publish src/JumpShell/JumpShell.csproj -c Release -r osx-x64 --self-contained true -p:PublishSingleFile=true
dotnet publish src/JumpShell/JumpShell.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true
dotnet publish src/JumpShell/JumpShell.csproj -c Release -r linux-arm64 --self-contained true -p:PublishSingleFile=true
```

Each command writes its executable under `src/JumpShell/bin/Release/net10.0/<rid>/publish/` and copies it to the repository-root `bin/` directory using the runtime identifier in its filename, for example `bin/jumpshell-linux-x64`, `bin/jumpshell-osx-arm64`, and `bin/jumpshell-win-x64.exe`. OpenSSH must be available as `ssh`. The VS Code Remote SSH action additionally requires the `code` CLI and the Remote SSH extension.

Set the application version in `JumpShell.csproj`. The release workflow uses it for the `v`-prefixed release tag, and the executable displays it in the main menu footer. Run `jumpshell --version` to print the version without opening the menu.

JumpShell references the vendored `src/JumpShell/lib/ConsoleFX.dll`, so building this repository does not require the ConsoleFX source checkout. When ConsoleFX changes, rebuild its `net10.0` assembly and replace this DLL to carry those changes into JumpShell builds. `Newtonsoft.Json` is restored as a NuGet dependency of JumpShell.

## Configuration

JumpShell creates its configuration file on first run:

- Windows: `%APPDATA%/JumpShell/config.yaml`
- macOS: `~/Library/Application Support/JumpShell/config.yaml`
- Linux: `$XDG_CONFIG_HOME/JumpShell/config.yaml`, or `~/.config/JumpShell/config.yaml` when `XDG_CONFIG_HOME` is unset

Example:

```yaml
configuration:
  use_password_state: false
  windows_connection_method: ssh-shell
  usernames:
    - admin
  menu_options:
    columns: 6
    title_color: '#15dbad'
    selection_color: '#ff0000'
    border_color: '#054873'
    text_color: '#808080'
connections:
  my-linux-host:
    operating_system: Linux
    alias: 192.0.2.10
    default_user: admin
    ssh_key: ~/.ssh/id_ed25519_work
    additional_users:
      - backup
    connection_method: SSH
    terminal_color:
      background: '#1e1e1e'
  my-windows-host:
    operating_system: Windows
    windows_connection_method: powershell-remoting
```

`windows_connection_method` accepts `ssh-shell` or `powershell-remoting`. The host-level value overrides the global value. `ssh-shell` runs `ssh -t user@host pwsh`, requiring OpenSSH Server and PowerShell on the Windows host. `powershell-remoting` runs local `pwsh` with `Enter-PSSession -HostName`, requiring PowerShell 7 locally and SSH remoting configured on the host.

The loader accepts host entries in either map form, as shown above, or the legacy sequence-of-single-host-maps form. Saving writes the map form. It also reads existing `configuration.credentials` usernames and preserves their PasswordState IDs and related legacy settings.

Each connection can set `alias`, `default_user`, `ssh_key`, `additional_users`, and `connection_method` (`SSH` or `VSCode`). The saved connection name remains in the menu; when an alias is set, JumpShell uses it as the target for SSH, VS Code Remote SSH, PowerShell remoting, and saved SSH host-key operations. If a newly added hostname cannot be pinged, JumpShell offers to save an alias such as another hostname or an IP address. Use “Set or clear alias” in the connection menu to edit it later; leave the alias blank to clear it. Use “Set or clear SSH key” to save a private-key path as `ssh_key`; press Tab in the path prompt to show matching filesystem paths and cycle through them, and leave the path blank to clear it. Direct SSH connections pass this key with `-i` and leave OpenSSH's normal identity and agent behavior enabled, so its other configured keys remain available if the selected key is rejected. This per-connection key option applies to direct SSH and Windows `ssh-shell` connections. VS Code Remote SSH and PowerShell remoting use their own SSH configuration paths and do not receive this per-launch key option.

Global `configuration.usernames` remain available to every host; `additional_users` are local to that connection. A configured `default_user` is used directly without showing the user picker. Without a default, a single assigned user is also used automatically; multiple assigned users show the picker. Connections without a configured method default to SSH; use `c` > “Set connection method” or set `connection_method: VSCode` in YAML to make VS Code the default for a host. Press `c` while a host is highlighted to open its single-column, layered connection menu, where you can connect, manage users/defaults, or open the config file.

The connection menu also has “Clear saved SSH host key”. After confirmation, JumpShell runs `ssh-keygen -R <hostname>` to remove that host's entry from the user's OpenSSH `known_hosts` file. The next SSH connection will verify and save the host key again.

From the host list, press `a` to add a host, `r` or Delete to remove the highlighted host after confirmation, `c` for its connection menu, or `?` for centered help. Enter connects to the highlighted host; Escape exits.

## PasswordState

Set `configuration.use_password_state` to `false` to use normal OpenSSH identities from the user's SSH configuration or agent. JumpShell does not generate or manage local keys when PasswordState is disabled, matching the current script's behavior.

The C# PasswordState provider is intentionally not implemented yet. When `use_password_state` is `true`, SSH connections display an explicit message instead of silently using another credential source. Direct PasswordState API integration can be added later without changing the SSH-key behavior for users who keep it disabled.

## Connections

- Linux hosts support interactive SSH and VS Code Remote SSH, including selection of previously opened remote workspaces.
- Windows hosts use the configured SSH mode above.
- Hosts can be added/removed and usernames can be managed through the menus. Password prompts and key selection for ordinary SSH connections are handled by OpenSSH.
- `terminal_color.background` retains the SSH terminal background behavior from the PowerShell version.
- “Open config file” is available from the host connection menu and uses `EDITOR` when set, Notepad on Windows, or the `code` CLI elsewhere.
