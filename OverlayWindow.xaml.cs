using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PlayniteGameOverlay
{
    public partial class OverlayWindow : Window
    {
        private readonly IPlayniteAPI playniteAPI;
        private readonly DispatcherTimer clockTimer;
        private readonly DispatcherTimer batteryUpdateTimer;

        private readonly int MAX_BAR_WIDTH = 53;
        private readonly int BAR_RIGHT = 158;
        private readonly int BAR_TOP = 29;
        public int barWidth = 0;

        public Game ActiveGame;
        public Nullable<DateTime> GameStarted;
        public Nullable<int> Pid;
        private static readonly ILogger logger = LogManager.GetLogger();



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


            if(SystemInformation.PowerStatus.BatteryChargeStatus == BatteryChargeStatus.NoSystemBattery)
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
            this.Show();
        }

        private void OnGameUpdated(object sender, ItemUpdatedEventArgs<Game> e)
        {
            UpdateGameInfo();
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
                logger.Info("Trying to close " + runningGame.Name);
                if (proc != null)
                {
                    logger.Info("Closing game: " + runningGame.Name + " PID: " + proc.Id);
                    proc.CloseMainWindow();
                    proc.Close();
                    // Remove all indicators of games being active
                    ActiveGame = null;
                    GameStarted = null;
                    Pid = null;
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

                // Option 1: If Playnite was able to get the process ID, use it
                if (Pid != null)
                {
                    var p = Process.GetProcessById(Pid.Value);
                    if (p != null && p.MainModule.FileName.IndexOf(runningGame.InstallDirectory, StringComparison.OrdinalIgnoreCase) >= 0) // Process is in the game's install directory
                    {
                        return p;
                    }
                }

                // Option 2: Look for likely candidates based on timing
                // Get processes that started after the game was launched
                DateTime gameStartTime = GameStarted.Value;

                // In some Playnite versions, you might be able to get the actual start time
                if (runningGame.LastActivity.HasValue)
                {
                    gameStartTime = runningGame.LastActivity.Value;
                }

                // Find processes that started around when the game launched
                // Focus on processes using significant resources
                Process[] allProcesses = Process.GetProcesses();
                var candidates = allProcesses
                    .Where(p => {
                        try
                        {
                            return p.StartTime < gameStartTime.AddMinutes(2) && // 5 min window around game start time
                                   p.StartTime > gameStartTime.AddMinutes(-3) &&
                                   p.MainModule.FileName.IndexOf(runningGame.InstallDirectory, StringComparison.OrdinalIgnoreCase) >= 0 && // Process is in the game's install directory
                                   (p.WorkingSet64 > 100 * 1024 * 1024); // Over 100MB memory usage
                        }
                        catch { return false; } // Skip processes we can't access
                    })
                    .OrderByDescending(p => p.WorkingSet64) // Order by memory usage
                    .ToList();

                if (candidates.Count > 0)
                {
                    return candidates[0]; // Return the most likely candidate
                }
            }
            catch (Exception ex)
            {
                // Log error
                logger.Info($"Error finding game process: {ex.Message}");
            }

            return null;
        }

        private void UpdateClock(object sender, EventArgs e)
        {
            Clock.Text = DateTime.Now.ToString("HH:mm");
        }

    } 
}