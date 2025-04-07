using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PlayniteGameOverlay
{
    class ButtonActions
    {
        public static void FocusOrLaunchDiscord()
        {

            bool processFound = false;
            try
            {
                Process[] processes = Process.GetProcessesByName("Discord");
                if (processes.Length > 0)
                {
                    processFound = true;

                    // Try to bring the window to the foreground
                    foreach (var process in processes)
                    {
                        try
                        {
                            // Get the main window handle
                            IntPtr handle = process.MainWindowHandle;

                            if (handle != IntPtr.Zero)
                            {
                                // Restore window if minimized
                                ShowWindow(handle, SW_MAXIMIZE);

                                // Bring to foreground
                                SetForegroundWindow(handle);

                                break; // Exit after focusing the first valid window
                            }
                        }
                        catch
                        {
                            // Continue to the next process if this one fails
                            continue;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error checking for existing process: {ex.Message}");
            }

            // If no running process was found or focused, start discord via URI
            if (!processFound)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = @"discord://-/",
                    UseShellExecute = true
                });
            }
        }

        public static void ExecuteKbdShortcut(string keystroke)
        {
            SendKeys.SendWait(keystroke);
        }

        public static void FocusOrLaunch(string path)
        {
            bool processFound = false;
            try
            {
                string fileName = Path.GetFileNameWithoutExtension(path);
                Process[] processes = Process.GetProcessesByName(fileName);
                if (processes.Length > 0)
                {
                    processFound = true;

                    // Try to bring the window to the foreground
                    foreach (var process in processes)
                    {
                        try
                        {
                            // Get the main window handle
                            IntPtr handle = process.MainWindowHandle;

                            if (handle != IntPtr.Zero)
                            {
                                // Restore window if minimized
                                ShowWindow(handle, SW_MAXIMIZE);

                                // Bring to foreground
                                SetForegroundWindow(handle);

                                break; // Exit after focusing the first valid window
                            }
                        }
                        catch
                        {
                            // Continue to the next process if this one fails
                            continue;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error checking for existing process: {ex.Message}");
            }

            // If no running process was found or focused, start a new one
            if (!processFound)
            {
                Process.Start(path);
            }
        }

        // Win32 API Declarations
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_MAXIMIZE = 3;

    }
}
