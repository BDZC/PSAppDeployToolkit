﻿using System;
using System.ComponentModel;
using Microsoft.Win32.SafeHandles;
using PSADT.SafeHandles;
using PSADT.Utilities;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.RemoteDesktop;

namespace PSADT.LibraryInterfaces
{
    /// <summary>
    /// CsWin32 P/Invoke wrappers for the wtsapi32.dll library.
    /// </summary>
    internal static class WtsApi32
    {
        /// <summary>
        /// Enumerates processes running on a specified server and retrieves detailed process information.
        /// </summary>
        /// <remarks>This method wraps the Windows API function <c>WTSEnumerateProcessesEx</c> and
        /// provides a managed interface for enumerating processes. The level of detail returned is determined by the
        /// <paramref name="pLevel"/> parameter: <list type="bullet"> <item><description>Level 0 returns basic process
        /// information (<c>WTS_PROCESS_INFOW</c>).</description></item> <item><description>Level 1 returns extended
        /// process information (<c>WTS_PROCESS_INFO_EXW</c>).</description></item> </list> The <paramref
        /// name="pProcessInfo"/> parameter must be disposed to avoid memory leaks.</remarks>
        /// <param name="hServer">A handle to the server on which the processes are to be enumerated. Use <see langword="null"/> to specify
        /// the local server.</param>
        /// <param name="pLevel">The level of detail for the process information. Use 0 for basic information or 1 for extended information.</param>
        /// <param name="SessionId">The session ID for which processes are to be enumerated. Use 0 to enumerate processes for all sessions.</param>
        /// <param name="pProcessInfo">When the method returns, contains a <see cref="SafeWtsExHandle"/> object that holds the enumerated process
        /// information. The caller is responsible for disposing of this handle to release the allocated resources.</param>
        /// <returns><see langword="true"/> if the operation succeeds; otherwise, <see langword="false"/>.</returns>
        internal static unsafe BOOL WTSEnumerateProcessesEx(HANDLE hServer, uint pLevel, uint SessionId, out SafeWtsExHandle pProcessInfo)
        {
            PWSTR ppProcessInfo; uint pCount; var res = PInvoke.WTSEnumerateProcessesEx(hServer, &pLevel, SessionId, &ppProcessInfo, &pCount);
            if (!res)
            {
                throw ExceptionUtilities.GetExceptionForLastWin32Error();
            }
            if (pLevel > 0)
            {
                pProcessInfo = new SafeWtsExHandle(new IntPtr(ppProcessInfo.Value), WTS_TYPE_CLASS.WTSTypeProcessInfoLevel1, (int)pCount * sizeof(WTS_PROCESS_INFO_EXW), true);
            }
            else
            {
                pProcessInfo = new SafeWtsExHandle(new IntPtr(ppProcessInfo.Value), WTS_TYPE_CLASS.WTSTypeProcessInfoLevel0, (int)pCount * sizeof(WTS_PROCESS_INFOW), true);
            }
            return res;
        }

        /// <summary>
        /// Wrapper around WTSEnumerateSessions to manage error handling.
        /// </summary>
        /// <param name="hServer"></param>
        /// <param name="pSessionInfo"></param>
        /// <returns></returns>
        internal static unsafe BOOL WTSEnumerateSessions(HANDLE hServer, out SafeWtsHandle pSessionInfo)
        {
            var res = PInvoke.WTSEnumerateSessions(hServer, 0, 1, out var ppSessionInfo, out var pCount);
            if (!res)
            {
                throw ExceptionUtilities.GetExceptionForLastWin32Error();
            }
            pSessionInfo = new SafeWtsHandle(new IntPtr(ppSessionInfo), (int)pCount * sizeof(WTS_SESSION_INFOW), true);
            return res;
        }

        /// <summary>
        /// Wrapper around WTSQuerySessionInformation to manage error handling.
        /// </summary>
        /// <param name="hServer"></param>
        /// <param name="SessionId"></param>
        /// <param name="WTSInfoClass"></param>
        /// <param name="pBuffer"></param>
        /// <returns></returns>
        /// <exception cref="Win32Exception"></exception>
        internal static unsafe BOOL WTSQuerySessionInformation(HANDLE hServer, uint SessionId, WTS_INFO_CLASS WTSInfoClass, out SafeWtsHandle pBuffer)
        {
            var res = PInvoke.WTSQuerySessionInformation(hServer, SessionId, WTSInfoClass, out var ppBuffer, out uint bytesReturned);
            if (!res)
            {
                throw ExceptionUtilities.GetExceptionForLastWin32Error();
            }
            pBuffer = new SafeWtsHandle(new IntPtr(ppBuffer), (int)bytesReturned, true);
            return res;
        }

        /// <summary>
        /// Wrapper around WTSQueryUserToken to manage error handling.
        /// </summary>
        /// <param name="SessionId"></param>
        /// <param name="phToken"></param>
        /// <returns></returns>
        /// <exception cref="Win32Exception"></exception>
        internal static unsafe BOOL WTSQueryUserToken(uint SessionId, out SafeFileHandle phToken)
        {
            HANDLE phTokenLocal;
            var res = PInvoke.WTSQueryUserToken(SessionId, &phTokenLocal);
            if (!res)
            {
                throw ExceptionUtilities.GetExceptionForLastWin32Error();
            }
            phToken = new SafeFileHandle(phTokenLocal, true);
            return res;
        }

        /// <summary>
        /// Wrapper around WTSFreeMemory to manage error handling.
        /// </summary>
        /// <param name="pMemory"></param>
        internal static unsafe void WTSFreeMemory(ref IntPtr pMemory)
        {
            if (pMemory != default && IntPtr.Zero != pMemory)
            {
                PInvoke.WTSFreeMemory(pMemory.ToPointer());
                pMemory = default;
            }
        }

        /// <summary>
        /// Releases memory allocated by the specified WTS API function.
        /// </summary>
        /// <remarks>This method wraps the native <c>WTSFreeMemoryEx</c> function and ensures proper
        /// cleanup of memory allocated by WTS API calls. It is the caller's responsibility to ensure that the memory
        /// being freed was allocated by a compatible WTS API function.</remarks>
        /// <param name="WTSTypeClass">The type class of the memory to be freed, indicating the structure or data type of the allocated memory.</param>
        /// <param name="pMemory">A reference to the pointer to the memory block to be freed. After the method is called successfully, the
        /// pointer is set to <see cref="IntPtr.Zero"/>.</param>
        /// <param name="NumberOfEntries">The number of entries in the memory block, if applicable. This value is used to determine the scope of the
        /// memory to be freed.</param>
        /// <returns><see langword="true"/> if the memory was successfully freed; otherwise, <see langword="false"/>.</returns>
        internal static unsafe BOOL WTSFreeMemoryEx(WTS_TYPE_CLASS WTSTypeClass, ref IntPtr pMemory, uint NumberOfEntries)
        {
            if (pMemory == default || IntPtr.Zero == pMemory)
            {
                return true;
            }
            var res = PInvoke.WTSFreeMemoryEx(WTSTypeClass, pMemory.ToPointer(), NumberOfEntries);
            if (!res)
            {
                throw ExceptionUtilities.GetExceptionForLastWin32Error();
            }
            pMemory = IntPtr.Zero;
            return res;
        }
    }

    /// <summary>
    /// Specifies the connection state of a Remote Desktop Services session.
    /// </summary>
    /// <remarks>
    /// <para><see href="https://learn.microsoft.com/windows/win32/api/wtsapi32/ne-wtsapi32-wts_connectstate_class">Learn more about this API from docs.microsoft.com</see>.</para>
    /// </remarks>
    public enum WTS_CONNECTSTATE_CLASS
    {
        /// <summary>
        /// A user is logged on to the WinStation. This state occurs when a user is signed in and actively connected to the device.
        /// </summary>
        WTSActive = Windows.Win32.System.RemoteDesktop.WTS_CONNECTSTATE_CLASS.WTSActive,

        /// <summary>
        /// The WinStation is connected to the client.
        /// </summary>
        WTSConnected = Windows.Win32.System.RemoteDesktop.WTS_CONNECTSTATE_CLASS.WTSConnected,

        /// <summary>
        /// The WinStation is in the process of connecting to the client.
        /// </summary>
        WTSConnectQuery = Windows.Win32.System.RemoteDesktop.WTS_CONNECTSTATE_CLASS.WTSConnectQuery,

        /// <summary>
        /// The WinStation is shadowing another WinStation.
        /// </summary>
        WTSShadow = Windows.Win32.System.RemoteDesktop.WTS_CONNECTSTATE_CLASS.WTSShadow,

        /// <summary>
        /// The WinStation is active but the client is disconnected. This state occurs when a user is signed in but not actively connected to the device, such as when the user has chosen to exit to the lock screen.
        /// </summary>
        WTSDisconnected = Windows.Win32.System.RemoteDesktop.WTS_CONNECTSTATE_CLASS.WTSDisconnected,

        /// <summary>
        /// The WinStation is waiting for a client to connect.
        /// </summary>
        WTSIdle = Windows.Win32.System.RemoteDesktop.WTS_CONNECTSTATE_CLASS.WTSIdle,

        /// <summary>
        /// The WinStation is listening for a connection. A listener session waits for requests for new client connections. No user is logged on a listener session. A listener session cannot be reset, shadowed, or changed to a regular client session.
        /// </summary>
        WTSListen = Windows.Win32.System.RemoteDesktop.WTS_CONNECTSTATE_CLASS.WTSListen,

        /// <summary>
        /// The WinStation is being reset.
        /// </summary>
        WTSReset = Windows.Win32.System.RemoteDesktop.WTS_CONNECTSTATE_CLASS.WTSReset,

        /// <summary>
        /// The WinStation is down due to an error.
        /// </summary>
        WTSDown = Windows.Win32.System.RemoteDesktop.WTS_CONNECTSTATE_CLASS.WTSDown,

        /// <summary>
        /// The WinStation is initializing.
        /// </summary>
        WTSInit = Windows.Win32.System.RemoteDesktop.WTS_CONNECTSTATE_CLASS.WTSInit,
    }
}
