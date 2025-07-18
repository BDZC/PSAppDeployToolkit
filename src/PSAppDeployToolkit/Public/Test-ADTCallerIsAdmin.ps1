﻿#-----------------------------------------------------------------------------
#
# MARK: Test-ADTCallerIsAdmin
#
#-----------------------------------------------------------------------------

function Test-ADTCallerIsAdmin
{
    <#
    .SYNOPSIS
        Checks if the current user has administrative privileges.

    .DESCRIPTION
        This function checks if the current user is a member of the Administrators group. It returns a boolean value indicating whether the user has administrative privileges.

    .INPUTS
        None

        You cannot pipe objects to this function.

    .OUTPUTS
        System.Boolean

        Returns $true if the current user is an administrator, otherwise $false.

    .EXAMPLE
        Test-ADTCallerIsAdmin

        Checks if the current user has administrative privileges and returns true or false.

    .NOTES
        An active ADT session is NOT required to use this function.

        Tags: psadt<br />
        Website: https://psappdeploytoolkit.com<br />
        Copyright: (C) 2025 PSAppDeployToolkit Team (Sean Lillis, Dan Cunningham, Muhammad Mashwani, Mitch Richters, Dan Gough).<br />
        License: https://opensource.org/license/lgpl-3-0

    .LINK
        https://psappdeploytoolkit.com/docs/reference/functions/Test-ADTCallerIsAdmin
    #>

    return [PSADT.AccountManagement.AccountUtilities]::CallerIsAdmin
}
