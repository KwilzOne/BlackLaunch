using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;

namespace BlackLaunch;

public static partial class TaskbarProgress
{
    [GeneratedComInterface]
    [Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal partial interface ITaskbarList3
    {
        void HrInit();
        void AddTab(IntPtr hwnd);
        void DeleteTab(IntPtr hwnd);
        void ActivateTab(IntPtr hwnd);
        void SetActiveAlt(IntPtr hwnd);
        void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fFullscreen);
        void SetProgressValue(IntPtr hwnd, ulong ullCompleted, ulong ullTotal);
        void SetProgressState(IntPtr hwnd, TaskbarStates tbpFlags);
    }

    public enum TaskbarStates
    {
        NoProgress = 0,
        Indeterminate = 1,
        Normal = 2,
        Error = 4,
        Paused = 8
    }

    private static ITaskbarList3? _taskbarList;
    private static readonly bool _isSupported = OperatingSystem.IsWindowsVersionAtLeast(6, 1);

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(ref Guid rclsid, IntPtr pUnkOuter, int dwClsContext, ref Guid riid, out ITaskbarList3 ppv);

    [SupportedOSPlatform("windows")]
    private static void InitializeTaskbar()
    {
        if (_taskbarList == null) {
            Guid clsid = new("56fdf344-fd6d-11d0-958a-006097c9a090"); // CLSID_TaskbarList
            Guid iid = new("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf");   // IID_ITaskbarList3
            // 1 = CLSCTX_INPROC_SERVER
            int hr = CoCreateInstance(ref clsid, IntPtr.Zero, 1, ref iid, out var taskbarObj);
            if (hr == 0 && taskbarObj != null) {
                _taskbarList = taskbarObj;
                _taskbarList.HrInit();
            }
        }
    }

    public static void SetProgress(IntPtr windowHandle, int value, int max = 100)
    {
        if (!_isSupported || windowHandle == IntPtr.Zero) return;
        try {
            if (OperatingSystem.IsWindows()) {
                InitializeTaskbar();
                if (_taskbarList != null) {
                    if (value <= 0) {
                        _taskbarList.SetProgressState(windowHandle, TaskbarStates.NoProgress);
                    } else {
                        _taskbarList.SetProgressState(windowHandle, TaskbarStates.Normal);
                        _taskbarList.SetProgressValue(windowHandle, (ulong)value, (ulong)max);
                    }
                }
            }
        } catch { _taskbarList = null; }
    }

    public static void SetState(IntPtr windowHandle, TaskbarStates state)
    {
        if (!_isSupported || windowHandle == IntPtr.Zero) return;
        try {
            if (OperatingSystem.IsWindows()) {
                InitializeTaskbar();
                _taskbarList?.SetProgressState(windowHandle, state);
            }
        } catch { _taskbarList = null; }
    }
}
