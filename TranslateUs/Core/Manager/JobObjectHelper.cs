using System.Diagnostics;
#if WINDOWS
using System.Runtime.InteropServices;
#endif

namespace TranslateUs.Core.Manager;

internal static class JobObjectHelper
{
    public static void AssignChildProcess(Process childProcess)
    {
#if WINDOWS
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            IntPtr hJob = CreateJobObject(IntPtr.Zero, null);
            if (hJob == IntPtr.Zero)
            {
                Main.Logger.LogWarning(
                    $"[JobObject] CreateJobObject failed: {Marshal.GetLastWin32Error()}");
                return;
            }

            var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
            {
                BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
                {
                    LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                }
            };

            int size = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(info, ptr, false);
                if (!SetInformationJobObject(
                        hJob, JOBOBJECTINFOCLASS.ExtendedLimitInformation, ptr, (uint)size))
                {
                    Main.Logger.LogWarning(
                        $"[JobObject] SetInformationJobObject failed: {Marshal.GetLastWin32Error()}");
                    return;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }

            IntPtr hChild = OpenProcess(
                PROCESS_SET_QUOTA | PROCESS_TERMINATE, false, childProcess.Id);
            if (hChild == IntPtr.Zero)
            {
                Main.Logger.LogWarning(
                    $"[JobObject] OpenProcess failed for PID {childProcess.Id}: {Marshal.GetLastWin32Error()}");
                return;
            }

            try
            {
                if (!AssignProcessToJobObject(hJob, hChild))
                {
                    Main.Logger.LogWarning(
                        $"[JobObject] AssignProcessToJobObject failed: {Marshal.GetLastWin32Error()}");
                }
                else
                {
                    Main.Logger.LogInfo(
                        $"[JobObject] llama-server (PID {childProcess.Id}) assigned to job — " +
                        "will auto-terminate when the game exits.");
                }
            }
            finally
            {
                CloseHandle(hChild);
            }
        }
        catch (Exception ex)
        {
            Main.Logger.LogWarning($"[JobObject] Failed to set up job object: {ex.Message}");
        }
#endif
    }

#if WINDOWS
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(
        IntPtr hJob,
        JOBOBJECTINFOCLASS JobObjectInfoClass,
        IntPtr lpJobObjectInfo,
        uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(
        uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    private enum JOBOBJECTINFOCLASS
    {
        ExtendedLimitInformation = 9
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;
    private const uint PROCESS_SET_QUOTA = 0x0100;
    private const uint PROCESS_TERMINATE = 0x0001;
#endif
}
