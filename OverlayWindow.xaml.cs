using Playnite.SDK;
using Playnite.SDK.Models;
using SharpDX.XInput;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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

        private readonly int MAX_BAR_WIDTH = 53;
        private readonly int BAR_RIGHT = 158;
        private readonly int BAR_TOP = 29;
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
            this.Hide();
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
            this.Show();


            // Set focus to first button when showing overlay
            ReturnToGameButton.Focus();
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
                ProcessInfo_DEBUG.Text += "PROCESS INFO:\nName: " + gameProcess.ProcessName +
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

        // Button click event handlers
        private void ReturnToGameButton_Click(object sender, RoutedEventArgs e)
        {
            var proc = FindRunningGameProcess();
            if (proc != null)
            {
                // Bring the game window to the front
                SetForegroundWindow(proc.MainWindowHandle);
                // Restore the window if it's minimized
                if (IsIconic(proc.MainWindowHandle))
                {
                    ShowWindow(proc.MainWindowHandle, SW_RESTORE);
                }
            }
            this.Hide();
        }

        private void CloseGameButton_Click(object sender, RoutedEventArgs e)
        {
            // Get the currently running game
            var runningGame = ActiveGame ?? playniteAPI.Database.Games.FirstOrDefault(g => g.IsRunning);

            if (runningGame != null)
            {
                //var gamePath = runningGame.GameActions.Where(a => a.Type == GameActionType.File).FirstOrDefault();
                //var emulatorPath = runningGame.GameActions.Where(a => a.Type == GameActionType.Emulator).FirstOrDefault();
                var proc = FindRunningGameProcess();
                Debug.WriteLine("Trying to close " + runningGame.Name);
                if (proc != null)
                {
                    Debug.WriteLine("Closing game: " + runningGame.Name + " PID: " + proc.Id);
                    proc.CloseMainWindow();
                    proc.Close();
                }
                // Hide the overlay
                this.Hide();
            }
        }

        private void ShowPlayniteButton_Click(object sender, RoutedEventArgs e)
        {
            // Find all processes with the name "playnite"
            ShowPlaynite();
            // Hide the overlay
            this.Hide();
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

        // Win32 API declarations
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        // ShowWindow command parameter
        private const int SW_RESTORE = 9;

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

            try
            {
                // Get all executable files in the game installation directory and subdirectories
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

                // Option 1: If Playnite was able to get the process ID, use it
                if (Pid != null)
                {
                    try
                    {
                        var p = Process.GetProcessById(Pid.Value);
                        if (p != null && !p.HasExited)
                        {
                            // Try to access MainModule
                            try
                            {
                                var modulePath = p.MainModule.FileName;
                                if (modulePath.IndexOf(runningGame.InstallDirectory, StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    return p;
                                }
                            }
                            catch
                            {
                                // Check if process name matches any executable in the game folder
                                if (gameExecutables.Any(exe => string.Equals(exe, p.ProcessName, StringComparison.OrdinalIgnoreCase)))
                                {
                                    Debug.WriteLine($"Process {p.ProcessName} matches a game executable filename");
                                    return p;
                                }

                                // Return it anyway since it matches the expected PID
                                Debug.WriteLine($"Using process by ID {p.Id} but couldn't verify path");
                                return p;
                            }
                        }
                    }
                    catch { /* Process might not exist anymore */ }
                }

                // Option 2: Look for likely candidates based on timing
                DateTime gameStartTime = GameStarted.Value;
                Process[] allProcesses = Process.GetProcesses();

                var candidates = new List<Process>();
                var nameMatchCandidates = new List<Process>();
                var inaccessibleCandidates = new List<Process>();

                foreach (var p in allProcesses)
                {
                    try
                    {
                        // Check start time and memory usage first
                        if (!p.HasExited &&
                            p.StartTime < gameStartTime.AddMinutes(2) &&
                            p.StartTime > gameStartTime.AddMinutes(-3) &&
                            p.WorkingSet64 > 100 * 1024 * 1024)
                        {
                            // Check if process name matches any executable in the game folder
                            bool nameMatches = gameExecutables.Any(exe =>
                                string.Equals(exe, p.ProcessName, StringComparison.OrdinalIgnoreCase));
                             
                            try
                            {
                                // Try to access the module info
                                var modulePath = p.MainModule.FileName;
                                if (modulePath.IndexOf(runningGame.InstallDirectory, StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    candidates.Add(p);
                                }
                                else if (nameMatches)
                                {
                                    // Path doesn't match but name does - this is a good candidate
                                    nameMatchCandidates.Add(p);
                                }
                            }
                            catch
                            {
                                // Can't access module info (likely due to 32/64 bit mismatch)
                                if (nameMatches)
                                {
                                    // Process name matches executable - higher priority
                                    nameMatchCandidates.Add(p);
                                }
                                else
                                {
                                    // No name match, but timing and memory usage match
                                    inaccessibleCandidates.Add(p);
                                }
                            }
                        }
                    }
                    catch { /* Skip processes we can't access at all */ }
                }

                // Priority order: direct path match, process name match, then best guess
                if (candidates.Count > 0)
                {
                    var bestMatch = candidates.OrderByDescending(p => p.WorkingSet64).First();
                    Debug.WriteLine($"Found process with matching path: {bestMatch.ProcessName} (ID: {bestMatch.Id})");
                    return bestMatch;
                }

                if (nameMatchCandidates.Count > 0)
                {
                    var bestMatch = nameMatchCandidates.OrderByDescending(p => p.WorkingSet64).First();
                    Debug.WriteLine($"Found process with matching name: {bestMatch.ProcessName} (ID: {bestMatch.Id})");
                    return bestMatch;
                }

                if (inaccessibleCandidates.Count > 0)
                {
                    var bestGuess = inaccessibleCandidates.OrderByDescending(p => p.WorkingSet64).First();
                    Debug.WriteLine($"Using best guess process: {bestGuess.ProcessName} (ID: {bestGuess.Id})");
                    return bestGuess;
                }
            }
            catch (Exception ex)
            {
                // Log error
                Debug.WriteLine($"Error finding game process: {ex.Message}");
            }

            return null;
        }
        // Helper method to safely check process path against directory

        private void UpdateClock(object sender, EventArgs e)
        {
            Clock.Text = DateTime.Now.ToString("HH:mm");
        }


    }
}