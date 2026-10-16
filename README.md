# JumpShell

JumpShell is a cross-platform .NET 10 console app for managing and opening saved remote connections. Its interactive terminal menu supports Linux and Windows hosts, with VS Code Remote SSH available for Linux connections.

Source code and downloads: [GitHub repository](https://github.com/ollie-galbraith/jumpshell) · [GitHub Releases](https://github.com/ollie-galbraith/jumpshell/releases).

## What it does

- Connects to Linux hosts over SSH, or opens them in VS Code Remote SSH.
- Connects to Windows hosts by starting PowerShell over SSH, or by using PowerShell 7 SSH remoting.
- Saves host aliases, usernames, default users, and optional SSH key paths in a YAML configuration file.
- Remembers host-specific terminal background colors for SSH sessions.

## Run

Download the executable for your operating system and CPU architecture from the [GitHub Releases page](https://github.com/ollie-galbraith/jumpshell/releases). For example, after downloading the Linux x64 release:

```sh
chmod +x jumpshell-linux-x64
./jumpshell-linux-x64
```

Pass `--version` to the executable to print its version without opening the menu. OpenSSH (`ssh`) must be available on your `PATH`. VS Code connections also need the `code` CLI and Remote SSH extension. The Windows PowerShell-remoting option requires PowerShell 7 locally and SSH remoting configured on the Windows host. The default Windows method starts `pwsh` on the host over SSH, so the host needs OpenSSH Server and PowerShell installed.

## Build

Install the .NET 10 SDK, then publish a self-contained single-file executable. For example, to build for Linux x64:

```sh
dotnet publish src/JumpShell/JumpShell.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true
```

The build workflow publishes `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64`, and `win-x64` releases. Local publish output is copied into the repository's `bin/` directory for release assembly; downloadable executables are provided on the [GitHub Releases page](https://github.com/ollie-galbraith/jumpshell/releases).

## Configuration

On first run, JumpShell creates `JumpShell/config.yaml` in the platform's user configuration directory:

- Windows: `%APPDATA%/JumpShell/config.yaml`
- macOS: `~/Library/Application Support/JumpShell/config.yaml`
- Linux: `$XDG_CONFIG_HOME/JumpShell/config.yaml`, or `~/.config/JumpShell/config.yaml` if `XDG_CONFIG_HOME` is unset

Use the menu to add hosts and manage users, or edit the YAML directly. Host entries may be written as the mapping shown here; the loader also accepts the older sequence-of-single-host-maps format.

```yaml
configuration:
	use_password_state: false
	windows_connection_method: ssh-shell
	usernames:
		- admin
	menu_options:
		columns: 6
		title_color: '#bb9af7'
		selection_color: '#f7768e'
		border_color: '#7aa2f7'
		text_color: '#7a8f75'
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

### Global options

| Option | Purpose | Values and default |
| --- | --- | --- |
| `configuration.use_password_state` | Controls whether direct Linux SSH connections stop with a PasswordState-not-supported message. | `true` or `false`; default `false`. Set to `false` to use OpenSSH identities and the agent. |
| `configuration.windows_connection_method` | Default connection method for Windows hosts. A host-level setting overrides it. | `ssh-shell` (default) or `powershell-remoting`. |
| `configuration.usernames` | Usernames available to all hosts. | List of strings. |
| `configuration.menu_options.columns` | Number of columns in the host menu. | Integer; default `6`. |
| `configuration.menu_options.title_color` | Menu title color. | Hex color; default `#bb9af7`. |
| `configuration.menu_options.selection_color` | Selected-item color. | Hex color; default `#f7768e`. |
| `configuration.menu_options.border_color` | Menu border color. | Hex color; default `#7aa2f7`. |
| `configuration.menu_options.text_color` | Menu and prompt text color. | Hex color; default `#7a8f75`. |

### Host options

Each key under `connections` is the saved host name shown in the menu.

| Option | Purpose | Values and default |
| --- | --- | --- |
| `operating_system` | Selects host behavior. | `Linux` (default) or `Windows`. |
| `alias` | Uses another hostname or IP as the connection target while retaining the saved host name in the menu. | String; optional. |
| `default_user` | Uses this username without showing the user picker. | String; optional. |
| `ssh_key` | Private key path passed to direct SSH and Windows `ssh-shell` connections. | Path; optional. OpenSSH's other configured identities remain available. |
| `additional_users` | Usernames available only for this host. | List of strings. |
| `connection_method` | Default method for Linux hosts. | `SSH` (default) or `VSCode`. |
| `windows_connection_method` | Overrides the global Windows method for this host. | `ssh-shell` or `powershell-remoting`; optional. |
| `terminal_color.background` | Sets the interactive SSH terminal background color. | Hex color such as `#1e1e1e`; optional. |

`ssh-shell` connects to Windows with `ssh -t user@host pwsh`; the host needs OpenSSH Server and PowerShell. `powershell-remoting` uses local PowerShell 7 and `Enter-PSSession -HostName`, with SSH remoting configured on the host. Per-host `ssh_key` is not passed to VS Code Remote SSH or PowerShell remoting.

Global usernames are shared by every host; `additional_users` are host-specific. A `default_user`, or the only assigned user, is selected automatically. If multiple users are assigned, JumpShell shows a picker. Set `connection_method: VSCode` to make VS Code the default for a Linux host.

The older `configuration.credentials` entries are read as global usernames, and their `password_id` values are preserved when saved. PasswordState integration, key generation, and credential retrieval are not implemented. The legacy `password_list`, `global_ssh_keypair_id`, `seperate_ssh_key_pairs`/`separate_ssh_key_pairs`, host `password_id`, and `public_key_set` fields may be present in older files but do not control connections in this build.

### Menu shortcuts

Press `a` to add a host, `c` to open the selected host's options, `r` or Delete to remove it, `?` for help, and Enter to connect. The host options menu also manages aliases, SSH keys, users, defaults, and saved SSH host keys. Press Escape to exit.

See the [build and release guide](src/JumpShell/README.md) for platform-specific publish commands and implementation notes.