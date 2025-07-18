﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.DirectoryServices;
using System.Linq;
using System.Security.Principal;
using PSADT.Security;

namespace PSADT.AccountManagement
{
    /// <summary>
    /// Utility methods for working with Windows accounts and groups.
    /// </summary>
    public static class AccountUtilities
    {
        /// <summary>
        /// Static constructor for readonly constant values.
        /// </summary>
        static AccountUtilities()
        {
            // Cache information about the current user.
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                CallerIsAdmin = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
                CallerUsername = new(identity.Name);
                CallerSid = identity.User!;
            }

            // Build out process/session id information.
            using (var currentProcess = Process.GetCurrentProcess())
            {
                CallerSessionId = (uint)currentProcess.SessionId;
                CallerProcessId = (uint)currentProcess.Id;
            }

            // Initialize the lookup table for well-known SIDs.
            Dictionary<WellKnownSidType, SecurityIdentifier> wellKnownSids = [];
            foreach (var wellKnownSid in typeof(WellKnownSidType).GetEnumValues().Cast<WellKnownSidType>())
            {
                try
                {
                    wellKnownSids.Add(wellKnownSid, new(wellKnownSid, null));
                }
                catch
                {
                    // Just fall through here. Some SIDs can't be created without a domain, etc.
                }
            }
            WellKnownSidLookupTable = new(wellKnownSids);

            // Determine if the caller is the local system account.
            CallerIsLocalSystem = CallerSid.Equals(GetWellKnownSid(WellKnownSidType.LocalSystemSid));
        }

        /// <summary>
        /// Retrieves the <see cref="SecurityIdentifier"/> associated with the specified well-known SID type.
        /// </summary>
        /// <remarks>Well-known SIDs are predefined identifiers for common security principals, such as
        /// "Everyone" or "Local System." Use this method to obtain the <see cref="SecurityIdentifier"/> for a specific
        /// well-known SID type.</remarks>
        /// <param name="wellKnownSidType">The type of the well-known SID to retrieve. This must be a valid <see cref="WellKnownSidType"/> value.</param>
        /// <returns>A <see cref="SecurityIdentifier"/> representing the specified well-known SID type.</returns>
        /// <exception cref="ArgumentException">Thrown if the specified <paramref name="wellKnownSidType"/> is not recognized or is unavailable in the
        /// current context.</exception>
        public static SecurityIdentifier GetWellKnownSid(WellKnownSidType wellKnownSidType)
        {
            // Return the SecurityIdentifier for the specified well-known SID type.
            if (!WellKnownSidLookupTable.TryGetValue(wellKnownSidType, out SecurityIdentifier? sid))
            {
                throw new ArgumentException($"The specified well-known SID type '{wellKnownSidType}' is not recognized or not available in this context.");
            }
            return sid;
        }

        /// <summary>
        /// Tests whether a given SID is a member of a given well known group.
        /// </summary>
        /// <param name="targetSid"></param>
        /// <param name="wellKnownGroupSid"></param>
        /// <returns></returns>
        internal static bool IsSidMemberOfWellKnownGroup(SecurityIdentifier targetSid, WellKnownSidType wellKnownGroupSid)
        {
            using (DirectoryEntry groupEntry = new($"WinNT://./{GetWellKnownSid(wellKnownGroupSid).Translate(typeof(NTAccount)).ToString().Split('\\')[1]},group"))
            {
                HashSet<string> visited = [];
                return CheckMemberRecursive(groupEntry, targetSid, visited);
            }
        }

        /// <summary>
        /// Internal helper method for IsSidMemberOfGroup() to scan all group members recurvsively via System.DirectoryServices.
        /// </summary>
        /// <param name="groupEntry"></param>
        /// <param name="targetSid"></param>
        /// <param name="visited"></param>
        /// <returns></returns>
        private static bool CheckMemberRecursive(DirectoryEntry groupEntry, SecurityIdentifier targetSid, HashSet<string> visited)
        {
            // Recursively test all member SIDs against our target SID, returning false if we have no match.
            if (groupEntry.Invoke("Members") is IEnumerable members)
            {
                foreach (object member in members)
                {
                    using (DirectoryEntry memberEntry = new(member))
                    {
                        // Skip over already parsed groups (group membership loops).
                        if (!visited.Add(memberEntry.Path))
                        {
                            continue;
                        }

                        // Skip over the SID if it's malformed.
                        var sid = memberEntry.Properties["ObjectSID"].Value;
                        if (null == sid)
                        {
                            continue;
                        }

                        // Return true if the current SID is the one we're testing for.
                        if (new SecurityIdentifier((byte[])sid, 0) == targetSid)
                        {
                            return true;
                        }

                        // If this member is a group, scan through its members recursively.
                        if (memberEntry.SchemaClassName == "Group" && CheckMemberRecursive(memberEntry, targetSid, visited))
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Determines whether the current process is elevated.
        /// </summary>
        /// <returns></returns>
        public static readonly bool CallerIsAdmin;

        /// <summary>
        /// Returns the current user's username.
        /// </summary>
        /// <returns></returns>
        public static readonly NTAccount CallerUsername;

        /// <summary>
        /// Represents the security identifier (SID) of the caller.
        /// </summary>
        /// <remarks>This field provides the SID associated with the caller, which can be used for 
        /// security-related operations such as access control or identity verification.</remarks>
        public static readonly SecurityIdentifier CallerSid;

        /// <summary>
        /// Gets the process ID of the caller's current process.
        /// </summary>
        public static readonly uint CallerProcessId;

        /// <summary>
        /// Session Id of the current user running this library.
        /// </summary>
        public static readonly uint CallerSessionId;

        /// <summary>
        /// Indicates whether the caller is the local system account.
        /// </summary>
        public static readonly bool CallerIsLocalSystem;

        /// <summary>
        /// Gets a read-only list of privileges associated with the caller.
        /// </summary>
        public static readonly IReadOnlyList<SE_PRIVILEGE> CallerPrivileges = PrivilegeManager.GetPrivileges();

        /// <summary>
        /// A read-only dictionary that maps <see cref="WellKnownSidType"/> values to their corresponding <see
        /// cref="SecurityIdentifier"/> instances.
        /// </summary>
        /// <remarks>This dictionary provides a lookup table for well-known security identifiers (SIDs)
        /// based on their type. It is intended to facilitate quick access to predefined SIDs commonly used in
        /// security-related operations.</remarks>
        private static readonly ReadOnlyDictionary<WellKnownSidType, SecurityIdentifier> WellKnownSidLookupTable;
    }
}
