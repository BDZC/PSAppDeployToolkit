﻿using System;
using System.Linq;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using PSADT.LibraryInterfaces;
using Windows.Win32;
using Windows.Win32.System.SystemInformation;

namespace PSADT.OperatingSystem
{
    /// <summary>
    /// Utility singleton class for getting OS version info.
    /// </summary>
    public sealed record OSVersionInfo
    {
        /// <summary>
        /// This operating system's version information.
        /// </summary>
        public static readonly OSVersionInfo Current = new();

        /// <summary>
        /// Constructor for the singleton.
        /// </summary>
        private OSVersionInfo()
        {
            static bool IsOperatingSystemEnterpriseMultiSessionOS(OS_PRODUCT_TYPE productType, string? editionId, string? productName)
            {
                // If the ProductType is 3 (Server), perform additional checks.
                if (productType == OS_PRODUCT_TYPE.PRODUCT_DATACENTER_SERVER)
                {
                    if ("EnterpriseMultiSession".Equals(editionId, StringComparison.OrdinalIgnoreCase) || "ServerRdsh".Equals(editionId, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrWhiteSpace(productName) && (productName!.IndexOf("Virtual Desktops", StringComparison.OrdinalIgnoreCase) >= 0 || productName!.IndexOf("Multi-Session", StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            return true;
                        }
                    }
                }
                return false;
            }

            NtDll.RtlGetVersion(out var osVersion);
            var suiteMask = (SUITE_MASK)osVersion.wSuiteMask;
            var productType = (PRODUCT_TYPE)osVersion.wProductType;
            string? editionId = null;
            string? productName = null;
            int ubr = 0;

            var windowsOS = (((ulong)osVersion.dwMajorVersion) << 48) | (((ulong)osVersion.dwMinorVersion) << 32) | (((ulong)osVersion.dwBuildNumber) << 16);
            if (Enum.IsDefined(typeof(WindowsOS), windowsOS))
            {
                OperatingSystem = (WindowsOS)windowsOS;
            }

            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion")!)
            {
                if (key.GetValue("UBR") is int ubrValue)
                {
                    ubr = ubrValue;
                }
                if (key.GetValue("ReleaseId") is string relId && !string.IsNullOrWhiteSpace(relId))
                {
                    ReleaseId = relId;
                }
                if (key.GetValue("DisplayVersion") is string relIdVer && !string.IsNullOrWhiteSpace(relIdVer))
                {
                    ReleaseIdName = relIdVer;
                }
                if (key.GetValue("EditionID") is string editionIdValue && !string.IsNullOrWhiteSpace(editionIdValue))
                {
                    editionId = editionIdValue;
                }
                if (key.GetValue("ProductName") is string productNameValue && !string.IsNullOrWhiteSpace(productNameValue))
                {
                    productName = productNameValue;
                }
            }

            PInvoke.GetProductInfo(osVersion.dwMajorVersion, osVersion.dwMinorVersion, osVersion.wServicePackMajor, osVersion.wServicePackMinor, out OS_PRODUCT_TYPE edition);
            Name = string.Format(((DescriptionAttribute[])typeof(WindowsOS).GetField(OperatingSystem.ToString())!.GetCustomAttributes(typeof(DescriptionAttribute), false)).First().Description, editionId);
            Version = new Version((int)osVersion.dwMajorVersion, (int)osVersion.dwMinorVersion, (int)osVersion.dwBuildNumber, ubr);
            Edition = edition.ToString();
            Architecture = RuntimeInformation.OSArchitecture;
            Is64BitOperatingSystem = Environment.Is64BitOperatingSystem;
            IsTerminalServer = ((suiteMask & SUITE_MASK.VER_SUITE_TERMINAL) == SUITE_MASK.VER_SUITE_TERMINAL) && !((suiteMask & SUITE_MASK.VER_SUITE_SINGLEUSERTS) == SUITE_MASK.VER_SUITE_SINGLEUSERTS);
            IsWorkstationEnterpriseMultiSessionOS = IsOperatingSystemEnterpriseMultiSessionOS(edition, editionId, productName);
            IsWorkstation = productType == PRODUCT_TYPE.VER_NT_WORKSTATION;
            IsServer = !IsWorkstation;
            IsDomainController = productType == PRODUCT_TYPE.VER_NT_DOMAIN_CONTROLLER;
        }

        /// <summary>
        /// Enum representing the operating system version as a packed unsigned long.
        /// </summary>
        public readonly WindowsOS OperatingSystem = WindowsOS.Unknown;

        /// <summary>
        /// Display name of the operating system.
        /// </summary>
        public readonly string Name;

        /// <summary>
        /// Edition of the operating system.
        /// </summary>
        public readonly string Edition;

        /// <summary>
        /// Version of the operating system.
        /// </summary>
        public readonly Version Version;

        /// <summary>
        /// Release Id of the operating system.
        /// </summary>
        public readonly string? ReleaseId;

        /// <summary>
        /// Release Id name of the operating system.
        /// </summary>
        public readonly string? ReleaseIdName;

        /// <summary>
        /// Architecture of the operating system.
        /// </summary>
        public readonly Architecture Architecture;

        /// <summary>
        /// Whether the operating system is 64-bit.
        /// </summary>
        public readonly bool Is64BitOperatingSystem;

        /// <summary>
        /// Whether the operating system is a terminal server.
        /// </summary>
        public readonly bool IsTerminalServer;

        /// <summary>
        /// Whether the operating system is a workstation capable of multiple sessions (AVD, etc).
        /// </summary>
        public readonly bool IsWorkstationEnterpriseMultiSessionOS;

        /// <summary>
        /// Whether the operating system is a workstation.
        /// </summary>
        public readonly bool IsWorkstation;

        /// <summary>
        /// Whether the operating system is a server.
        /// </summary>
        public readonly bool IsServer;

        /// <summary>
        /// Whether the operating system is a domain controller.
        /// </summary>
        public readonly bool IsDomainController;
    }
}
