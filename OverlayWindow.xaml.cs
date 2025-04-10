using System;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Input;
using System.Windows.Interop;
using System.Linq;
using System.Diagnostics;
using System.Windows.Forms;

namespace PlayniteGameOverlay
{
    public partial class OverlayWindow : Window
    {
        private readonly Logger _logger;
        private readonly GameStateManager _gameStateManager;
        private readonly BatteryManager _batteryManager;

        private readonly DispatcherTimer clockTimer;
        private DispatcherTimer batteryUpdateTimer;
        private DispatcherTimer controllerTimer;

        private DateTime lastUpTime = DateTime.MinValue;
        private DateTime lastDownTime = DateTime.MinValue;
        private DateTime lastLeftTime = DateTime.MinValue;
        private DateTime lastRightTime = DateTime.MinValue;

        OverlaySettings Settings;

        public bool Active = false;

        public OverlayWindowViewModel ViewModel { get; private set; }

        public OverlayWindow(OverlaySettings settings)
        {
            InitializeComponent();

            Settings = settings;

            _logger = new Logger(settings?.DebugMode ?? false);
            _gameStateManager = new GameStateManager(_logger);
            _batteryManager = new BatteryManager();

            // Initialize ViewModel
            ViewModel = new OverlayWindowViewModel();
            this.DataContext = ViewModel;

            // Connect ViewModel events to handlers
            ViewModel.HideOverlayRequested += () => {
                if (ViewModel.IsGameRunning)
                {
                    var proc = FindProcessById(ViewModel.ProcessId);
                    if (proc != null)
                    {
                        // Restore the window if it's minimized
                        if (WindowHelper.IsIconic(proc.MainWindowHandle))
                        {
                            WindowHelper.ShowWindow(proc.MainWindowHandle, WindowHelper.SW_RESTORE);
                        }
                        // Bring the game window to the front
                        WindowHelper.SetForegroundWindow(proc.MainWindowHandle);
                    }
                }
                this.Hide(); 
            };
            ViewModel.ShowPlayniteRequested += (fullscreen) => {
                this.Hide();
                OnShowPlayniteRequested?.Invoke(); 
            };
            ViewModel.CloseGameRequested += () => {
                this.Hide();
                CloseGame(); 
            };
            ViewModel.ExecuteShortcutRequested += ExecuteShortcut;

            // Set the window to fullscreen
            this.WindowState = WindowState.Maximized;

            // Initialize and start the clock timer
            clockTimer = new DispatcherTimer();
            clockTimer.Interval = TimeSpan.FromSeconds(1);
            clockTimer.Tick += UpdateClock;
            clockTimer.Start();

            InitializeBatteryDisplay();

            // Disable keyboard navigation
            this.PreviewKeyDown += OverlayWindow_PreviewKeyDown;

            // Initialize buttons and setup from settings
            ViewModel.DebugVisible = settings?.DebugMode ?? false;
            ViewModel.InitializeShortcutButtons(settings);
        }

        private void InitializeBatteryDisplay()
        {
            if (!_batteryManager.HasBattery)
            {
                ViewModel.HasBattery = false;
            }
            else
            {
                ViewModel.HasBattery = true;
                UpdateBattery(null, EventArgs.Empty);
                batteryUpdateTimer = new DispatcherTimer();
                batteryUpdateTimer.Interval = TimeSpan.FromSeconds(60);
                batteryUpdateTimer.Tick += UpdateBattery;
                batteryUpdateTimer.Start();
            }
        }

        private void UpdateBattery(object sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                var batteryStatus = _batteryManager.GetStatus();
                ViewModel.BatteryPercentage = ((int)batteryStatus.Percentage*100);
            });
        }

        private void UpdateClock(object sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                // Update session time if game is active
                ViewModel.UpdateSessionTime();
                ViewModel.UpdateCurrentTime();
            });
        }

        public void UpdateGameOverlay(GameOverlayData gameData)
        {
            Dispatcher.Invoke(() =>
            {
                ViewModel.UpdateFromGameOverlayData(gameData);

                if (ViewModel.DebugVisible)
                {
                    if (gameData == null)
                    {
                        ViewModel.UpdateDebugInfo("No active game");
                    }
                    else
                    {
                        ViewModel.UpdateDebugInfo($"DEBUG INFO:\n" +
                            $"Game: {gameData.GameName}\n" +
                            $"PID: {gameData.ProcessId}\n" +
                            $"Start Time: {gameData.GameStartTime}");
                    }
                }
            });
        }

        private void CloseGame()
        {
            _gameStateManager.CloseGame(CreateGameOverlayDataFromViewModel());
            this.Hide();
        }

        private void ExecuteShortcut(ShortcutButtonViewModel btn)
        {
            switch (btn.Type)
            {
                case ShortcutType.Discord:
                    ButtonActions.FocusOrLaunchDiscord();
                    break;
                case ShortcutType.Path:
                    ButtonActions.FocusOrLaunch(btn.ShortcutCommand);
                    break;
                case ShortcutType.KbdShortcut:
                    // hide then execute shortcut
                    this.Hide();
                    ButtonActions.ExecuteKbdShortcut(btn.ShortcutCommand);
                    return;
            }

            // Return to game after executing other shortcuts
            this.Hide();
        }

        private GameOverlayData CreateGameOverlayDataFromViewModel()
        {
            // Create a GameOverlayData object from the ViewModel
            return new GameOverlayData
            {
                GameName = ViewModel.GameTitle,
                ProcessId = ViewModel.ProcessId,
                GameStartTime = ViewModel.SessionStartTime,
                Playtime = ViewModel.TotalPlaytime,
                CoverImagePath = ViewModel.CoverImagePath
                // Add other properties as needed
            };
        }

        public void ShowOverlay()
        {
            Active = true;
            // Resume timers and immediately update
            ResumeTimers();

            // Update immediately
            UpdateClock(null, EventArgs.Empty);
            UpdateBattery(null, EventArgs.Empty);

            // Show the window
            Dispatcher.Invoke(() =>
            {
                this.Show();
                // Activate the window to bring it to the foreground and set focus
                this.Activate();
                ForceFocusOverlay();
                // Set focus to first button when showing overlay
                ReturnToGameButton.Focus();
            });

        }

        public void ForceFocusOverlay()
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            WindowHelper.ForceFocusWindow(hwnd);
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

                if (controllerTimer != null)
                {
                    controllerTimer.Start();
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

                if (controllerTimer != null)
                {
                    controllerTimer.Stop();
                }
            });
        }

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

        protected override void OnClosed(EventArgs e)
        {
            ControllerManager.Instance.Dispose();

            // Stop timers
            PauseTimers();

            base.OnClosed(e);
        }

        public new void Hide()
        {
            Active = false;
            // Pause timers when hiding
            PauseTimers();

            // Call base Hide method
            Dispatcher.Invoke(() =>
            {
                base.Hide();
            });
        }

        // Navigation methods for the OverlayWindow class
        private void OverlayWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            // List of keys to block
            var blockedKeys = new[]
            {
                System.Windows.Input.Key.Tab,
                System.Windows.Input.Key.Left,
                System.Windows.Input.Key.Right,
                System.Windows.Input.Key.Up,
                System.Windows.Input.Key.Down
            };

            if (blockedKeys.Contains(e.Key))
            {
                e.Handled = true; // Prevent navigation
            }
        }

        // Focus navigation methods for controller support
        public void FocusDown()
        {
            Dispatcher.Invoke(() =>
            {

                UIElement focusedElement = Keyboard.FocusedElement as UIElement;
                if (focusedElement != null)
                {
                    _logger.Log($"Moving focus from {focusedElement.GetType().Name} to next element", "SDL_NAV");
                    focusedElement.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                    _logger.Log($"Focus now on: {(Keyboard.FocusedElement as FrameworkElement)?.Name ?? "unknown"}", "SDL_NAV");
                }
                else
                {
                    _logger.Log("No element currently has focus for next navigation", "SDL_NAV");
                }

            });
}

        public void FocusUp()
        {
            Dispatcher.Invoke(() =>
            {

                UIElement focusedElement = Keyboard.FocusedElement as UIElement;
                if (focusedElement != null)
                {
                    _logger.Log($"Moving focus from {focusedElement.GetType().Name} to previous element", "SDL_NAV");
                    focusedElement.MoveFocus(new TraversalRequest(FocusNavigationDirection.Previous));
                    _logger.Log($"Focus now on: {(Keyboard.FocusedElement as FrameworkElement)?.Name ?? "unknown"}", "SDL_NAV");
                }
                else
                {
                    _logger.Log("No element currently has focus for previous navigation", "SDL_NAV");
                }

            });
        }

        public void FocusLeft()
        {
            Dispatcher.Invoke(() =>
            {

                UIElement focusedElement = Keyboard.FocusedElement as UIElement;
                if (focusedElement != null)
                {
                    _logger.Log($"Moving focus from {focusedElement.GetType().Name} to left element", "SDL_NAV");
                    focusedElement.MoveFocus(new TraversalRequest(FocusNavigationDirection.Left));
                    _logger.Log($"Focus now on: {(Keyboard.FocusedElement as FrameworkElement)?.Name ?? "unknown"}", "SDL_NAV");
                }
                else
                {
                    _logger.Log("No element currently has focus for left navigation", "SDL_NAV");
                }

            });
        }

        public void FocusRight()
        {
            Dispatcher.Invoke(() =>
            {

                UIElement focusedElement = Keyboard.FocusedElement as UIElement;
                if (focusedElement != null)
                {
                    _logger.Log($"Moving focus from {focusedElement.GetType().Name} to right element", "SDL_NAV");
                    focusedElement.MoveFocus(new TraversalRequest(FocusNavigationDirection.Right));
                    _logger.Log($"Focus now on: {(Keyboard.FocusedElement as FrameworkElement)?.Name ?? "unknown"}", "SDL_NAV");
                }
                else
                {
                    _logger.Log("No element currently has focus for right navigation", "SDL_NAV");
                }

            });
        }

        public void ClickFocusedElement()
        {
            Dispatcher.Invoke(() =>
            {
                if (Keyboard.FocusedElement is System.Windows.Controls.Button button)
                {
                    _logger.Log($"Clicking button: {button.Name}", "SDL_NAV");

                    // Try to invoke the Click event handler directly if available
                    if (button.Command != null && button.Command.CanExecute(button.CommandParameter))
                    {
                        button.Command.Execute(button.CommandParameter);
                    }
                    else
                    {
                        // Create a click routed event
                        RoutedEventArgs args = new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent);

                        // Raise the event on the button
                        button.RaiseEvent(args);
                    }
                }
                else
                {
                    _logger.Log($"Focused element is not a button: {Keyboard.FocusedElement?.GetType().Name ?? "null"}", "SDL_NAV");
                }
            });
        }

        // Event to request showing Playnite (to be handled by the plugin)
        public event Action OnShowPlayniteRequested;
        // Event to request showing the Overlay (to be handled by the plugin)
        public event Action OnShowOverlayRequested;
    }
}