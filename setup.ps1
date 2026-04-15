$PSRepositoryName = 'LaTrobeRepo'
$PSRepositorySourceLocation = 'https://modules.powershell.latrobe.biz/nuget'
$PSRepositoryPublishLocation = 'https://modules.powershell.latrobe.biz/nuget'

$TerminalSettings = "$env:LOCALAPPDATA\Packages\Microsoft.WindowsTerminal_8wekyb3d8bbwe\LocalState\settings.json"
$AppFolder = "$env:APPDATA\JumpShell"

Write-Host "Registering Internal PS Repository..." -ForegroundColor Green
Try
    {
        Get-PSRepository -Name $PSRepositoryName -ErrorAction Stop | Out-Null
    }
Catch
    {
        Register-PSRepository -Name $PSRepositoryName -SourceLocation $PSRepositorySourceLocation -PublishLocation $PSRepositoryPublishLocation -InstallationPolicy Trusted
    }

Write-Host "Installing required modules..." -ForegroundColor Green
Install-Module -Name powershell-yaml -Scope CurrentUser
Install-Module -Name ConsoleFX -Scope CurrentUser -Repository $PSRepositoryName
Install-Module -Name LTF-PasswordState -MinimumVersion 1.0.7.2 -Scope CurrentUser -Repository $PSRepositoryName

Write-Host "Copying required files to location..." -ForegroundColor Green

If((Test-Path -Path $AppFolder) -eq $False)
    {
        New-Item -Path $AppFolder -ItemType Directory -Force | Out-Null
    }
Copy-Item -Path $PSScriptRoot\src\jumpshell.ps1 -Destination $AppFolder\jumpshell.ps1 -Force
Copy-Item -Path $PSScriptRoot\src\jumpshell.png -Destination $AppFolder\jumpshell.png -Force

$WindowsTerminalConfig = Get-Content -Path $TerminalSettings -Raw | ConvertFrom-Json

$Profiles = $WindowsTerminalConfig.profiles.list
If($Profiles.name -notcontains 'JumpShell')
    {
        Write-Host "Setting up new profile for Windows Terminal" -ForegroundColor Green
        $ProfileConfig = Get-Content -Path $PSScriptRoot/src/profile.json -Raw | ConvertFrom-Json
        $WindowsTerminalConfig.profiles.list += $ProfileConfig

        $NewSettings = ConvertTo-Json -InputObject $WindowsTerminalConfig -Depth 100
        [IO.File]::WriteAllLines($TerminalSettings, $NewSettings)
    }