using System;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using System.Windows.Forms;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Linq;
using SDL2;
using System.Windows.Input;
using System.Windows.Interop;

namespace PlayniteGameOverlay
{
    public partial class OverlayWindow : Window
    {
        private bool IS_DEBUG = true;

        private void log(string msg, string tag = "DEBUG")
        {
            if (IS_DEBUG)
            {
                Debug.WriteLine("GameOverlay[" + tag + "]: " + msg);
            }
        }

        private readonly DispatcherTimer clockTimer;
        private DispatcherTimer batteryUpdateTimer;

        // Thread-safe data storage
        private volatile GameOverlayData _currentGameData;

        // Constants for battery display
        private double MAX_BAR_WIDTH;
        private double BAR_RIGHT;
        private double BAR_TOP;
        private double barWidth;

        private DateTime lastUpTime = DateTime.MinValue;
        private DateTime lastDownTime = DateTime.MinValue;
        private DateTime lastLeftTime = DateTime.MinValue;
        private DateTime lastRightTime = DateTime.MinValue;

        private const int DEBOUNCE_THRESHOLD = 100; // 100 milliseconds debounce threshold

        public OverlayWindow(bool debug = false)
        {
            InitializeComponent();

            IS_DEBUG = debug;

            // Set the window to fullscreen
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

            InitializeController();

            // Disable keyboard navigation
            this.PreviewKeyDown += OverlayWindow_PreviewKeyDown;

            // Set initial focus to first button
            ReturnToGameButton.Focus();
        }

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

            ForceFocusOverlay();

            // Set focus to first button when showing overlay
            ReturnToGameButton.Focus();
        }

        public void ForceFocusOverlay()
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            IntPtr foregroundWindow = GetForegroundWindow();

            uint foregroundThread = GetWindowThreadProcessId(foregroundWindow, out _);
            uint currentThread = GetCurrentThreadId();

            AttachThreadInput(currentThread, foregroundThread, true);
            SetForegroundWindow(hwnd);
            AttachThreadInput(currentThread, foregroundThread, false);
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

        protected override void OnClosed(EventArgs e)
        {
                CloseController();
            // Clean up SDL
            if (sdlInitialized)
            {
                SDL.SDL_Quit();
                sdlInitialized = false;
            }

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
            if (IS_DEBUG)
            { 
                ProcessInfo_DEBUG.Visibility = Visibility.Visible;
                if (gameData == null)
                {
                    ProcessInfo_DEBUG.Text = "No active game";
                    return;
                }

                ProcessInfo_DEBUG.Text = $"DEBUG INFO:\n" +
                    $"Game: {gameData.GameName}\n" +
                    $"PID: {gameData.ProcessId}\n" +
                    $"Start Time: {gameData.GameStartTime}";
            } else
            {
                ProcessInfo_DEBUG.Visibility = Visibility.Collapsed;
            }
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


        #region SDL stuff

        private IntPtr controller = IntPtr.Zero;

        private bool sdlInitialized = false;
        private int controllerId = -1;
        private DispatcherTimer controllerTimer;

        private void InitializeController()
        {
            try
            {
                log("Initializing SDL controller support", "SDL");

                // Initialize SDL with game controller support
                if (SDL.SDL_Init(SDL.SDL_INIT_GAMECONTROLLER) < 0)
                {
                    string error = SDL.SDL_GetError();
                    log($"SDL could not initialize! SDL Error: {error}", "SDL_ERROR");
                    return;
                }

                sdlInitialized = true;
                log("SDL initialized successfully", "SDL");

                // Look for connected controllers
                int numJoysticks = SDL.SDL_NumJoysticks();
                log($"Found {numJoysticks} joysticks/controllers", "SDL");

                // Try to find a connected compatible controller
                for (int i = 0; i < numJoysticks; i++)
                {
                    if (SDL.SDL_IsGameController(i) == SDL.SDL_bool.SDL_TRUE)
                    {
                        controllerId = i;
                        log($"Found compatible game controller at index {i}", "SDL");

                        // Open the controller here, and keep it open
                        controller = SDL.SDL_GameControllerOpen(controllerId);
                        if (controller == IntPtr.Zero)
                        {
                            log($"Could not open controller! SDL Error: {SDL.SDL_GetError()}", "SDL_ERROR");
                            return;
                        }

                        // Optional: Log controller mapping
                        string mapping = SDL.SDL_GameControllerMapping(controller);
                        log($"Controller mapping: {mapping}", "SDL_DEBUG");

                        break;
                    }
                }

                if (controllerId == -1)
                {
                    log("No compatible game controllers found", "SDL");
                    return;
                }

                // Set up controller polling timer (poll @ 120Hz)
                log("Setting up controller polling timer", "SDL");
                controllerTimer = new DispatcherTimer();
                controllerTimer.Interval = TimeSpan.FromMilliseconds(8);
                controllerTimer.Tick += PollControllerInput;
                controllerTimer.Start();
                log("Controller polling timer started", "SDL");
            }
            catch (Exception ex)
            {
                log($"Error initializing SDL: {ex.Message}", "SDL_ERROR");
                log($"Stack trace: {ex.StackTrace}", "SDL_ERROR");
            }
        }

        private void CloseController()
        {
            if (controller != IntPtr.Zero)
            {
                SDL.SDL_GameControllerClose(controller);
                controller = IntPtr.Zero;
                log("Controller closed", "SDL");
            }
        }

        private void PollControllerInput(object sender, EventArgs e)
        {
            // Ensure that the controller is initialized and opened
            if (controller == IntPtr.Zero)
            {
                log("Controller not open, skipping polling", "SDL");
                return;
            }

            // Process SDL events (optional, but can be useful for other input events)
            SDL.SDL_Event sdlEvent;
            while (SDL.SDL_PollEvent(out sdlEvent) != 0)
            {
                log($"SDL event type: {sdlEvent.type}", "SDL_EVENT");

                // You can add handling for other event types if needed, such as controller device additions/removals
                if (sdlEvent.type == SDL.SDL_EventType.SDL_CONTROLLERDEVICEADDED)
                {
                    log($"Controller device added: {sdlEvent.cdevice.which}", "SDL_EVENT");
                }
                else if (sdlEvent.type == SDL.SDL_EventType.SDL_CONTROLLERDEVICEREMOVED)
                {
                    log($"Controller device removed: {sdlEvent.cdevice.which}", "SDL_EVENT");
                }
            }

            // Update the controller's state (fetch input states like button presses, axis movements, etc.)
            SDL.SDL_GameControllerUpdate();

            // Check if the A button is pressed (used for selection)
            bool aPressed = SDL.SDL_GameControllerGetButton(controller, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_A) == 1;

            // Check if the B button is pressed (used for hiding the overlay)
            bool bPressed = SDL.SDL_GameControllerGetButton(controller, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_B) == 1;

            // Check D-Pad buttons (Up, Down, Left, Right)
            bool dpadUp = SDL.SDL_GameControllerGetButton(controller, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_UP) == 1;
            bool dpadDown = SDL.SDL_GameControllerGetButton(controller, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_DOWN) == 1;
            bool dpadLeft = SDL.SDL_GameControllerGetButton(controller, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_LEFT) == 1;
            bool dpadRight = SDL.SDL_GameControllerGetButton(controller, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_RIGHT) == 1;

            // Check the left analog stick axis values
            short leftX = SDL.SDL_GameControllerGetAxis(controller, SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_LEFTX);
            short leftY = SDL.SDL_GameControllerGetAxis(controller, SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_LEFTY);

            // Log raw input values for debugging purposes
            log($"Raw input - A:{aPressed} B:{bPressed} DPadUDLR:({dpadUp},{dpadDown},{dpadLeft},{dpadRight}) LeftStick:({leftX},{leftY})", "SDL_RAW");

            // Define a deadzone for the analog sticks (about 30% of max value)
            const short DEADZONE = 10000;

            // Combine D-Pad and Left Stick for movement detection
            bool moveUp = dpadUp || leftY < -DEADZONE;
            bool moveDown = dpadDown || leftY > DEADZONE;
            bool moveLeft = dpadLeft || leftX < -DEADZONE;
            bool moveRight = dpadRight || leftX > DEADZONE;

            // Log controller inputs when they happen (if any input is detected)
            if (moveUp || moveDown || moveLeft || moveRight || aPressed)
            {
                log($"Controller input: Up:{moveUp} Down:{moveDown} Left:{moveLeft} Right:{moveRight} A:{aPressed} B:{bPressed}", "SDL_INPUT");
            }

            if (!moveUp) lastUpTime = DateTime.MinValue;
            if (!moveDown) lastDownTime = DateTime.MinValue;
            if (!moveLeft) lastLeftTime = DateTime.MinValue;
            if (!moveRight) lastRightTime = DateTime.MinValue;


            // Debounce the navigation actions
            bool canMoveUp = DateTime.Now - lastUpTime > TimeSpan.FromMilliseconds(DEBOUNCE_THRESHOLD);
            bool canMoveDown = DateTime.Now - lastDownTime > TimeSpan.FromMilliseconds(DEBOUNCE_THRESHOLD);
            bool canMoveLeft = DateTime.Now - lastLeftTime > TimeSpan.FromMilliseconds(DEBOUNCE_THRESHOLD);
            bool canMoveRight = DateTime.Now - lastRightTime > TimeSpan.FromMilliseconds(DEBOUNCE_THRESHOLD);

            // Handle navigation based on the input
            Dispatcher.Invoke(() =>
            {

                // Log controller inputs when they happen (if any input is detected)
                if (moveUp && canMoveUp)
                {
                    log("Navigating UP", "SDL_NAV");
                    FocusPreviousElement();
                    lastUpTime = DateTime.Now; // Update the time of the last navigation event
                }

                if (moveDown && canMoveDown)
                {
                    log("Navigating DOWN", "SDL_NAV");
                    FocusNextElement();
                    lastDownTime = DateTime.Now; // Update the time of the last navigation event
                }

                if (moveLeft && canMoveLeft)
                {
                    log("Navigating LEFT", "SDL_NAV");
                    FocusLeftElement();
                    lastLeftTime = DateTime.Now; // Update the time of the last navigation event
                }

                if (moveRight && canMoveRight)
                {
                    log("Navigating RIGHT", "SDL_NAV");
                    FocusRightElement();
                    lastRightTime = DateTime.Now; // Update the time of the last navigation event
                }

                // Handle the A button for selection
                if (aPressed)
                {
                    log("Button A pressed - clicking focused element", "SDL_NAV");
                    ClickFocusedElement();
                }
                // Handle the B button for exit
                if (bPressed)
                {
                    log("Button B pressed - Hiding overlay", "SDL_NAV");
                    this.Hide();
                }
            });
        }

        private void FocusNextElement()
        {
            UIElement focusedElement = Keyboard.FocusedElement as UIElement;
            if (focusedElement != null)
            {
                log($"Moving focus from {focusedElement.GetType().Name} to next element", "SDL_NAV");
                focusedElement.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                log($"Focus now on: {(Keyboard.FocusedElement as FrameworkElement)?.Name ?? "unknown"}", "SDL_NAV");
            }
            else
            {
                log("No element currently has focus for next navigation", "SDL_NAV");
            }
        }

        private void FocusPreviousElement()
        {
            UIElement focusedElement = Keyboard.FocusedElement as UIElement;
            if (focusedElement != null)
            {
                log($"Moving focus from {focusedElement.GetType().Name} to previous element", "SDL_NAV");
                focusedElement.MoveFocus(new TraversalRequest(FocusNavigationDirection.Previous));
                log($"Focus now on: {(Keyboard.FocusedElement as FrameworkElement)?.Name ?? "unknown"}", "SDL_NAV");
            }
            else
            {
                log("No element currently has focus for previous navigation", "SDL_NAV");
            }
        }

        private void FocusLeftElement()
        {
            UIElement focusedElement = Keyboard.FocusedElement as UIElement;
            if (focusedElement != null)
            {
                log($"Moving focus from {focusedElement.GetType().Name} to left element", "SDL_NAV");
                focusedElement.MoveFocus(new TraversalRequest(FocusNavigationDirection.Left));
                log($"Focus now on: {(Keyboard.FocusedElement as FrameworkElement)?.Name ?? "unknown"}", "SDL_NAV");
            }
            else
            {
                log("No element currently has focus for left navigation", "SDL_NAV");
            }
        }

        private void FocusRightElement()
        {
            UIElement focusedElement = Keyboard.FocusedElement as UIElement;
            if (focusedElement != null)
            {
                log($"Moving focus from {focusedElement.GetType().Name} to right element", "SDL_NAV");
                focusedElement.MoveFocus(new TraversalRequest(FocusNavigationDirection.Right));
                log($"Focus now on: {(Keyboard.FocusedElement as FrameworkElement)?.Name ?? "unknown"}", "SDL_NAV");
            }
            else
            {
                log("No element currently has focus for right navigation", "SDL_NAV");
            }
        }

        private void ClickFocusedElement()
        {
            if (Keyboard.FocusedElement is System.Windows.Controls.Button button)
            {
                log($"Clicking button: {button.Name}", "SDL_NAV");
                // Create a click routed event
                RoutedEventArgs args = new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent);

                // Raise the event on the button
                button.RaiseEvent(args);
            }
            else
            {
                log($"Focused element is not a button: {Keyboard.FocusedElement?.GetType().Name ?? "null"}", "SDL_NAV");
            }
        }
        #endregion

        // Win32 API Declarations
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        private const int SW_RESTORE = 9;

        // Event to request showing Playnite (to be handled by the plugin)
        public event Action OnShowPlayniteRequested;
    }
}