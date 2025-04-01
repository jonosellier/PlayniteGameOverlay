using System;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using System.Windows.Forms;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Linq;

namespace PlayniteGameOverlay
{
    public partial class OverlayWindow : Window
    {
        private readonly DispatcherTimer clockTimer;
        private DispatcherTimer batteryUpdateTimer;

        // Thread-safe data storage
        private volatile GameOverlayData _currentGameData;

        // Constants for battery display
        private double MAX_BAR_WIDTH;
        private double BAR_RIGHT;
        private double BAR_TOP;
        private double barWidth;

        public OverlayWindow()
        {
            InitializeComponent();

            // Set the window to fullscreen
            this.WindowState = WindowState.Maximized;

            // Connect button click events
            ReturnToGameButton.Click += ReturnToGameButton_Click;
            CloseGameButton.Click += CloseGameButton_Click;
            ShowPlayniteButton.Click += ShowPlayniteButton_Click;

            // Initialize and start the clock timer
            clockTimer = new DispatcherTimer();
            clockTimer.Interval = TimeSpan.FromSeconds(1);
            clockTimer.Tick += UpdateClock;
            clockTimer.Start();

            InitializeBatteryDisplay();

            // Set initial focus to first button
            ReturnToGameButton.Focus();
        }

        public void ShowOverlay()
        {
            // Resume timers and immediately update
            ResumeTimers();

            // Update immediately
            UpdateClock(null, EventArgs.Empty);
            UpdateBattery(null, EventArgs.Empty);

            // Show the window
            this.Show();

            // Activate the window to bring it to the foreground and set focus
            this.Activate();

            // Set focus to first button when showing overlay
            ReturnToGameButton.Focus();
        }

        private void ResumeTimers()
        {
            Dispatcher.Invoke(() =>
            {
                clockTimer.Start();

                if (batteryUpdateTimer != null)
                {
                    batteryUpdateTimer.Start();
                }
            });
        }

        private void PauseTimers()
        {
            Dispatcher.Invoke(() =>
            {
                clockTimer.Stop();

                if (batteryUpdateTimer != null)
                {
                    batteryUpdateTimer.Stop();
                }
            });
        }

        protected override void OnClosed(EventArgs e)
        {
            // Stop timers
            PauseTimers();

            base.OnClosed(e);
        }

        public new void Hide()
        {
            // Pause timers when hiding
            PauseTimers();

            // Call base Hide method
            base.Hide();
        }

        private void InitializeBatteryDisplay()
        {
            if (SystemInformation.PowerStatus.BatteryChargeStatus == BatteryChargeStatus.NoSystemBattery)
            {
                Battery.Visibility = Visibility.Collapsed;
            }
            else
            {
                MAX_BAR_WIDTH = BatteryBar.Width;
                BAR_RIGHT = BatteryBar.Margin.Right;
                BAR_TOP = BatteryBar.Margin.Top;
                barWidth = 0;
                Battery.Visibility = Visibility.Visible;
                UpdateBattery(null, EventArgs.Empty);
                batteryUpdateTimer = new DispatcherTimer();
                batteryUpdateTimer.Interval = TimeSpan.FromSeconds(60);
                batteryUpdateTimer.Tick += UpdateBattery;
                batteryUpdateTimer.Start();
            }
        }

        // Method to update the overlay with new game data
        public void UpdateGameOverlay(GameOverlayData gameData)
        {
            Dispatcher.Invoke(() =>
            {
                _currentGameData = gameData;
                UpdateGameInfo(gameData);
                UpdateDebugInfo(gameData);
                UpdateAchievementData(gameData);
            });
        }

        private void UpdateAchievementData(GameOverlayData gameData)
        {
            if (gameData?.Achievements != null && gameData.Achievements.Count > 0)
            {
                var mostRecentAchievement = gameData.Achievements.FindAll(a => a.IsUnlocked).OrderByDescending(a => a.UnlockDate).FirstOrDefault();
                if(mostRecentAchievement == null)
                {
                    AchFade1.Visibility = Visibility.Collapsed;
                    AchFade2.Visibility = Visibility.Collapsed;
                    AchievementPanel.Visibility = Visibility.Collapsed;
                    UnlockPercent.Visibility = Visibility.Collapsed;
                    return;
                }
                else if (mostRecentAchievement.UnlockDate != null)
                {
                    TimeSpan timeSinceUnlock = (TimeSpan)(DateTime.Now - mostRecentAchievement.UnlockDate);
                    LastAchievementName.Text = mostRecentAchievement.Name;
                    LastAchievementDate.Text = timeSinceUnlock.TotalDays < 1 ? "Today" : $"{(int)timeSinceUnlock.TotalDays} days ago";
                    LastAchievementDescription.Text = mostRecentAchievement.Description;
                    try
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(mostRecentAchievement.IconUrl);
                        bitmap.CacheOption = BitmapCacheOption.OnLoad; // This loads the image fully before displaying
                        bitmap.EndInit();
                        LastAchievementIcon.Source = bitmap;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"ERROR: Failed to load achievement icon: {ex.Message}");
                    }
                    UnlockPercent.Text = $"Unlocked {gameData.Achievements.Count(a => a.IsUnlocked)}/{gameData.Achievements.Count} Achievements";
                    AchievementPanel.Visibility = Visibility.Visible;
                    UnlockPercent.Visibility = Visibility.Visible;
                    AchFade2.Visibility = Visibility.Visible;
                    AchFade1.Visibility = Visibility.Visible;
                }
            }
            else
            {
                AchFade1.Visibility = Visibility.Collapsed;
                AchFade2.Visibility = Visibility.Collapsed;
                AchievementPanel.Visibility = Visibility.Collapsed;
                UnlockPercent.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateGameInfo(GameOverlayData gameData)
        {
            if (gameData == null)
            {
                ResetGameDisplay();
                return;
            }

            GameTitle.Text = gameData.GameName;

            // Load cover image
            if (!string.IsNullOrEmpty(gameData.CoverImagePath))
            {
                try
                {
                    GameCoverImage.Source = new BitmapImage(new Uri(gameData.CoverImagePath));
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

            // Format playtime
            PlayTime.Text = FormatPlaytime(gameData.Playtime);
        }

        private void UpdateDebugInfo(GameOverlayData gameData)
        {
            if (gameData == null)
            {
                ProcessInfo_DEBUG.Text = "No active game";
                return;
            }

            ProcessInfo_DEBUG.Text = $"DEBUG INFO:\n" +
                $"Game: {gameData.GameName}\n" +
                $"PID: {gameData.ProcessId}\n" +
                $"Start Time: {gameData.GameStartTime}";
        }

        private void ResetGameDisplay()
        {
            GameTitle.Text = string.Empty;
            GameCoverImage.Source = null;
            GameCoverImage.Visibility = Visibility.Collapsed;
            PlayTime.Text = string.Empty;
            ProcessInfo_DEBUG.Text = string.Empty;
        }

        private string FormatPlaytime(TimeSpan playtime)
        {
            if (playtime.TotalMinutes < 120)
                return $"{(int)playtime.TotalMinutes} mins.";

            return $"{(int)playtime.TotalHours} hrs.";
        }

        private void UpdateBattery(object sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                PowerStatus power = SystemInformation.PowerStatus;
                float batteryPercentage = power.BatteryLifePercent;
                barWidth = (int)(MAX_BAR_WIDTH * batteryPercentage);
                BatteryBar.Width = barWidth;
                BatteryBar.Margin = new Thickness(0, BAR_TOP, MAX_BAR_WIDTH - barWidth + BAR_RIGHT, 0);
                BatteryText.Text = (int)(batteryPercentage * 100) + "%";
            });
        }

        private void UpdateClock(object sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                if (_currentGameData != null)
                {
                    var playTime = DateTime.Now - _currentGameData.GameStartTime;
                    SessionTime.Text = $"{playTime.Hours}:{playTime.Minutes:D2}:{playTime.Seconds:D2}";
                }
                Clock.Text = DateTime.Now.ToString("hh:mm");
                Clock.Text = DateTime.Now.ToString("hh:mm");
            });
        }

        // Button Click Handlers
        private void ReturnToGameButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentGameData != null)
            {
                var proc = FindProcessById(_currentGameData.ProcessId);
                if (proc != null)
                {
                    // Restore the window if it's minimized
                    if (IsIconic(proc.MainWindowHandle))
                    {
                        ShowWindow(proc.MainWindowHandle, SW_RESTORE);
                    }
                    // Bring the game window to the front
                    SetForegroundWindow(proc.MainWindowHandle);
                }
            }
            this.Hide();
        }

        private void CloseGameButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentGameData != null)
            {
                var proc = FindProcessById(_currentGameData.ProcessId);
                if (proc != null)
                {
                    proc.CloseMainWindow();
                    proc.Close();
                }
            }
            // Hide the overlay
            this.Hide();
        }

        private void ShowPlayniteButton_Click(object sender, RoutedEventArgs e)
        {
            // This method will need to be implemented by the plugin to show Playnite
            OnShowPlayniteRequested?.Invoke();
            this.Hide();
        }

        // Process finding helper
        private Process FindProcessById(int processId)
        {
            try
            {
                return Process.GetProcessById(processId);
            }
            catch
            {
                return null;
            }
        }

        // Win32 API Declarations
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_RESTORE = 9;

        // Event to request showing Playnite (to be handled by the plugin)
        public event Action OnShowPlayniteRequested;
    }
}