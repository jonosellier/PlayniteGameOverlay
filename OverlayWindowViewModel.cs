using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Playnite.SDK;
using Playnite.SDK.Models;

namespace PlayniteGameOverlay
{
    public class OverlayWindowViewModel : INotifyPropertyChanged
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private string _gameTitle;
        private string _coverImagePath;
        private TimeSpan _totalPlaytime;
        private TimeSpan _sessionPlaytime;
        private DateTime _sessionStartTime;
        private string _currentTime;
        private float _batteryPercentage;
        private bool _debugVisible;
        private string _debugInfo;
        private bool _isGameRunning;
        private int _processId;
        private bool _hasAchievements;
        private AchievementData _lastAchievement;
        private string _unlockPercentText;
        private ObservableCollection<ShortcutButtonViewModel> _shortcutButtons;
        private bool _hasBattery;

        public bool ShowShortcutButtons => ShortcutButtons.Count > 0;

        public string GameTitle
        {
            get => _gameTitle;
            set { _gameTitle = value; OnPropertyChanged(); }
        }

        public string CoverImagePath
        {
            get => _coverImagePath;
            set { _coverImagePath = value; OnPropertyChanged(); }
        }

        public TimeSpan TotalPlaytime
        {
            get => _totalPlaytime;
            set { _totalPlaytime = value; OnPropertyChanged(); OnPropertyChanged(nameof(TotalPlaytimeText)); }
        }

        public string TotalPlaytimeText
        {
            get
            {
                int hours = (int)TotalPlaytime.TotalHours;
                return $"{hours} {(hours == 1 ? "Hour" : "Hours")}";
            }
        }

        public TimeSpan SessionPlaytime
        {
            get => _sessionPlaytime;
            set { _sessionPlaytime = value; OnPropertyChanged(); OnPropertyChanged(nameof(SessionPlaytimeText)); }
        }

        public string SessionPlaytimeText
        {
            get => SessionPlaytime.ToString(@"hh\:mm\:ss");
        }

        public DateTime SessionStartTime
        {
            get => _sessionStartTime;
            set { _sessionStartTime = value; OnPropertyChanged(); }
        }

        public string CurrentTime
        {
            get => _currentTime;
            set { _currentTime = value; OnPropertyChanged(); }
        }

        public float BatteryPercentage
        {
            get => _batteryPercentage;
            set { _batteryPercentage = value; OnPropertyChanged(); }
        }

        public int BatteryBarWidth
        {
            get => (int)(BatteryPercentage * 0.35); // Calculate width based on percentage
        }

        public bool DebugVisible
        {
            get => _debugVisible;
            set { _debugVisible = value; OnPropertyChanged(); }
        }

        public string DebugInfo
        {
            get => _debugInfo;
            set { _debugInfo = value; OnPropertyChanged(); }
        }

        public bool IsGameRunning
        {
            get => _isGameRunning;
            set { _isGameRunning = value; OnPropertyChanged(); }
        }

        public int ProcessId
        {
            get => _processId;
            set { _processId = value; OnPropertyChanged(); }
        }

        public bool HasAchievements
        {
            get => _hasAchievements;
            set { _hasAchievements = value; OnPropertyChanged(); }
        }

        public bool HasBattery
        {
            get => _hasBattery;
            set { _hasBattery = value; OnPropertyChanged(); }
        }

        public AchievementData LastAchievement
        {
            get => _lastAchievement;
            set { _lastAchievement = value; OnPropertyChanged(); }
        }

        public string LastAchievementTimeText { 
            get {
                if (LastAchievement == null)
                {
                    return string.Empty;
                }
                if (LastAchievement.UnlockDate.HasValue)
                {
                    var span = DateTime.Now - LastAchievement.UnlockDate.Value;
                    return span.TotalHours > 24 ? $"{(int)span.TotalDays} days ago" : $"{(int)span.TotalHours} hours ago";
                }
                else
                {
                    return "recently";
                }
            }
        }

        public string UnlockPercentText
        {
            get => _unlockPercentText;
            set { _unlockPercentText = value; OnPropertyChanged(); }
        }

        public ObservableCollection<ShortcutButtonViewModel> ShortcutButtons
        {
            get => _shortcutButtons;
            set { _shortcutButtons = value; OnPropertyChanged(); }
        }

        // Commands
        public ICommand ReturnToGameCommand { get; }
        public ICommand ShowPlayniteCommand { get; }
        public ICommand CloseGameCommand { get; }
        public ICommand ShortcutButtonCommand { get; }

        // Event for showing Playnite
        public event Action<bool> ShowPlayniteRequested;
        public event Action HideOverlayRequested;
        public event Action CloseGameRequested;
        public event Action<ShortcutButtonViewModel> ExecuteShortcutRequested;

        // Constructor with design-time data support
        public OverlayWindowViewModel(bool designMode = false)
        {
            ShortcutButtons = new ObservableCollection<ShortcutButtonViewModel>();

            // Initialize commands
            ReturnToGameCommand = new RelayCommand(_ => HideOverlayRequested?.Invoke());
            ShowPlayniteCommand = new RelayCommand(_ => ShowPlayniteRequested?.Invoke(false));
            CloseGameCommand = new RelayCommand(_ => CloseGameRequested?.Invoke());
            ShortcutButtonCommand = new RelayCommand(ExecuteShortcut);

            if (designMode || System.ComponentModel.DesignerProperties.GetIsInDesignMode(new DependencyObject()))
            {
                // Initialize with sample data for design time
                GameTitle = "Sample Game Title";
                HasBattery = true;
                BatteryPercentage = 75;
                CurrentTime = "12:34";
                TotalPlaytime = TimeSpan.FromHours(125);
                SessionPlaytime = TimeSpan.FromMinutes(154);
                HasAchievements = true;
                UnlockPercentText = "Unlocked 42/100 Achievements";
                DebugVisible = true;
                DebugInfo = "DEBUG MODE ENABLED";

                // Sample achievement
                LastAchievement = new AchievementData
                {
                    Name = "Sample Achievement",
                    Description = "This is a sample achievement description for design-time display.",
                    IconUrl = "/PlayniteGameOverlay;component/assets/achievement.png"
                };

                // Add sample shortcut buttons
                InitializeShortcutButtons(new OverlaySettings
                {
                    ShowRecordGameplay = true,
                    ShowRecordRecent = true,
                    ShowStreaming = true,
                    ShowPerformanceOverlay = true,
                    ShowScreenshotGallery = true,
                    ShowWebBrowser = true,
                    ShowDiscord = true
                });
            }
        }


        public void UpdateFromGameOverlayData(GameOverlayData data)
        {
            if (data == null)
            {
                IsGameRunning = false;
                return;
            }

            IsGameRunning = true;
            GameTitle = data.GameName;
            ProcessId = data.ProcessId;
            SessionStartTime = data.GameStartTime;
            TotalPlaytime = data.Playtime;
            CoverImagePath = data.CoverImagePath;

            // Process achievements
            HasAchievements = data.Achievements != null && data.Achievements.Count > 0;

            if (HasAchievements)
            {
                // Find the most recent unlocked achievement
                AchievementData lastUnlocked = null;
                int totalAchievements = data.Achievements.Count;
                int unlockedCount = 0;

                foreach (var achievement in data.Achievements)
                {
                    if (achievement.IsUnlocked)
                    {
                        unlockedCount++;
                        if (lastUnlocked == null ||
                            (achievement.UnlockDate.HasValue && lastUnlocked.UnlockDate.HasValue &&
                             achievement.UnlockDate > lastUnlocked.UnlockDate))
                        {
                            lastUnlocked = achievement;
                        }
                    }
                }

                LastAchievement = lastUnlocked ?? new AchievementData
                {
                    Name = "No Achievements Unlocked",
                    Description = "Keep playing to unlock achievements",
                    IconUrl = "https://icons.veryicon.com/png/o/business/classic-icon/trophy-20.png"
                };

                UnlockPercentText = $"Unlocked {unlockedCount}/{totalAchievements} Achievements";
            }
            else
            {
                LastAchievement = new AchievementData
                {
                    Name = "No Achievements Available",
                    Description = "This game doesn't have achievements tracked",
                    IconUrl = "https://icons.veryicon.com/png/o/business/classic-icon/trophy-20.png"
                };
                UnlockPercentText = string.Empty;
            }
        }

        public void UpdateSessionTime()
        {
            if (SessionStartTime != default)
            {
                SessionPlaytime = DateTime.Now - SessionStartTime;
            }
        }

        public void UpdateCurrentTime()
        {
            CurrentTime = DateTime.Now.ToString("HH:mm");
        }

        public void UpdateBatteryStatus(int percentage)
        {
            BatteryPercentage = percentage;
        }

        private void ExecuteShortcut(object parameter)
        {
            if (parameter is ShortcutButtonViewModel shortcut)
            {
                ExecuteShortcutRequested?.Invoke(shortcut);
            }
        }

        public void UpdateDebugInfo(string info)
        {
            DebugInfo = info;
        }

        public void InitializeShortcutButtons(OverlaySettings settings)
        {
            ShortcutButtons.Clear();

            // Add buttons based on settings
            if (settings.ShowRecordGameplay)
            {
                ShortcutButtons.Add(new ShortcutButtonViewModel
                {
                    Title = "Record Gameplay",
                    IconPath = "/PlayniteGameOverlay;component/assets/record.png",
                    ShortcutCommand = settings.RecordGameplayShortcut,
                    Type = ShortcutType.KbdShortcut,
                });
            }

            if (settings.ShowRecordRecent)
            {
                ShortcutButtons.Add(new ShortcutButtonViewModel
                {
                    Title = "Record Last 30s",
                    IconPath = "/PlayniteGameOverlay;component/assets/record-recent.png",
                    ShortcutCommand = settings.RecordRecentShortcut,
                    Type = ShortcutType.KbdShortcut,
                });
            }

            if (settings.ShowStreaming)
            {
                ShortcutButtons.Add(new ShortcutButtonViewModel
                {
                    Title = "Streaming",
                    IconPath = "/PlayniteGameOverlay;component/assets/stream.png",
                    ShortcutCommand = settings.StreamingShortcut,
                    Type = ShortcutType.KbdShortcut,
                });
            }

            if (settings.ShowPerformanceOverlay)
            {
                ShortcutButtons.Add(new ShortcutButtonViewModel
                {
                    Title = "Performance",
                    IconPath = "/PlayniteGameOverlay;component/assets/performance.png",
                    ShortcutCommand = settings.PerformanceOverlayShortcut,
                    Type = ShortcutType.KbdShortcut,
                });
            }

            if (settings.ShowScreenshotGallery)
            {
                ShortcutButtons.Add(new ShortcutButtonViewModel
                {
                    Title = "Screenshots",
                    IconPath = "/PlayniteGameOverlay;component/assets/screenshot.png",
                    ShortcutCommand = settings.ScreenshotGalleryPath,
                    Type = ShortcutType.Path,
                });
            }

            if (settings.ShowWebBrowser)
            {
                ShortcutButtons.Add(new ShortcutButtonViewModel
                {
                    Title = "Web Browser",
                    IconPath = "/PlayniteGameOverlay;component/assets/browser.png",
                    ShortcutCommand = settings.WebBrowserPath,
                    Type = ShortcutType.Path,
                });
            }

            if (settings.ShowDiscord)
            {
                ShortcutButtons.Add(new ShortcutButtonViewModel
                {
                    Title = "Discord",
                    IconPath = "/PlayniteGameOverlay;component/assets/discord.png",
                    ShortcutCommand = "discord",
                    Type = ShortcutType.Discord
                });
            }
        }

        // INotifyPropertyChanged implementation
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class ShortcutButtonViewModel
    {
        public string Title { get; set; }
        public string IconPath { get; set; }
        public string ShortcutCommand { get; set; }
        public ShortcutType Type { get; set; }
    }

    public enum ShortcutType
    {
        Discord,
        Path,
        KbdShortcut
    }

    // Command implementation
    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Func<object, bool> _canExecute;

        public RelayCommand(Action<object> execute, Func<object, bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter) => _canExecute == null || _canExecute(parameter);

        public void Execute(object parameter) => _execute(parameter);

        public event EventHandler CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }
}