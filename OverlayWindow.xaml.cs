using Playnite.SDK;
using Playnite.SDK.Models;
using SharpDX.XInput;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PlayniteGameOverlay
{
    public partial class OverlayWindow : Window
    {
        private readonly IPlayniteAPI playniteAPI;
        private readonly DispatcherTimer clockTimer;
        private readonly DispatcherTimer batteryUpdateTimer;
        private readonly DispatcherTimer controllerTimer;

        private readonly Controller controller;
        private Gamepad previousState;
        private bool isLeftStickInNeutralPosition = true;
        private bool isDPadInNeutralPosition = true;

        // Battery bar constants
        private readonly int MAX_BAR_WIDTH = 53;
        private readonly int BAR_RIGHT = 158;
        private readonly int BAR_TOP = 29;

        // GetWindow constants
        private const uint GW_CHILD = 5;
        private const uint GW_HWNDNEXT = 2;

        // Window message constants
        private const uint WM_ACTIVATE = 0x0006;
        private readonly IntPtr WA_ACTIVE = new IntPtr(1);

        // ShowWindow command parameter
        private const int SW_RESTORE = 9;

        private bool wasGameWindowEnabled = true;
        private HashSet<IntPtr> disabledWindows = new HashSet<IntPtr>();

        public int barWidth = 0;

        public Game ActiveGame;
        public Nullable<DateTime> GameStarted;
        public Nullable<int> Pid;
        private static readonly ILogger logger = LogManager.GetLogger();

        private Process gameProcess;



        public OverlayWindow(IPlayniteAPI api)
        {
            InitializeComponent();
            playniteAPI = api;

            // Set the window to fullscreen
            this.WindowState = WindowState.Maximized;

            // Connect button click events
            ReturnToGameButton.Click += ReturnToGameButton_Click;
            CloseGameButton.Click += CloseGameButton_Click;
            ShowPlayniteButton.Click += ShowPlayniteButton_Click;

            UpdateGameInfo();
            playniteAPI.Database.Games.ItemUpdated += OnGameUpdated;

            // Initialize and start the clock timer
            clockTimer = new DispatcherTimer();
            clockTimer.Interval = TimeSpan.FromSeconds(1);
            clockTimer.Tick += UpdateClock;
            clockTimer.Start();


            if (SystemInformation.PowerStatus.BatteryChargeStatus == BatteryChargeStatus.NoSystemBattery)
            {
                Battery.Visibility = Visibility.Collapsed;
            }
            else
            {
                Battery.Visibility = Visibility.Visible;
                batteryUpdateTimer = new DispatcherTimer();
                batteryUpdateTimer.Interval = TimeSpan.FromSeconds(60);
                batteryUpdateTimer.Tick += (sender, e) => UpdateBattery();
                batteryUpdateTimer.Start();
            }

            // Initialize XInput controller
            controller = new Controller(UserIndex.One);

            // Setup controller polling timer
            controllerTimer = new DispatcherTimer();
            controllerTimer.Interval = TimeSpan.FromMilliseconds(50); // 50ms polling rate
            controllerTimer.Tick += CheckControllerInput;
            controllerTimer.Start();

            // Set initial focus to first button
            ReturnToGameButton.Focus();
            this.Closing += OverlayWindow_Closing;
        }

        private void OverlayWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // Make sure to re-enable game window input when closing
            EnableGameWindowInput();
        }

        private void CheckControllerInput(object sender, EventArgs e)
        {
            if (!controller.IsConnected)
                return;

            var gamepadState = controller.GetState().Gamepad;

            // Handle button presses
            if (IsNewButtonPress(gamepadState.Buttons, previousState.Buttons, GamepadButtonFlags.A))
            {
                // Simulate a click on the focused button
                if (FocusManager.GetFocusedElement(this) is System.Windows.Controls.Button focusedButton)
                {
                    focusedButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                }
            }

            if (IsNewButtonPress(gamepadState.Buttons, previousState.Buttons, GamepadButtonFlags.B))
            {
                B_Button_Pressed();
            }

            if (IsNewButtonPress(gamepadState.Buttons, previousState.Buttons, GamepadButtonFlags.X))
            {
                X_Button_Pressed();
            }

            if (IsNewButtonPress(gamepadState.Buttons, previousState.Buttons, GamepadButtonFlags.Y))
            {
                Y_Button_Pressed();
            }

            // Handle D-pad navigation
            if (isLeftStickInNeutralPosition && isDPadInNeutralPosition)
            {
                // Move focus up
                if (gamepadState.LeftThumbY > 8000 || (gamepadState.Buttons & GamepadButtonFlags.DPadUp) != 0)
                {
                    MoveFocus(FocusNavigationDirection.Up);
                    isLeftStickInNeutralPosition = false;
                    isDPadInNeutralPosition = false;
                }
                // Move focus down
                else if (gamepadState.LeftThumbY < -8000 || (gamepadState.Buttons & GamepadButtonFlags.DPadDown) != 0)
                {
                    MoveFocus(FocusNavigationDirection.Down);
                    isLeftStickInNeutralPosition = false;
                    isDPadInNeutralPosition = false;
                }
            }

            // Reset stick/d-pad neutral flags when input returns to neutral position
            if (Math.Abs(gamepadState.LeftThumbY) < 5000)
            {
                isLeftStickInNeutralPosition = true;
            }

            if ((gamepadState.Buttons & (GamepadButtonFlags.DPadUp | GamepadButtonFlags.DPadDown)) == 0)
            {
                isDPadInNeutralPosition = true;
            }

            // Save current state for next comparison
            previousState = gamepadState;
        }

        private bool IsNewButtonPress(GamepadButtonFlags currentButtons, GamepadButtonFlags previousButtons, GamepadButtonFlags button)
        {
            bool isCurrentlyPressed = (currentButtons & button) != 0;
            bool wasPreviouslyPressed = (previousButtons & button) != 0;

            return isCurrentlyPressed && !wasPreviouslyPressed;
        }

        private void MoveFocus(FocusNavigationDirection direction)
        {
            if (FocusManager.GetFocusedElement(this) is UIElement focusedElement)
            {
                focusedElement.MoveFocus(new TraversalRequest(direction));
            }
        }

        // Controller button handlers
        private void B_Button_Pressed()
        {
            // By default, B acts like an "Exit/Cancel" button
            Hide();
        }

        private void X_Button_Pressed()
        {
            // Custom X button functionality
            Debug.WriteLine("X button pressed");
        }

        private void Y_Button_Pressed()
        {
            // Custom Y button functionality
            Debug.WriteLine("Y button pressed");
        }

        private void UpdateBattery()
        {
            PowerStatus power = SystemInformation.PowerStatus;
            float batteryPercentage = power.BatteryLifePercent;
            barWidth = (int)(MAX_BAR_WIDTH * batteryPercentage);
            BatteryBar.Width = barWidth;
            BatteryBar.Margin = new Thickness(0, BAR_TOP, MAX_BAR_WIDTH - barWidth + BAR_RIGHT, 0);
            BatteryText.Text = (int)(batteryPercentage * 100) + "%";
        }

        public void ShowOverlay()
        {
            UpdateGameInfo();
            UpdateDebugInfo();

            // Disable game window input before showing overlay
            gameProcess = FindRunningGameProcess();
            if (gameProcess != null && gameProcess.MainWindowHandle != IntPtr.Zero)
            {
                DisableGameWindowInput();
            }

            this.Show();

            // Set focus to first button when showing overlay
            ReturnToGameButton.Focus();
        }

        public new void Hide()
        {
            // Re-enable game window input
            EnableGameWindowInput();

            // Call the base Hide method
            base.Hide();
        }

        private void DisableGameWindowInput()
        {
            try
            {
                // Store the current state and disable the main window
                IntPtr mainWindowHandle = gameProcess.MainWindowHandle;
                if (mainWindowHandle != IntPtr.Zero && IsWindow(mainWindowHandle))
                {
                    // Store the window state so we can restore it later
                    wasGameWindowEnabled = IsWindowEnabled(mainWindowHandle);

                    // Disable the main window
                    EnableWindow(mainWindowHandle, false);
                    disabledWindows.Add(mainWindowHandle);

                    // Optionally disable child windows to be thorough
                    DisableChildWindows(mainWindowHandle);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error disabling game window input: {ex.Message}");
            }
        }

        private void EnableGameWindowInput()
        {
            try
            {
                // Re-enable all windows we disabled
                foreach (IntPtr windowHandle in disabledWindows)
                {
                    if (IsWindow(windowHandle))
                    {
                        EnableWindow(windowHandle, true);
                    }
                }

                // Clear the collection
                disabledWindows.Clear();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error enabling game window input: {ex.Message}");
            }
        }

        private void DisableChildWindows(IntPtr parentWindow)
        {
            try
            {
                // Get first child window
                IntPtr childWindow = GetWindow(parentWindow, GW_CHILD);

                // Iterate through all child windows
                while (childWindow != IntPtr.Zero)
                {
                    if (IsWindow(childWindow) && IsWindowEnabled(childWindow))
                    {
                        EnableWindow(childWindow, false);
                        disabledWindows.Add(childWindow);

                        // Recursively disable this child's children
                        DisableChildWindows(childWindow);
                    }

                    // Get next child window
                    childWindow = GetWindow(childWindow, GW_HWNDNEXT);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error disabling child windows: {ex.Message}");
            }
        }

        private void OnGameUpdated(object sender, ItemUpdatedEventArgs<Game> e)
        {
            UpdateGameInfo();
        }


        public void UpdateDebugInfo()
        {
            ProcessInfo_DEBUG.Text = "DEBUG INFO:\nGame: "+ActiveGame.Name+"\nPID from Playnite: "+Pid+"\nGame Start Time: "+GameStarted;

            gameProcess = FindRunningGameProcess();

            if (gameProcess != null)
            {
                ProcessInfo_DEBUG.Text += "\nPROCESS INFO:\nName: " + gameProcess.ProcessName +
                         "\nPID: " + gameProcess.Id;
            }
        }

        private void UpdateGameInfo()
        {
            var runningGame = ActiveGame ?? playniteAPI.Database.Games.FirstOrDefault(g => g.IsRunning);

            if (runningGame != null)
            {
                GameTitle.Text = runningGame.Name;

                // Load the cover image if available
                if (!string.IsNullOrEmpty(runningGame.CoverImage))
                {
                    try
                    {
                        string imagePath = playniteAPI.Database.GetFullFilePath(runningGame.CoverImage);
                        GameCoverImage.Source = new BitmapImage(new System.Uri(imagePath));
                        GameCoverImage.Visibility = Visibility.Visible;
                    }
                    catch
                    {
                        GameCoverImage.Visibility = Visibility.Collapsed;
                    }
                }
                else
                {
                    GameCoverImage.Visibility = Visibility.Collapsed;
                }

                var playtime = TimeSpan.FromSeconds(runningGame.Playtime);
                string playtimeString = (int)playtime.TotalMinutes + " mins. played";
                if (playtime.TotalMinutes >= 120)
                {
                    playtimeString = (int)playtime.TotalHours + " hours played";
                }

                Playtime.Text = playtimeString;
            }
            else
            {
                ActiveGame = null;
            }
        }

        private void ReturnToGameButton_Click(object sender, RoutedEventArgs e)
        {
            // First hide our overlay
            Hide();

            // Use a slight delay to ensure everything settles before trying to focus the game
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            timer.Tick += (s, args) => {
                timer.Stop();
                ForceFocusGameWindow();
            };
            timer.Start();
        }


        private void CloseGameButton_Click(object sender, RoutedEventArgs e)
        {
            // Get the currently running game
            var runningGame = ActiveGame ?? playniteAPI.Database.Games.FirstOrDefault(g => g.IsRunning);

            if (runningGame != null)
            {
                var proc = FindRunningGameProcess();
                Debug.WriteLine("Trying to close " + runningGame.Name);
                if (proc != null)
                {
                    Debug.WriteLine("Closing game: " + runningGame.Name + " PID: " + proc.Id);
                    proc.CloseMainWindow();
                    proc.Close();
                }
                // Hide the overlay
                Hide(); // Will call our overridden method
            }
        }

        private void ShowPlayniteButton_Click(object sender, RoutedEventArgs e)
        {
            // Find all processes with the name "playnite"
            ShowPlaynite();
            // Hide the overlay
            Hide(); // Will call our overridden method
        }

        public void ShowPlaynite()
        {
            var playniteProcesses = Process.GetProcesses().Where(p => p.ProcessName.IndexOf("playnite", StringComparison.OrdinalIgnoreCase) >= 0).ToArray();

            foreach (var proc in playniteProcesses)
            {
                // Bring the Playnite window to the front
                SetForegroundWindow(proc.MainWindowHandle);
                // Restore the window if it's minimized
                if (IsIconic(proc.MainWindowHandle))
                {
                    ShowWindow(proc.MainWindowHandle, SW_RESTORE);
                }
            }
        }

        private void ForceFocusGameWindow()
        {
            FindRunningGameProcess();

            if(gameProcess == null)
            {
                return;
            }

            try
            {
                IntPtr mainWindowHandle = gameProcess.MainWindowHandle;
                if (mainWindowHandle == IntPtr.Zero)
                    return;

                // Get window placement info
                WINDOWPLACEMENT placement = new WINDOWPLACEMENT();
                placement.length = Marshal.SizeOf(placement);
                GetWindowPlacement(mainWindowHandle, ref placement);

                // First try minimizing and then restoring the window
                // This often helps with focus issues
                ShowWindow(mainWindowHandle, SW_MINIMIZE);
                System.Threading.Thread.Sleep(50);
                ShowWindow(mainWindowHandle, SW_RESTORE);

                // Use all available methods to activate the window
                BringWindowToTop(mainWindowHandle);
                SetForegroundWindow(mainWindowHandle);
                SetActiveWindow(mainWindowHandle);

                // For stubborn applications, simulating Alt+Tab can help
                SimulateAltTab(mainWindowHandle);

                // Force activation via input
                SendMessage(mainWindowHandle, WM_ACTIVATE, WA_ACTIVE, IntPtr.Zero);
                SendMessage(mainWindowHandle, WM_SETFOCUS, IntPtr.Zero, IntPtr.Zero);

                Debug.WriteLine($"Attempted to force focus to window {gameProcess.ProcessName}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error forcing focus to game window: {ex.Message}");
            }
        }


        private void FocusGameWindow()
        {
            if (gameProcess != null && !gameProcess.HasExited)
            {
                try
                {
                    IntPtr mainWindowHandle = gameProcess.MainWindowHandle;
                    if (mainWindowHandle != IntPtr.Zero)
                    {
                        // Restore the window if it's minimized
                        if (IsIconic(mainWindowHandle))
                        {
                            ShowWindow(mainWindowHandle, SW_RESTORE);
                        }

                        // Focus the window
                        SetForegroundWindow(mainWindowHandle);

                        // Additional forceful focus method
                        SetActiveWindow(mainWindowHandle);
                        BringWindowToTop(mainWindowHandle);

                        // Some applications might require this approach to restore focus
                        SendMessage(mainWindowHandle, WM_ACTIVATE, WA_ACTIVE, IntPtr.Zero);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error focusing game window: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Attempts to find the process for the currently running game
        /// </summary>
        /// <returns>The game process if found, null otherwise</returns>
        private Process FindRunningGameProcess()
        {
            // First, check if there's a game running according to Playnite
            var runningGame = ActiveGame ?? playniteAPI.Database.Games.FirstOrDefault(g => g.IsRunning);

            if (runningGame == null)
            {
                return null; // No game is running
            }

            if (gameProcess != null && !gameProcess.HasExited)
            {
                return gameProcess; // Return the cached process if it's still running
            }
            else
            {
                gameProcess = null; // Reset the cached process
            }

            try
            {
                // Get all executable files in the game installation directory
                var gameExecutables = new List<string>();
                try
                {
                    gameExecutables = Directory.GetFiles(runningGame.InstallDirectory, "*.exe", SearchOption.AllDirectories)
                        .Select(path => Path.GetFileNameWithoutExtension(path))
                        .ToList();

                    // Add common variations
                    var additionalNames = new List<string>();
                    foreach (var exe in gameExecutables)
                    {
                        // Add variations like "game-win64", "game_launcher", etc.
                        additionalNames.Add(exe.Replace("-", ""));
                        additionalNames.Add(exe.Replace("_", ""));
                        additionalNames.Add(exe.Replace(" ", ""));
                    }
                    gameExecutables.AddRange(additionalNames);

                    Debug.WriteLine($"Found {gameExecutables.Count} potential game executables in {runningGame.InstallDirectory}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error scanning game directory: {ex.Message}");
                }

                // Get start time for timing-based matching
                DateTime gameStartTime = GameStarted.Value;

                // Get all processes with visible windows
                var processesWithWindows = GetProcessesWithVisibleWindows();
                Debug.WriteLine($"Found {processesWithWindows.Count} processes with visible windows");

                var candidates = new List<Process>();
                var nameMatchCandidates = new List<Process>();
                var timingMatchCandidates = new List<Process>();

                foreach (var p in processesWithWindows)
                {
                    try
                    {
                        if (p.HasExited)
                            continue;

                        // Check if window title contains game name (often a good indicator)
                        string windowTitle = GetWindowText(p.MainWindowHandle).ToLower();
                        bool titleMatches = !string.IsNullOrEmpty(windowTitle) &&
                                           windowTitle.Contains(runningGame.Name.ToLower());

                        // Check if process name matches any executable in the game folder
                        bool nameMatches = gameExecutables.Any(exe =>
                            string.Equals(exe, p.ProcessName, StringComparison.OrdinalIgnoreCase));

                        // Check if timing matches (started around when the game was launched)
                        bool timingMatches = p.StartTime < gameStartTime.AddMinutes(2) &&
                                            p.StartTime > gameStartTime.AddMinutes(-3);

                        // Check substantial memory usage (typical for games)
                        bool hasSubstantialMemory = p.WorkingSet64 > 100 * 1024 * 1024;

                        try
                        {
                            // Try to access the module info
                            var modulePath = p.MainModule.FileName;
                            if (modulePath.IndexOf(runningGame.InstallDirectory, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                // Direct path match is the best indicator
                                candidates.Add(p);
                                Debug.WriteLine($"Found process with matching path: {p.ProcessName} (ID: {p.Id})");
                            }
                            else if (titleMatches)
                            {
                                // Window title match is a strong indicator
                                candidates.Add(p);
                                Debug.WriteLine($"Found process with matching window title: {p.ProcessName} (ID: {p.Id}, Title: {windowTitle})");
                            }
                            else if (nameMatches && hasSubstantialMemory)
                            {
                                // Name match with substantial memory is a good indicator
                                nameMatchCandidates.Add(p);
                                Debug.WriteLine($"Found process with matching name and substantial memory: {p.ProcessName} (ID: {p.Id})");
                            }
                        }
                        catch
                        {
                            // Can't access module info (likely due to 32/64 bit mismatch)
                            if (titleMatches)
                            {
                                // Window title match is still a strong indicator
                                nameMatchCandidates.Add(p);
                                Debug.WriteLine($"Found process with matching window title (module inaccessible): {p.ProcessName} (ID: {p.Id})");
                            }
                            else if (nameMatches && hasSubstantialMemory)
                            {
                                // Name match with substantial memory
                                nameMatchCandidates.Add(p);
                                Debug.WriteLine($"Found process with matching name (module inaccessible): {p.ProcessName} (ID: {p.Id})");
                            }
                            else if (timingMatches && hasSubstantialMemory)
                            {
                                // Timing match with substantial memory
                                timingMatchCandidates.Add(p);
                                Debug.WriteLine($"Found process with matching timing and substantial memory: {p.ProcessName} (ID: {p.Id})");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error analyzing process {p.ProcessName}: {ex.Message}");
                    }
                }

                // Priority order: direct path/title match, name match, then timing match
                if (candidates.Count > 0)
                {
                    var bestMatch = candidates.OrderByDescending(p => p.WorkingSet64).First();
                    Debug.WriteLine($"Selected best candidate: {bestMatch.ProcessName} (ID: {bestMatch.Id})");
                    gameProcess = bestMatch;
                    return bestMatch;
                }

                if (nameMatchCandidates.Count > 0)
                {
                    var bestMatch = nameMatchCandidates.OrderByDescending(p => p.WorkingSet64).First();
                    Debug.WriteLine($"Selected best name match: {bestMatch.ProcessName} (ID: {bestMatch.Id})");
                    gameProcess = bestMatch;
                    return bestMatch;
                }

                if (timingMatchCandidates.Count > 0)
                {
                    var bestGuess = timingMatchCandidates.OrderByDescending(p => p.WorkingSet64).First();
                    Debug.WriteLine($"Selected best timing match: {bestGuess.ProcessName} (ID: {bestGuess.Id})");
                    // Don't store this one as we want to try to do better next time
                    return bestGuess;
                }

                Debug.WriteLine("Could not find any matching process with a window");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error finding game process: {ex.Message}");
            }

            return null;
        }

        // Helper method to get window text
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        private string GetWindowText(IntPtr hWnd)
        {
            int length = GetWindowTextLength(hWnd);
            if (length == 0)
                return string.Empty;

            StringBuilder builder = new StringBuilder(length + 1);
            GetWindowText(hWnd, builder, builder.Capacity);
            return builder.ToString();
        }

        // Helper method to get all processes with visible windows
        private List<Process> GetProcessesWithVisibleWindows()
        {
            var result = new List<Process>();
            Process[] processes = Process.GetProcesses();

            foreach (Process process in processes)
            {
                if (process.MainWindowHandle != IntPtr.Zero && IsWindowVisible(process.MainWindowHandle))
                {
                    result.Add(process);
                }
            }

            return result;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        // Helper method to safely check process path against directory

        private void UpdateClock(object sender, EventArgs e)
        {
            Clock.Text = DateTime.Now.ToString("HH:mm");
        }

        private void SimulateAltTab(IntPtr targetWindow)
        {
            try
            {
                // Simulate releasing all keys first to avoid issues
                keybd_event(0, 0, KEYEVENTF_KEYUP, UIntPtr.Zero); // Release any pressed keys

                // Press Alt
                keybd_event(VK_MENU, 0, 0, UIntPtr.Zero);
                System.Threading.Thread.Sleep(50);

                // Press Tab
                keybd_event(VK_TAB, 0, 0, UIntPtr.Zero);
                System.Threading.Thread.Sleep(50);

                // Release Tab
                keybd_event(VK_TAB, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                System.Threading.Thread.Sleep(50);

                // Keep trying to set focus using standard methods
                SetForegroundWindow(targetWindow);

                // Finally release Alt
                keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error simulating Alt+Tab: {ex.Message}");
            }
        }

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        private const byte VK_MENU = 0x12; // ALT key
        private const byte VK_TAB = 0x09;  // TAB key
        private const uint KEYEVENTF_KEYUP = 0x0002;


        // Win32 API declarations
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool EnableWindow(IntPtr hWnd, bool enable);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowEnabled(IntPtr hWnd);

        // Add these Win32 API declarations
        [DllImport("user32.dll")]
        private static extern IntPtr SetActiveWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

        [StructLayout(LayoutKind.Sequential)]
        private struct WINDOWPLACEMENT
        {
            public int length;
            public int flags;
            public int showCmd;
            public System.Drawing.Point ptMinPosition;
            public System.Drawing.Point ptMaxPosition;
            public System.Drawing.Rectangle rcNormalPosition;
        }

        private const int SW_MINIMIZE = 6;
        private const uint WM_SETFOCUS = 0x0007;
    }
}