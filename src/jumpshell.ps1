Function Get-VSCodeRemoteWorkspace
    {
        param(
            [Parameter(Mandatory=$true)]
            [String]$RemoteHost
        )

        [System.Collections.Arraylist]$AvailableWorkspaces = @()
        [System.Collections.Arraylist]$Output = @()

        $StorageContent = Get-Content -path $env:APPDATA\Code\User\globalStorage\storage.json -Raw | ConvertFrom-Json

        $Workspaces = $StorageContent.profileAssociations.workspaces
        ForEach($Workspace in ($Workspaces | Get-Member -MemberType NoteProperty | Select-Object -ExpandProperty Name))
            {                
                If($Workspace -like "*$RemoteHost*")
                    {
                        [void]$AvailableWorkspaces.Add($Workspace)
                    }
            }

        ForEach($AvailableWorkspace in $AvailableWorkspaces)
            {
                $Path = [System.Uri]::UnescapeDataString(($AvailableWorkspace -split $RemoteHost)[-1])

                $Obj = [pscustomobject]@{
                    Uri = $AvailableWorkspace
                    Path = $Path
                    Name = ($Path | Split-Path -Leaf)
                }
                [void]$Output.Add($Obj)
            }
        Return $Output
    }

Function Import-RemoteConnectionConfig
    {       
        $Configuration = ConvertFrom-Yaml (Get-Content -Path $Script:Path -Raw) -Ordered

        If($null -eq $Configuration.connections.Keys)
            {
                $AvailableConnections = [System.Collections.Generic.List[object]]::New()
            }
        Else
            {
                $AvailableConnections = [System.Collections.Generic.List[object]]::New(@($Configuration.connections.Keys))
                $AvailableConnections.Sort()
            }

        [void]$AvailableConnections.Add("Enter hostname")
        [void]$AvailableConnections.Add("Remove hostname")
        [void]$AvailableConnections.Add("Open config file")
        
        $AvailableUsers = [System.Collections.Generic.List[object]]::New(@($Configuration.configuration.credentials.username))
        $AvailableUsers.Sort()

        [void]$AvailableUsers.Add("Enter username")
        [void]$AvailableUsers.Add("Remove username")

        $Script:Config = $Configuration
        $Script:PasswordList = $Configuration.configuration.password_list
        $Script:AvailableUsers = $AvailableUsers
        $Script:AvailableConnections = $AvailableConnections
    }

Function Initialize-ConfigurationFile
    {
        [CmdletBinding()]
        param(
            [Parameter(Mandatory=$False)]
            [String]$Path = "$env:APPDATA\JumpShell\RemoteConnections-Config.yml",
            [Parameter(Mandatory=$False)]
            [Switch]$ResetDefaults
        )

        $Script:Path = $Path

        $Script:YamlOutputParams = @{
            Options = 'WithIndentedSequences'
            OutFile = $Script:Path
            Force = $true
        }

        If(((Test-Path -Path $Path) -eq $True) -and ($ResetDefaults -eq $false))
            {
                Return $null
            }

        $PasswordStateList = Initialize-PrivatePwdStateList
        New-Item -Path $Path -ItemType File -Force | Out-Null

        $DefaultContent = [Ordered]@{
            configuration = @{
                use_password_state     = $true
                password_list          = $PasswordStateList.PasswordListID
                global_ssh_keypair_id  = $PasswordStateList.GlobalSSHKeyID
                seperate_ssh_key_pairs = $false
                usernames              = @()
                menu_options = @{
                    columns         = 6
                    title_color     = "'#15dbad'"
                    selection_color = "'#ff0000'"
                    border_color    = "'#054873'"
                    text_color      = "'#808080'"
                }
            }
            connections = @()
        }

        Write-Host "Creating a new configuration file for Remote Connections..."

        ConvertTo-Yaml $DefaultContent @Script:YamlOutputParams
    }

Function Initialize-PrivatePwdStateList
    {
        [CmdletBinding()]
        param(
            [Parameter(Mandatory=$false)]
            [String]$Username = $Env:USERNAME
        )

        $PasswordListName = "$Username | Remote Connection"

        Try
            {
                $PasswordList = Get-PwdStatePasswordList -PasswordList $PasswordListName -UseDefaultCredentials -ErrorAction Stop
            }
        Catch
            {
                Write-Host "Creating a new private password list in PasswordState to store credentials..."
                $PasswordList = New-PwdStatePasswordList -Name $PasswordListName -Description "SSH Keys for $Username" -Private -CopySettingsFromTemplateID 12 -UseDefaultCredentials
            }

        Try
            {
                $GlobalSSHKey = Get-PwdStatePassword -PasswordListID $PasswordList.PasswordListID -Title "GLOBAL" -UseDefaultCredentials -ErrorAction Stop | Select-Object -ExpandProperty PasswordID
            }
        Catch
            {
                Write-Host "Creating a new SSH key pair..."
                $GlobalSSHKey = Initialize-SSHKeyPair -PasswordListID $PasswordList.PasswordListID -RemoteHostName "GLOBAL" #-Passphrase $Passphrase
            }

        Return [pscustomobject]@{
            PasswordListID = $PasswordList.PasswordListID
            GlobalSSHKeyID = $GlobalSSHKey
        }
    }

Function Initialize-SSHKeyPair
    {
        [CmdletBinding()]
        param(
            [Parameter(Mandatory=$True)]
            [Int]$PasswordListID,
            [Parameter(Mandatory=$True)]
            [String]$RemoteHostName,
            [Parameter(Mandatory=$False)]
            [ValidateSet('DSA', 'ECDSA', 'ECDSA-SK', 'ED25519', 'ED25519-SK', 'RSA')]
            [String]$KeyType = "ED25519",
            [Parameter(Mandatory=$False)]
            [SecureString]$Passphrase,
            [Parameter(Mandatory=$False)]
            [String]$Comment
        )

        $CommandArgs = @("-t", $($KeyType.ToLower()), "-f", "$($Env:Temp)\temp_key")
        If([String]::IsNullOrEmpty($Comment) -eq $false)
            {
                $CommandArgs += @('-C', $Comment)
            }
            
        If([String]::IsNullOrEmpty($Passphrase))
            {
                $CommandArgs += @("-N", '""')
            }
        Else
            {
                $CommandArgs += @("-N", $Passphrase)
            }

        ssh-keygen $CommandArgs | Out-Null

        $PrivateKey = Get-Content -Path "$($Env:Temp)\temp_key" -Raw
        $PublicKey = Get-Content -Path "$($Env:Temp)\temp_key.pub" -Raw

        Remove-Item "$($Env:Temp)\temp_key" -Force
        Remove-Item "$($Env:Temp)\temp_key.pub" -Force

        $Password = $Passphrase
        If($null -eq $Passphrase)
            {
                $Password = ConvertTo-SecureString -String 'Not_important_1928348123$@' -AsPlainText -Force
            }

        $PasswordStateParams = @{
            PasswordListID = $PasswordListID
            Title = $RemoteHostName
            Username = $env:USERNAME
            Password = $Password
            PublicKey = $PublicKey
            PrivateKey = $PrivateKey | ConvertTo-SecureString -AsPlainText -Force
            Type = 'SSH'
            UseDefaultCredentials = $true
        }

        If($null -ne $Passphrase)
            {
                $PasswordStateParams.Add('Passphrase', $Passphrase)
            }

        $PasswordItem = Add-PwdStatePassword @PasswordStateParams

        Return $PasswordItem.PasswordID
    }

Function New-RemoteConnection
    {
        [CmdletBinding()]
        param(
            [Parameter(Mandatory=$false)]
            [String]$Hostname,
            [Parameter(Mandatory=$false)]
            [String]$OperatingSystem,
            [Parameter(Mandatory=$false)]
            [Boolean]$SaveToPasswordState = $True,
            [Parameter(Mandatory=$false)]
            [Boolean]$SeperateSSHKeyPair = $False
        )

        Import-RemoteConnectionConfig

        If($Script:Config.connections.Keys -contains $Hostname)
            {
                Return $null
            }

        Try
            {
                Test-Connection -ComputerName $Hostname -Count 1 -ErrorAction Stop | Out-Null
            }
        Catch
            {
                Write-Warning "Unable to ping $($Hostname). Continue connecting?"
                $ContinueConnecting = Read-Host "Y/N"

                If($ContinueConnecting -ne 'Y')
                    {
                        [System.Console]::Clear()
                        Continue RemoteConnectionLoop
                    }
            }

        $MachineObject = @{
            "$Hostname" = @{
                'operating_system' = $OperatingSystem
            }
        }

        If(($Script:Config.configuration.use_password_state -eq $True) -and ($SaveToPasswordState -eq $True))
            {
                If($OperatingSystem -eq 'Linux')
                    {
                        If($SeperateSSHKeyPair -eq $true)
                            {
                                $Passphrase = New-Password -Words 3
                                $SSHKeyPasswordID = Initialize-SSHKeyPair -PasswordListID $Script:Config.configuration.password_list -RemoteHostName $Hostname -Passphrase $Passphrase

                                $MachineObject.$Hostname['password_id'] = $SSHKeyPasswordID
                            }

                        $MachineObject.$Hostname['public_key_set'] = @()
                    }           
            }

        [void]$Script:Config.connections.Add($MachineObject)
        ConvertTo-Yaml $Script:Config @Script:YamlOutputParams
    }

Function New-RemoteConnectionUser
    {
        [CmdletBinding()]
        param(
            [Parameter(Mandatory=$True)]
            [String]$Username,
            [Parameter(Mandatory=$false)]
            [Boolean]$SaveToPasswordState = $True
        )

        $Username = Read-TextInput -Prompt "Enter username:" @TextInputParams

        Import-RemoteConnectionConfig

        If($Script:Config.configuration.credentials.Keys -contains $Username)
            {
                Return $null
            }

        $UserObject = @{
            "username" = $Username
        }

        If(($Script:Config.configuration.use_password_state -eq $True) -and ($SaveToPasswordState -eq $True))
            {
                $SecureString = Read-TextInput -Prompt "Enter $Username`s password:" -AsSecureString @TextInputParams
                $PasswordID = Add-PwdStatePassword -PasswordListID $Script:Config.configuration.password_list -Title $Username -Username $Username -Password $SecureString -Type Basic -UseDefaultCredentials | Select-Object -ExpandProperty PasswordID

                $UserObject['password_id'] = $PasswordID
            }

        [void]$Script:Config.configuration.credentials.Add($UserObject)

        ConvertTo-Yaml $Script:Config @Script:YamlOutputParams

        Return $Username
    }

Function Remove-RemoteConnection
    {
        [CmdletBinding()]
        param(
            [Parameter(Mandatory=$True)]
            [String]$Hostname
        )

        Import-RemoteConnectionConfig

        If($Script:Config.connections.Keys -notcontains $Hostname)
            {
                Return $null
            }

        $Script:Config.connections = @($Script:Config.connections | Where-Object {$_.Keys -notcontains $Hostname})

        ConvertTo-Yaml $Script:Config @Script:YamlOutputParams
    }

Function Remove-RemoteConnectionUser
    {
        [CmdletBinding()]
        param(
            [Parameter(Mandatory=$True)]
            [String]$Username
        )

        Import-RemoteConnectionConfig

        If($Script:Config.configuration.credentials.username -notcontains $Username)
            {
                Return $null
            }

        $Script:Config.configuration.credentials = @($Script:Config.configuration.credentials | Where-Object {$_.username -ne $Username})
        ConvertTo-Yaml $Script:Config @Script:YamlOutputParams
    }

Function Show-RemoteConnectionMenu
    {
        [CmdletBinding()]
        param(
        )

        $ExtraOptions = @(
            "Enter hostname",
            "Remove hostname",
            "Open config file"
        )
        
        $MachineMenuParams = @{
            Items    = ($Script:AvailableConnections | ForEach-Object{ If($ExtraOptions -contains $_){ $_ } Else { $_.ToUpper() }})
            Title    = 'Machine List'
            Columns  = $Script:Config.configuration.menu_options.columns
            PassThru = $True
        }

        $MachineMenuResult = Select-GridMenu @MachineMenuParams @Script:MenuParameters
        $SelectedComputer = $MachineMenuResult.Result

        Switch($SelectedComputer)
            {
                {[String]::IsNullOrEmpty($_)} { Exit }
                "Enter hostname"
                    {
                        $SelectedComputer = Read-TextInput -Prompt "Enter hostname" @TextInputParams

                        If($Script:AvailableConnections -contains $SelectedComputer)
                            {
                                $OperatingSystem = $Script:Config.connections.$SelectedComputer.operating_system
                            }
                        Else
                            {
                                $OperatingSystem = Select-GridMenu -Items $AllowedOperatingSystems -Title "Enter the OS for $SelectedComputer" -Columns $AllowedOperatingSystems.Count @Script:MenuParamsL2 -UnderlayGridMenu $MachineMenuResult.Menu
                                New-RemoteConnection -Hostname $SelectedComputer -OperatingSystem $OperatingSystem
                            }
                    }
                "Remove hostname"
                    {
                        $SelectedComputer = Select-GridMenu -Items $Script:Config.connections.Keys -Title "Host to remove" -Columns $Script:Config.configuration.menu_options.columns @Script:MenuParamsL2 -UnderlayGridMenu $MachineMenuResult.Menu
                        If([String]::IsNullOrEmpty($SelectedComputer))
                            {
                                Return $null
                            }

                        Remove-RemoteConnection -Hostname $SelectedComputer
                        Continue RemoteConnectionLoop
                    }
                "Open config file"
                    {
                        Try
                            {
                                $Editor = Get-Command code -ErrorAction Stop | Select-Object -ExpandProperty Source
                            }
                        Catch
                            {
                                $Editor = Get-Command notepad | Select-Object -ExpandProperty Source
                            }
                        
                        Invoke-Expression ". '$Editor' $($Script:Path )"
                        Continue RemoteConnectionLoop
                    }
                Default
                    {
                        $OperatingSystem = $Script:Config.connections.$SelectedComputer.operating_system
                    }
            }

        If([String]::IsNullOrEmpty($SelectedComputer))
            {
                Return $null
            }

        $UserMenuParams = @{
            Items            = $Script:AvailableUsers
            Title            = "Select User to connect to $SelectedComputer"
            Columns          = $Script:AvailableUsers.Count
            UnderlayGridMenu = $MachineMenuResult.Menu
            PassThru         = $True
        }

        $Script:UserMenuResult = Select-GridMenu @UserMenuParams @Script:MenuParamsL2
        $SelectedUser = $Script:UserMenuResult.Result
        Switch($SelectedUser)
            {
                {[String]::IsNullOrEmpty($_)}
                    {
                        Return $null
                    }
                "Enter username"
                    {
                        $SelectedUser = New-RemoteConnectionUser
                    }
                "Remove username"
                    {
                        $SelectedUser = Select-GridMenu -Items $Script:Config.configuration.credentials.username -Title "Username to remove" @Script:MenuParamsL3 -UnderlayGridMenu $Script:UserMenuResult.Menu
                        Remove-RemoteConnectionUser -Username $SelectedUser

                        Continue RemoteConnectionLoop
                    }
            }

        $ConnectionOptions = @{
            Username        = $SelectedUser
            Hostname        = $SelectedComputer
            OperatingSystem = $OperatingSystem
        }

        Return $ConnectionOptions
    }

Function Add-BashRCFile
    {
        [CmdletBinding()]
        param(
            [Parameter(Mandatory=$true)]
            [string]$FileName,
            [Parameter(Mandatory=$true)]
            [string]$Content,
            [Parameter(Mandatory=$true)]
            [string[]]$SSHOptions,
            [Parameter(Mandatory=$true)]
            [string]$SSHConnectionString
        )

        $Content | ssh $SSHOptions $SSHConnectionString "mkdir -p ~/.bashrc.d && cat > ~/.bashrc.d/$FileName && sed -i 's/\r$//' ~/.bashrc.d/$FileName"
    }

Function Start-JumpShell
    {
        [CmdletBinding()]
        param()

        & {. $PSScriptRoot\jumpshell.ps1}
    }

$AllowedOperatingSystems = @(
    'Linux',
    'Windows'
)

$LinuxConnectionTypes = @(
    'SSH',
    'VSCode'
)

$SSHBackgroundColorRC = @'
#!/usr/bin/env bash

# Sets the background colour of the terminal
if [[ $- == *i* ]] && [[ -n "$SSH_CONNECTION" ]]; then
# $- == *i* indicates that an interactive shell
        if [[ "${WT_BG_COLOR:-}" =~ ^#[0-9A-Fa-f]{6}$ ]]; then
        # Ensures WT_BG_COLOR is a valid hexcode
                printf '\e]11;%s\a' "$WT_BG_COLOR"
                trap 'printf "\e]111\a"' EXIT
                # Will reset the terminal colours on exit
        fi
fi
'@

If(!(Get-Command -Module LTF-PasswordState))
    {
        Import-Module LTF-PasswordState -MinimumVersion 1.0.7.2
    }

If(!(Get-Command -Module ConsoleFX))
    {
        Import-Module ConsoleFX
    }

Initialize-ConfigurationFile
Import-RemoteConnectionConfig

$SelectionColor = $Script:Config.configuration.menu_options.selection_color
$TitleColor     = $Script:Config.configuration.menu_options.title_color
$BorderColor    = $Script:Config.configuration.menu_options.border_color
$TextColor      = $Script:Config.configuration.menu_options.text_color
If($SelectionColor -match '^#([0-9A-Fa-f]{6})$')
    {
        $SelectionColor = [ConsoleFX.ColorModel.Hex]::New($SelectionColor)
    }
If($TitleColor -match '^#([0-9A-Fa-f]{6})$')
    {
        $TitleColor = [ConsoleFX.ColorModel.Hex]::New($TitleColor)
    }
If($BorderColor -match '^#([0-9A-Fa-f]{6})$')
    {
        $BorderColor = [ConsoleFX.ColorModel.Hex]::New($BorderColor)
    }
If($TextColor -match '^#([0-9A-Fa-f]{6})$')
    {
        $TextColor = [ConsoleFX.ColorModel.Hex]::New($TextColor)
    }

$Script:MenuParameters = @{
    SelectionColor = $SelectionColor
    Border         = $true
    TitleColor     = $TitleColor
    BorderColor    = $BorderColor
    TextColor      = $TextColor
}

$TextInputParams = @{
    Border       = $true
    FullWidth    = $true
    PromptInline = $true
}

$Script:MenuParamsL2 = $Script:MenuParameters + @{ StartRow = 3; BorderInset = 3 }
$Script:MenuParamsL3 = $Script:MenuParameters + @{ StartRow = 6; BorderInset = 6 }


:RemoteConnectionLoop While($True)
    {
        $SelectedComputer = $null
        Clear-Host

        Import-RemoteConnectionConfig

        If($Script:Config.configuration.use_password_state -eq $true)
            {
                $PasswordStateParams = @{
                    UseDefaultCredentials = $true
                }
            }

        $ConnectionOptions = Show-RemoteConnectionMenu

        If($null -eq $ConnectionOptions)
            {
                Clear-Host
                Continue RemoteConnectionLoop
            }

        $ConnectionConfig = $Script:Config.connections.$($ConnectionOptions.Hostname)

        Switch($ConnectionOptions.OperatingSystem)
            {
                'Windows'
                    {
                        $SessionOptions = New-PSSessionOption -SkipCACheck -SkipCNCheck -SkipRevocationCheck

                        $WinRMParams = @{
                            ComputerName = $ConnectionOptions.Hostname
                            UseSSL = $True
                            SessionOption = $SessionOptions
                        }

                        If($ConnectionOptions.Username -ne $env:USERNAME)
                            {
                                If($Script:Config.configuration.use_password_state -eq $true)
                                    {
                                        $PasswordID = ($Script:Config.configuration.credentials | Where-Object {$_.username -eq $($ConnectionOptions.Username)}).password_id

                                        $Script:Config.configuration.credentials.$($ConnectionOptions.Username).password_id
                                        $CredentialObj = Get-PwdStatePassword -PasswordID $PasswordID -AsPSCredential @PasswordStateParams
                                    }
                                Else
                                    {
                                        $CredentialObj = New-Object PSCredential -ArgumentList ($ConnectionOptions.Username, (Read-Host "Enter password for $($ConnectionOptions.Username)" -AsSecureString))
                                    }
                                [void]$WinRMParams.Add('Credential', $CredentialObj)
                            }

                        Clear-Host
                        Enter-PSSession @WinRMParams
                        Break RemoteConnectionLoop
                    }
                'Linux'
                    {
                        $ConnectionType = Select-GridMenu -Items $LinuxConnectionTypes -Title "Connect to $($ConnectionOptions.Hostname) via..." @Script:MenuParameters -Columns $LinuxConnectionTypes.Count -UnderLayGridMenu $Script:UserMenuResult.Menu -StartRow 6 -BorderInset 6

                        If([String]::IsNullOrEmpty($ConnectionType))
                            {
                                Clear-Host
                                Continue RemoteConnectionLoop
                            }

                        $ConnectionString = "$($ConnectionOptions.Username)@$($ConnectionOptions.Hostname)"

                        Switch($ConnectionType)
                            {
                                'SSH'
                                    {
                                        $SSHOptions = @('-o', 'PreferredAuthentications=publickey,gssapi-with-mic,password', '-o', 'GSSAPIAuthentication=yes')
                                        If($Script:Config.configuration.use_password_state -eq $true)
                                            {
                                                $PasswordID = $Script:Config.configuration.global_ssh_keypair_id
                                                If($ConnectionConfig.Key -contains 'password_id')
                                                    {
                                                        $PasswordID = $ConnectionConfig.password_id
                                                    }

                                                $SSHKey = Get-PwdStatePassword -PasswordID $PasswordID -Properties 'GenericField1', 'GenericField2' @PasswordStateParams

                                                $PrivateKeyFile = New-TemporaryFile
                                                Set-Content -Path $PrivateKeyFile.FullName -Value $SSHKey.GenericField1

                                                $SSHOptions += @('-i', $PrivateKeyFile.FullName)

                                                If($Script:Config.connections.$($ConnectionOptions.Hostname).public_key_set -notcontains $ConnectionOptions.Username)
                                                    {
                                                        $SSHKey.GenericField2 | ssh $ConnectionString "mkdir -p ~/.ssh && cat >> ~/.ssh/authorized_keys"

                                                        $Script:Config.connections.$($ConnectionOptions.Hostname).public_key_set += @($ConnectionOptions.Username)
                                                        ConvertTo-Yaml $Script:Config @Script:YamlOutputParams
                                                    }

                                                Start-Job -ScriptBlock {Start-Sleep -Seconds 3; Remove-Item $Using:PrivateKeyFile.FullName -Force} | Out-Null
                                            }
                                        
                                        Clear-Host

                                        $ColouredTerminal = ''
                                        If($Script:Config.connections.$($ConnectionOptions.Hostname).Keys -contains 'terminal_color')
                                            {
                                                If($Script:Config.connections.$($ConnectionOptions.Hostname).terminal_color.Keys -contains 'background')
                                                    {
                                                        Add-BashRCFile -FileName 'background_color' -Content $SSHBackgroundColorRC -SSHOptions $SSHOptions -SSHConnectionString $ConnectionString

                                                        $ColouredTerminal = 'export WT_BG_COLOR="{0}"; exec "$SHELL" -l' -f $Script:Config.connections.$($ConnectionOptions.Hostname).terminal_color.background
                                                        $SSHOptions += @('-tt')
                                                    }
                                            }

                                        ssh $SSHOptions $ConnectionString $ColouredTerminal
                                    }
                                'VSCode'
                                    {
                                        $VSCodeWorkspaces = Get-VSCodeRemoteWorkspace -RemoteHost $ConnectionOptions.Hostname
                                        
                                        If($VSCodeWorkspaces.Count -eq 0)
                                            {
                                                Clear-Host
                                                code --remote ssh-remote+$($ConnectionString.ToLower())

                                                Continue RemoteConnectionLoop
                                            }

                                        [System.Collections.ArrayList]$WorkspaceNames = @()

                                        ForEach($Workspace in $VSCodeWorkspaces.Name)
                                            {
                                                [void]$WorkspaceNames.Add($Workspace)
                                            }

                                        [void]$WorkspaceNames.Add("New Workspace")

                                        $WorkspaceToConnect = Select-GridMenu -Items $WorkspaceNames -Title "Open Workspace" @Script:MenuParameters -FullWidth:$false

                                        If([String]::IsNullOrEmpty($WorkspaceToConnect))
                                            {
                                                Clear-Host
                                                Continue RemoteConnectionLoop
                                            }
                                        If($WorkspaceToConnect -eq 'New Workspace')
                                            {
                                                $ExtraCodeArgs = ''
                                            }
                                        Else
                                            {
                                                $WorkspacePath = $VSCodeWorkspaces | Where-Object Name -eq $WorkspaceToConnect | Select-Object -ExpandProperty Path
                                                $ExtraCodeArgs = "$WorkspacePath"
                                            }

                                        Clear-Host
                                        code --remote ssh-remote+$($ConnectionString.ToLower()) $ExtraCodeArgs
                                    }
                            }
                    }
                Default
                    {
                    }
            }
    }