﻿#-----------------------------------------------------------------------------
#
# MARK: Show-ADTBalloonTip
#
#-----------------------------------------------------------------------------

function Show-ADTBalloonTip
{
    <#
    .SYNOPSIS
        Displays a balloon tip notification in the system tray.

    .DESCRIPTION
        Displays a balloon tip notification in the system tray. This function can be used to show notifications to the user with customizable text, title, icon, and display duration.

        For Windows 10 and above, balloon tips automatically get translated by the system into toast notifications.

    .PARAMETER BalloonTipText
        Text of the balloon tip.

    .PARAMETER BalloonTipIcon
        Icon to be used. Options: 'Error', 'Info', 'None', 'Warning'.

    .PARAMETER BalloonTipTime
        Time in milliseconds to display the balloon tip. Default: 10000.

    .PARAMETER NoWait
        Creates the balloon tip asynchronously.

    .INPUTS
        None

        You cannot pipe objects to this function.

    .OUTPUTS
        None

        This function does not return any output.

    .EXAMPLE
        Show-ADTBalloonTip -BalloonTipText 'Installation Started' -BalloonTipTitle 'Application Name'

        Displays a balloon tip with the text 'Installation Started' and the title 'Application Name'.

    .EXAMPLE
        Show-ADTBalloonTip -BalloonTipIcon 'Info' -BalloonTipText 'Installation Started' -BalloonTipTitle 'Application Name'

        Displays a balloon tip with the info icon, the text 'Installation Started', and the title 'Application Name'

    .NOTES
        An active ADT session is NOT required to use this function.

        Tags: psadt<br />
        Website: https://psappdeploytoolkit.com<br />
        Copyright: (C) 2025 PSAppDeployToolkit Team (Sean Lillis, Dan Cunningham, Muhammad Mashwani, Mitch Richters, Dan Gough).<br />
        License: https://opensource.org/license/lgpl-3-0

    .LINK
        https://psappdeploytoolkit.com/docs/reference/functions/Show-ADTBalloonTip
    #>

    [System.Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSReviewUnusedParameter', 'BalloonTipIcon', Justification = "This parameter is used via the function's PSBoundParameters dictionary, which is not something PSScriptAnalyzer understands. See https://github.com/PowerShell/PSScriptAnalyzer/issues/1472 for more details.")]
    [CmdletBinding()]
    param
    (
        [Parameter(Mandatory = $true, Position = 0)]
        [ValidateNotNullOrEmpty()]
        [System.String]$BalloonTipText,

        [Parameter(Mandatory = $false)]
        [ValidateNotNullOrEmpty()]
        [System.Windows.Forms.ToolTipIcon]$BalloonTipIcon = [System.Windows.Forms.ToolTipIcon]::Info,

        [Parameter(Mandatory = $false)]
        [ValidateNotNullOrEmpty()]
        [System.Nullable[System.UInt32]]$BalloonTipTime = 10000,

        [Parameter(Mandatory = $false)]
        [System.Management.Automation.SwitchParameter]$NoWait
    )

    dynamicparam
    {
        # Initialize the module first if needed.
        $adtSession = Initialize-ADTModuleIfUnitialized -Cmdlet $PSCmdlet

        # Define parameter dictionary for returning at the end.
        $paramDictionary = [System.Management.Automation.RuntimeDefinedParameterDictionary]::new()

        # Add in parameters we need as mandatory when there's no active ADTSession.
        $paramDictionary.Add('BalloonTipTitle', [System.Management.Automation.RuntimeDefinedParameter]::new(
                'BalloonTipTitle', [System.String], $(
                    [System.Management.Automation.ParameterAttribute]@{ Mandatory = !$adtSession; HelpMessage = 'Title of the balloon tip.' }
                    [System.Management.Automation.ValidateNotNullOrEmptyAttribute]::new()
                )
            ))

        # Return the populated dictionary.
        return $paramDictionary
    }

    begin
    {
        # Initialize function.
        Initialize-ADTFunction -Cmdlet $PSCmdlet -SessionState $ExecutionContext.SessionState
        $adtConfig = Get-ADTConfig

        # Set up defaults if not specified.
        $BalloonTipTitle = if (!$PSBoundParameters.ContainsKey('BalloonTipTitle'))
        {
            $adtSession.InstallTitle
        }
        else
        {
            $PSBoundParameters.BalloonTipTitle
        }
    }

    process
    {
        # Don't allow toast notifications with fluent dialogs unless this function was explicitly requested by the caller.
        if (($adtConfig.UI.DialogStyle -eq 'Fluent') -and (Get-PSCallStack | Select-Object -Skip 1 | Select-Object -First 1 | & { process { $_.Command -match '^(Show|Close)-ADTInstallationProgress$' } }))
        {
            return
        }

        try
        {
            try
            {
                # Skip balloon if in silent mode, disabled in the config, a presentation is detected, or there's no logged on user.
                if (!$adtConfig.UI.BalloonNotifications)
                {
                    Write-ADTLogEntry -Message "Bypassing $($MyInvocation.MyCommand.Name) [Config Show Balloon Notifications: $($adtConfig.UI.BalloonNotifications)]. BalloonTipText: $BalloonTipText"
                    return
                }
                if ($adtSession -and $adtSession.IsSilent())
                {
                    Write-ADTLogEntry -Message "Bypassing $($MyInvocation.MyCommand.Name) [Mode: $($adtSession.DeployMode)]. BalloonTipText: $BalloonTipText"
                    return
                }
                if (Test-ADTUserIsBusy)
                {
                    Write-ADTLogEntry -Message "Bypassing $($MyInvocation.MyCommand.Name) [Presentation/Microphone in Use Detected: $true]. BalloonTipText: $BalloonTipText"
                    return
                }
                if (!($runAsActiveUser = Get-ADTClientServerUser))
                {
                    Write-ADTLogEntry -Message "Bypassing $($MyInvocation.MyCommand.Name) as there is no active user logged onto the system."
                    return
                }

                # Establish options class for displaying the balloon tip.
                [PSADT.UserInterface.DialogOptions.BalloonTipOptions]$options = @{
                    TrayTitle = $adtConfig.Toolkit.CompanyName
                    TrayIcon = $adtConfig.Assets.Logo
                    BalloonTipTitle = $BalloonTipTitle
                    BalloonTipText = $BalloonTipText
                    BalloonTipIcon = $BalloonTipIcon
                    BalloonTipTime = $BalloonTipTime
                }

                # Display the balloon tip via our client/server process.
                if ($NoWait)
                {
                    Write-ADTLogEntry -Message "Displaying balloon tip notification asynchronously with message [$BalloonTipText]."
                    Invoke-ADTClientServerOperation -ShowBalloonTip -User $runAsActiveUser -Options $options -NoWait
                    return
                }
                Write-ADTLogEntry -Message "Displaying balloon tip notification with message [$BalloonTipText]."
                Invoke-ADTClientServerOperation -ShowBalloonTip -User $runAsActiveUser -Options $options
            }
            catch
            {
                Write-Error -ErrorRecord $_
            }
        }
        catch
        {
            Invoke-ADTFunctionErrorHandler -Cmdlet $PSCmdlet -SessionState $ExecutionContext.SessionState -ErrorRecord $_
        }
    }

    end
    {
        Complete-ADTFunction -Cmdlet $PSCmdlet
    }
}
