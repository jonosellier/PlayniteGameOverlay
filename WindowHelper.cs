using System;
using System.Runtime.InteropServices;

namespace PlayniteGameOverlay
{
    public static class WindowHelper
    {
        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        public const int SW_RESTORE = 9;
        public const int SW_MAXIMIZE = 3;

        public static void ForceFocusWindow(IntPtr hwnd)
        {
            IntPtr foregroundWindow = GetForegroundWindow();
            uint foregroundThread = GetWindowThreadProcessId(foregroundWindow, out _);
            uint currentThread = GetCurrentThreadId();

            AttachThreadInput(currentThread, foregroundThread, true);
            SetForegroundWindow(hwnd);
            AttachThreadInput(currentThread, foregroundThread, false);
        }

        public static bool IsFolderOpen(string folderPath)
        {
            // Move the implementation from the existing code
            return false;
        }
    }
}