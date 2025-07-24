#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.IO;

namespace WinFastGUI
{
    [SupportedOSPlatform("windows")]
    public static class WindowsRestartManager
    {
        private const int CCH_RM_MAX_APP_NAME = 255;
        private const int CCH_RM_MAX_SVC_NAME = 63;

        [StructLayout(LayoutKind.Sequential)]
        internal struct RM_UNIQUE_PROCESS
        {
            public uint dwProcessId;
            public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct RM_PROCESS_INFO
        {
            public RM_UNIQUE_PROCESS Process;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCH_RM_MAX_APP_NAME + 1)]
            public string strAppName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCH_RM_MAX_SVC_NAME + 1)]
            public string strServiceShortName;
            public uint ApplicationType;
            public uint AppStatus;
            public uint TSSessionId;
            [MarshalAs(UnmanagedType.Bool)]
            public bool bRestartable;
        }

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, string strSessionKey);

        [DllImport("rstrtmgr.dll")]
        static extern int RmEndSession(uint pSessionHandle);

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        static extern int RmRegisterResources(uint pSessionHandle, uint nFiles, string[] rgsFilenames,
            uint nApplications, [In] RM_UNIQUE_PROCESS[]? rgApplications, uint nServices, string[]? rgsServiceNames);

        [DllImport("rstrtmgr.dll")]
        static extern int RmGetList(uint pSessionHandle, out uint pnProcInfoNeeded,
            ref uint pnProcInfo, [In, Out] RM_PROCESS_INFO[]? rgAffectedApps, ref uint lpdwRebootReasons);

        public static List<Process> GetProcessesLockingFile(string filePath)
        {
            var processes = new List<Process>();
            if (!File.Exists(filePath)) return processes;

            uint sessionHandle;
            string sessionKey = Guid.NewGuid().ToString();
            int currentProcessId = Process.GetCurrentProcess().Id;

            if (RmStartSession(out sessionHandle, 0, sessionKey) != 0) return processes;

            try
            {
                string[] resources = { filePath };
                if (RmRegisterResources(sessionHandle, 1, resources, 0, null, 0, null) != 0) return processes;

                uint pnProcInfoNeeded = 0;
                uint pnProcInfo = 0;
                uint lpdwRebootReasons = 0;

                int res = RmGetList(sessionHandle, out pnProcInfoNeeded, ref pnProcInfo, null, ref lpdwRebootReasons);
                if (res != 234) return processes; // ERROR_MORE_DATA değilse devam etme

                RM_PROCESS_INFO[] affectedApps = new RM_PROCESS_INFO[pnProcInfoNeeded];
                pnProcInfo = pnProcInfoNeeded;
                res = RmGetList(sessionHandle, out pnProcInfoNeeded, ref pnProcInfo, affectedApps, ref lpdwRebootReasons);

                if (res == 0)
                {
                    for (int i = 0; i < pnProcInfo; i++)
                    {
                        uint processId = affectedApps[i].Process.dwProcessId;

                        if (processId == currentProcessId) continue; // Kendi prosesini listeye ekleme

                        try
                        {
                            processes.Add(Process.GetProcessById((int)processId));
                        }
                        catch (ArgumentException) { /* Proses biz kontrol ederken kapanmış olabilir */ }
                    }
                }
            }
            finally
            {
                RmEndSession(sessionHandle);
            }
            return processes;
        }
    }
}