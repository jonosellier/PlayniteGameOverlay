﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Windows.Forms;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using SDL2;
using System.Windows.Threading;
using System.ComponentModel;
using IWshRuntimeLibrary;

namespace PlayniteGameOverlay
{
    public class PlayniteGameOverlay : GenericPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private OverlaySettings settings;

        private GameOverlayData GameOverlayData;

        public List<ButtonItem> ButtonItems = new List<ButtonItem>();

        private OverlayWindow overlayWindow;
        private IPlayniteAPI playniteAPI;
        private GlobalKeyboardHook keyboardHook;

        public override Guid Id { get; } = Guid.Parse("fc75626e-ec69-4287-972a-b86298555ebb");

        DateTime gameStarted;

        // SuccessStory integration
        private bool isSuccessStoryAvailable = false;
        private Guid successStoryId = Guid.Parse("cebe6d32-8c46-4459-b993-5a5189d60788"); // SuccessStory plugin ID

        private void CheckSuccessStoryAvailability()
        {
            try
            {
                var plugins = playniteAPI.Addons.Plugins;
                isSuccessStoryAvailable = plugins.Any(p => p.Id == successStoryId);

                if (isSuccessStoryAvailable)
                {
                    log("SuccessStory plugin detected, achievement integration enabled");
                }
                else
                {
                    log("SuccessStory plugin not found, achievement integration disabled");
                }
            }
            catch (Exception ex)
            {
                log($"Error checking for SuccessStory plugin: {ex.Message}", "ERROR");
                isSuccessStoryAvailable = false;
            }
        }




        public PlayniteGameOverlay(IPlayniteAPI api) : base(api)
        {
            playniteAPI = api;
            Properties = new GenericPluginProperties { HasSettings = true };
        }

        public List<ButtonItem> ReadShortcutsFromDirectory(string directoryPath)
        {
            var buttonItems = new List<ButtonItem>();

            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
                return buttonItems;
            }

            var shortcutFiles = Directory.GetFiles(directoryPath, "*.lnk");

            foreach (var shortcutPath in shortcutFiles)
            {
                try
                {
                    var shell = new WshShell();
                    var shortcut = (IWshShortcut)shell.CreateShortcut(shortcutPath);

                    var button = new ButtonItem
                    {
                        Title = Path.GetFileNameWithoutExtension(shortcutPath),
                        ActionType = ButtonAction.Shortcut,
                        Path = Path.GetFullPath(shortcutPath),
                        IconPath = @"C:\Users\Jono\Downloads\pngegg.png"
                    };

                    buttonItems.Add(button);
                }
                catch (Exception ex)
                {
                    // Handle exceptions as needed (e.g. log or skip)
                    log($"Failed to process shortcut: {shortcutPath}, Error: {ex.Message}");
                }
            }

            foreach(var button in buttonItems)
            {
                log("---- ButtonItem ----");
                log($"Title:      {button.Title}");
                log($"ActionType: {button.ActionType}");
                log($"Path:       {button.Path}");
                log($"IconPath:   {button.IconPath}");
            }

            return buttonItems;
        }

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            logger.Info("Starting Overlay Extension...");

            // Check if SuccessStory is installed
            CheckSuccessStoryAvailability();

            ButtonItems = ReadShortcutsFromDirectory(Path.Combine(
                            playniteAPI.Paths.ExtensionsDataPath, Id.ToString(), "Shortcuts"));

            // Initialize overlay window
            overlayWindow = new OverlayWindow(Settings, ButtonItems.ToArray());
            overlayWindow.Hide();

            // Set up show Playnite handler
            overlayWindow.OnShowPlayniteRequested += ShowPlaynite;

            // Initialize global keyboard hook
            keyboardHook = new GlobalKeyboardHook();
            keyboardHook.KeyPressed += OnKeyPressed;
            InitializeController();
            if (controllerTimer != null)
            {
                controllerTimer.Stop();
            }
        }

        public void ReloadOverlay(OverlaySettings settings)
        {
            overlayWindow.Close(); //close old window
            overlayWindow = new OverlayWindow(settings != null ? settings : Settings, ButtonItems.ToArray()); //open new one
            overlayWindow.Hide();
            if(GameOverlayData != null)
            {
                overlayWindow.UpdateGameOverlay(GameOverlayData);
            }

            // Set up show Playnite handler again
            overlayWindow.OnShowPlayniteRequested += ShowPlaynite;
        }

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            logger.Info("Stopping Overlay...");

            // Cleanup resources
            overlayWindow?.Close();
            keyboardHook?.Dispose();
            // Clean up SDL
            if (sdlInitialized)
            {
                SDL.SDL_Quit();
                sdlInitialized = false;
            }
            CloseController();
        }

        private void OnKeyPressed(Keys key, bool altPressed)
        {
            // Alt + ` (backtick) to toggle overlay
            if (altPressed && key == Keys.Oem3)
            {
                var runningGame = playniteAPI.Database.Games.FirstOrDefault(g => g.IsRunning);

                //if (runningGame != null)
                //{
                    if (overlayWindow.IsVisible)
                        overlayWindow.Hide();
                    else
                        ShowGameOverlay(runningGame);
                //}
                //else
                //{
                //    ShowPlaynite();
                //}
            }

            // Escape to hide overlay
            if (key == Keys.Escape && overlayWindow.IsVisible)
            {
                overlayWindow.Hide();
            }
        }

        public override void OnGameStarted(OnGameStartedEventArgs args)
        {
            try
            {
                gameStarted = DateTime.Now;
                var gameOverlayData = CreateGameOverlayData(args.Game, args.StartedProcessId, gameStarted);
                GameOverlayData = gameOverlayData;
                overlayWindow.UpdateGameOverlay(gameOverlayData);
                if (controllerTimer != null)
                {
                    controllerTimer.Start();
                }
            }
            catch (Exception ex)
            {
                logger.Error($"Error preparing game overlay data: {ex}");
            }
        }

        public override void OnGameStopped(OnGameStoppedEventArgs args)
        {
            overlayWindow.UpdateGameOverlay(null);
            GameOverlayData = null;
            if (controllerTimer != null)
            {
                controllerTimer.Stop();
            }
        }

        private void ShowGameOverlay(Game game)
        {
            //if (game == null) return;

            var gameOverlayData = CreateGameOverlayData(game, FindRunningGameProcess(game)?.Id, gameStarted);
            overlayWindow.UpdateGameOverlay(gameOverlayData);
            overlayWindow.ShowOverlay();
        }

        private GameOverlayData CreateGameOverlayData(Game game, int? processId, DateTime StartTime)
        {
            if (game == null) return null;

            var achievements = GetGameAchievements(game);

            return new GameOverlayData
            {
                GameName = game.Name,
                ProcessId = processId ?? -1,
                GameStartTime = StartTime,
                Playtime = TimeSpan.FromSeconds(game.Playtime),
                CoverImagePath = GetFullCoverImagePath(game),
                Achievements = achievements
            };
        }

        private List<AchievementData> GetGameAchievements(Game game)
        {
            log($"Retrieving achievements for game {game.Name} (ID: {game.Id}, SuccessStory Enabled: {isSuccessStoryAvailable})");
            var achievements = new List<AchievementData>();

            if (!isSuccessStoryAvailable || game == null)
                return achievements;

            try
            {
                // Access SuccessStory's data through Playnite extension API
                var successStory = playniteAPI.Addons.Plugins.FirstOrDefault(p => p.Id == successStoryId);
                if (successStory != null)
                {
                    try
                    {
                        // Find SuccessStory's data directory
                        string successStoryDir = Path.Combine(
                            playniteAPI.Paths.ExtensionsDataPath, successStoryId.ToString(), "SuccessStory");

                        if (Directory.Exists(successStoryDir))
                        {
                            // Look for a file containing achievements for this game
                            string achievementsFile = Path.Combine(successStoryDir, $"{game.Id}.json");
                            if (System.IO.File.Exists(achievementsFile))
                            {
                                string achievementsJson = System.IO.File.ReadAllText(achievementsFile);
                                achievements = ParseSuccessStoryData(achievementsJson);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Error($"Failed to get achievements from file: {ex.Message}");
                    }
                }

                logger.Info($"Retrieved {achievements.Count} achievements for game {game.Name}");
            }
            catch (Exception ex)
            {
                logger.Error($"Error retrieving achievements from SuccessStory: {ex.Message}");
            }

            return achievements;
        }

        private string GetFullCoverImagePath(Game game)
        {
            if (string.IsNullOrEmpty(game.CoverImage))
                return null;

            try
            {
                return playniteAPI.Database.GetFullFilePath(game.CoverImage);
            }
            catch (Exception ex)
            {
                logger.Error($"Error getting cover image path: {ex}");
                return null;
            }
        }

        private Process FindRunningGameProcess(Game game)
        {
            if (game == null) return null;

            try
            {
                // Get all executable files in the game installation directory and subdirectories
                var gameExecutables = new List<string>();
                try
                {
                    gameExecutables = Directory.GetFiles(game.InstallDirectory, "*.exe", SearchOption.AllDirectories)
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
                        additionalNames.Add(exe.Replace("-", " "));
                        additionalNames.Add(exe.Replace("_", " "));
                        additionalNames.Add(exe.Replace(" ", " "));
                    }
                    gameExecutables.AddRange(additionalNames);

                    log($"Found {gameExecutables.Count} potential game executables in {game.InstallDirectory}");
                }
                catch (Exception ex)
                {
                    logger.Error($"Error scanning game directory: {ex.Message}");
                }

                // Get the game name words for title comparison
                string gameName = game.Name;
                string[] gameNameWords = gameName.ToLower().Split(new char[] { ' ', '-', '_', ':', '.', '(', ')', '[', ']' },
                    StringSplitOptions.RemoveEmptyEntries);

                log($"Game name for matching: {gameName}, split into {gameNameWords.Length} words");

                // Get all processes with main window
                Process[] allProcesses = Process.GetProcesses()
                    .Where(p =>
                    {
                        try
                        {
                            return p.MainWindowHandle != IntPtr.Zero && !string.IsNullOrEmpty(p.MainWindowTitle);
                        }
                        catch
                        {
                            return false;
                        }
                    })
                    .ToArray();

                var candidates = new List<Process>();
                var nameMatchCandidates = new List<Process>();
                var titleMatchCandidates = new List<(Process Process, int MatchCount)>();
                var inaccessibleCandidates = new List<Process>();

                DateTime gameStartTime = DateTime.Now; // Use current time as fallback

                foreach (var p in allProcesses)
                {
                    try
                    {
                        // Check memory usage first
                        if (!p.HasExited && p.WorkingSet64 > 100 * 1024 * 1024)
                        {
                            // Check if process name matches any executable in the game folder
                            bool nameMatches = gameExecutables.Any(exe =>
                                string.Equals(exe, p.ProcessName, StringComparison.OrdinalIgnoreCase));

                            // Check window title for matches with game name
                            int titleMatchScore = 0;
                            if (p.MainWindowHandle != IntPtr.Zero && !string.IsNullOrEmpty(p.MainWindowTitle))
                            {
                                string[] windowTitleWords = p.MainWindowTitle.ToLower().Split(new char[] { ' ', '-', '_', ':', '.', '(', ')', '[', ']' },
                                    StringSplitOptions.RemoveEmptyEntries);

                                // Count how many words from the game name appear in the window title
                                titleMatchScore = gameNameWords.Count(gameWord =>
                                    windowTitleWords.Any(titleWord => titleWord.Contains(gameWord) || gameWord.Contains(titleWord)));
                            }

                            try
                            {
                                // Try to access the module info
                                var modulePath = p.MainModule.FileName;
                                if (modulePath.IndexOf(game.InstallDirectory, StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    candidates.Add(p);
                                }
                                else if (nameMatches)
                                {
                                    // Path doesn't match but name does - this is a good candidate
                                    nameMatchCandidates.Add(p);
                                }
                                else if (titleMatchScore > 0)
                                {
                                    // Window title matches game name
                                    titleMatchCandidates.Add((p, titleMatchScore));
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
                                else if (titleMatchScore > 0)
                                {
                                    // Window title matches game name
                                    titleMatchCandidates.Add((p, titleMatchScore));
                                }
                                else
                                {
                                    // No name match, but memory usage matches
                                    inaccessibleCandidates.Add(p);
                                }
                            }
                        }
                    }
                    catch { /* Skip processes we can't access at all */ }
                }

                // Priority order: direct path match, window title match, process name match, then best guess
                if (candidates.Count > 0)
                {
                    var bestMatch = candidates.OrderByDescending(p => p.WorkingSet64).First();
                    log($"Found process with matching path: {bestMatch.ProcessName} (ID: {bestMatch.Id})");
                    return bestMatch;
                }

                if (titleMatchCandidates.Count > 0)
                {
                    // Get the process with the highest title match score
                    var bestMatch = titleMatchCandidates.OrderByDescending(t => t.MatchCount)
                                                        .ThenByDescending(t => t.Process.WorkingSet64)
                                                        .First().Process;
                    log($"Found process with matching window title: {bestMatch.ProcessName} (ID: {bestMatch.Id}, Title: {bestMatch.MainWindowTitle})");
                    return bestMatch;
                }

                if (nameMatchCandidates.Count > 0)
                {
                    var bestMatch = nameMatchCandidates.OrderByDescending(p => p.WorkingSet64).First();
                    log($"Found process with matching name: {bestMatch.ProcessName} (ID: {bestMatch.Id})");
                    return bestMatch;
                }

                if (inaccessibleCandidates.Count > 0)
                {
                    var bestGuess = inaccessibleCandidates.OrderByDescending(p => p.WorkingSet64).First();
                    log($"Using best guess process: {bestGuess.ProcessName} (ID: {bestGuess.Id})");
                    return bestGuess;
                }
            }
            catch (Exception ex)
            {
                log($"Error finding game process: {ex.Message}", "ERROR");
            }

            return null;
        }

        private void log(string msg, string tag = "DEBUG")
        {
            if (Settings.DebugMode)
            {
                Debug.WriteLine("GameOverlay[" + tag + "]: " + msg);
            }
            logger.Debug(msg);
        }


        private void ShowPlaynite()
        {
            try
            {
                Process[] processes = Process.GetProcessesByName("Playnite.DesktopApp");
                if (processes.Length == 0)
                {
                    processes = Process.GetProcessesByName("Playnite.FullscreenApp");
                }

                if (processes.Length > 0)
                {
                    // Use the Win32 API calls you already have to activate the window
                    Process proc = processes[0];
                    if (IsIconic(proc.MainWindowHandle))
                    {
                        ShowWindow(proc.MainWindowHandle, SW_RESTORE);
                    }
                    SetForegroundWindow(proc.MainWindowHandle);
                    log("Activated Playnite process: " + proc.ProcessName);
                }
                else
                {
                    log("Could not find any Playnite process to activate", "WARNING");
                }
            }
            catch (Exception ex)
            {
                logger.Error($"Error showing Playnite: {ex}");
            }
        }

        // Method to parse the JSON file and convert to your AchievementData format
        private List<AchievementData> ParseSuccessStoryData(string jsonData)
        {
            var achievements = new List<AchievementData>();

            try
            {
                
                var successStoryData = JsonSerializer.Deserialize<SuccessStoryData>(jsonData);

                if (successStoryData?.Items == null)
                {
                    logger.Error("Failed to parse SuccessStory data: Items is null");
                    return achievements;
                }

                foreach (var item in successStoryData.Items)
                {
                    bool isUnlocked = !string.IsNullOrEmpty(item.DateUnlockedStr);
                    DateTime? unlockDate = null;

                    if (isUnlocked)
                    {
                        // Try to parse the date
                        if (DateTime.TryParse(item.DateUnlockedStr, out DateTime parsedDate))
                        {
                            unlockDate = parsedDate;
                        }
                    }

                    achievements.Add(new AchievementData
                    {
                        Name = item.Name,
                        Description = item.Description,
                        IsUnlocked = isUnlocked,
                        UnlockDate = unlockDate,
                        IconUrl = isUnlocked ? item.UrlUnlocked : item.UrlLocked
                    });
                }

                log($"Successfully parsed {achievements.Count} achievements from SuccessStory");
            }
            catch (Exception ex)
            {
                log($"Error parsing SuccessStory file: {ex.Message}", "ERROR");
            }

            return achievements;
        }

        #region SDL2
        private IntPtr controller = IntPtr.Zero;

        private bool sdlInitialized = false;
        private int controllerId = -1;
        private DispatcherTimer controllerTimer;

        private void InitializeController()
        {
            try
            {
                log("Initializing SDL controller support", "SDL_GLOBAL");

                // Initialize SDL with game controller support
                if (SDL.SDL_Init(SDL.SDL_INIT_GAMECONTROLLER) < 0)
                {
                    string error = SDL.SDL_GetError();
                    log($"SDL_GLOBAL could not initialize! SDL Error: {error}", "SDL_GLOBAL_ERROR");
                    return;
                }

                sdlInitialized = true;
                log("SDL_GLOBAL initialized successfully", "SDL_GLOBAL");

                // Look for connected controllers
                int numJoysticks = SDL.SDL_NumJoysticks();
                log($"Found {numJoysticks} joysticks/controllers", "SDL_GLOBAL");

                // Try to find a connected compatible controller
                for (int i = 0; i < numJoysticks; i++)
                {
                    if (SDL.SDL_IsGameController(i) == SDL.SDL_bool.SDL_TRUE)
                    {
                        controllerId = i;
                        log($"Found compatible game controller at index {i}", "SDL_GLOBAL");

                        // Open the controller here, and keep it open
                        controller = SDL.SDL_GameControllerOpen(controllerId);
                        if (controller == IntPtr.Zero)
                        {
                            log($"Could not open controller! SDL Error: {SDL.SDL_GetError()}", "SDL_GLOBAL_ERROR");
                            return;
                        }

                        // Optional: Log controller mapping
                        string mapping = SDL.SDL_GameControllerMapping(controller);
                        log($"Controller mapping: {mapping}", "SDL_GLOBAL_DEBUG");

                        break;
                    }
                }

                if (controllerId == -1)
                {
                    log("No compatible game controllers found", "SDL_GLOBAL");
                    return;
                }

                // Set up controller polling timer (poll @ 120Hz)
                log("Setting up controller polling timer", "SDL_GLOBAL");
                controllerTimer = new DispatcherTimer();
                controllerTimer.Interval = TimeSpan.FromMilliseconds(8);
                controllerTimer.Tick += PollControllerInput;
                controllerTimer.Start();
                log("Controller polling timer started", "SDL_GLOBAL");
            }
            catch (Exception ex)
            {
                log($"Error initializing SDL: {ex.Message}", "SDL_GLOBAL_ERROR");
                log($"Stack trace: {ex.StackTrace}", "SDL_GLOBAL_ERROR");
            }
        }

        private void CloseController()
        {
            if (controller != IntPtr.Zero)
            {
                SDL.SDL_GameControllerClose(controller);
                controller = IntPtr.Zero;
                log("Controller closed", "SDL_GLOBAL");
            }
        }

        private void PollControllerInput(object sender, EventArgs e)
        {
            // Ensure that the controller is initialized and opened
            if (controller == IntPtr.Zero)
            {
                log("Controller not open, skipping polling", "SDL_GLOBAL");
                return;
            }

            // Process SDL events (optional, but can be useful for other input events)
            SDL.SDL_Event sdlEvent;
            while (SDL.SDL_PollEvent(out sdlEvent) != 0)
            {
                log($"SDL_GLOBAL event type: {sdlEvent.type}", "SDL_GLOBAL_EVENT");

                // You can add handling for other event types if needed, such as controller device additions/removals
                if (sdlEvent.type == SDL.SDL_EventType.SDL_CONTROLLERDEVICEADDED)
                {
                    log($"Controller device added: {sdlEvent.cdevice.which}", "SDL_GLOBAL_EVENT");
                }
                else if (sdlEvent.type == SDL.SDL_EventType.SDL_CONTROLLERDEVICEREMOVED)
                {
                    log($"Controller device removed: {sdlEvent.cdevice.which}", "SDL_GLOBAL_EVENT");
                }
            }

            // Update the controller's state (fetch input states like button presses, axis movements, etc.)
            SDL.SDL_GameControllerUpdate();

            // Check if the A button is pressed (used for selection)
            bool startPressed = SDL.SDL_GameControllerGetButton(controller, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_START) == 1;

            // Check if the B button is pressed (used for hiding the overlay)
            bool backPressed = SDL.SDL_GameControllerGetButton(controller, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_BACK) == 1;

            bool guidePressed = SDL.SDL_GameControllerGetButton(controller, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_GUIDE) == 1;

            if (
                (Settings.ControllerShortcut == ControllerShortcut.StartBack && startPressed && backPressed)
                || (Settings.ControllerShortcut == ControllerShortcut.Guide && guidePressed)
                )
            {
                var runningGame = playniteAPI.Database.Games.FirstOrDefault(g => g.IsRunning);
                if (runningGame != null)
                {
                    if (overlayWindow.IsVisible)
                        overlayWindow.Hide();
                    else
                        ShowGameOverlay(runningGame);
                } else
                {
                    log("No running game found to show overlay", "WARNING");
                }
            }
        }

        #endregion


        #region Settings
        public override ISettings GetSettings(bool firstRunSettings)
        {
            if (settings == null)
            {
                settings = new OverlaySettings(this);
            }

            return settings;
        }

        public override System.Windows.Controls.UserControl GetSettingsView(bool firstRunSettings)
        {
            return new OverlaySettingsView();
        }

        // You might also want to add a public property to easily access settings
        public OverlaySettings Settings
        {
            get
            {
                if (settings == null)
                {
                    settings = (OverlaySettings)GetSettings(false);
                }

                return settings;
            }
        }
        #endregion

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_RESTORE = 9;
    }

    public class AchievementData
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public bool IsUnlocked { get; set; }
        public DateTime? UnlockDate { get; set; }
        public string IconUrl { get; set; }
    }

    // Update your GameOverlayData class to include achievements
    public class GameOverlayData
    {
        public string GameName { get; set; }
        public int ProcessId { get; set; }
        public DateTime GameStartTime { get; set; }
        public TimeSpan Playtime { get; set; }
        public string CoverImagePath { get; set; }
        public List<AchievementData> Achievements { get; set; }
    }

    // Define classes that match the JSON structure
    public class SuccessStoryData
    {
        [JsonPropertyName("Items")]
        public List<SuccessStoryAchievement> Items { get; set; }

        [JsonPropertyName("Name")]
        public string Name { get; set; }
    }

    public class SuccessStoryAchievement
    {
        [JsonPropertyName("Name")]
        public string Name { get; set; }

        [JsonPropertyName("Description")]
        public string Description { get; set; }

        [JsonPropertyName("DateUnlocked")]
        public string DateUnlockedStr { get; set; }

        [JsonPropertyName("UrlUnlocked")]
        public string UrlUnlocked { get; set; }

        [JsonPropertyName("UrlLocked")]
        public string UrlLocked { get; set; }
    }

    public class OverlaySettings : ObservableObject, ISettings
    {
        private readonly PlayniteGameOverlay plugin;

        // Using properties with setters that notify of changes
        private ControllerShortcut _controllerShortcut = ControllerShortcut.StartBack;
        private bool _debugMode = false;
        private AspectRatio _aspectRatio = AspectRatio.Portrait;

        // Toggle properties for showing/hiding buttons
        private bool _showRecordGameplay = true;
        private bool _showRecordRecent = true;
        private bool _showStreaming = true;
        private bool _showPerformanceOverlay = true;
        private bool _showScreenshotGallery = true;
        private bool _showWebBrowser = true;

        // Shortcut and path properties
        private string _recordGameplayShortcut = "";
        private string _recordRecentShortcut = "";
        private string _streamingShortcut = "";
        private string _performanceOverlayShortcut = "";
        private string _screenshotGalleryPath = "";
        private string _webBrowserPath = "";

        public ControllerShortcut ControllerShortcut
        {
            get => _controllerShortcut;
            set => SetValue(ref _controllerShortcut, value);
        }

        public bool DebugMode
        {
            get => _debugMode;
            set => SetValue(ref _debugMode, value);
        }

        public AspectRatio AspectRatio
        {
            get => _aspectRatio;
            set => SetValue(ref _aspectRatio, value);
        }

        // Properties for toggle options
        public bool ShowRecordGameplay
        {
            get => _showRecordGameplay;
            set => SetValue(ref _showRecordGameplay, value);
        }

        public bool ShowRecordRecent
        {
            get => _showRecordRecent;
            set => SetValue(ref _showRecordRecent, value);
        }

        public bool ShowStreaming
        {
            get => _showStreaming;
            set => SetValue(ref _showStreaming, value);
        }

        public bool ShowPerformanceOverlay
        {
            get => _showPerformanceOverlay;
            set => SetValue(ref _showPerformanceOverlay, value);
        }

        public bool ShowScreenshotGallery
        {
            get => _showScreenshotGallery;
            set => SetValue(ref _showScreenshotGallery, value);
        }

        public bool ShowWebBrowser
        {
            get => _showWebBrowser;
            set => SetValue(ref _showWebBrowser, value);
        }

        // Properties for shortcuts and paths
        public string RecordGameplayShortcut
        {
            get => _recordGameplayShortcut;
            set => SetValue(ref _recordGameplayShortcut, value);
        }

        public string RecordRecentShortcut
        {
            get => _recordRecentShortcut;
            set => SetValue(ref _recordRecentShortcut, value);
        }

        public string StreamingShortcut
        {
            get => _streamingShortcut;
            set => SetValue(ref _streamingShortcut, value);
        }

        public string PerformanceOverlayShortcut
        {
            get => _performanceOverlayShortcut;
            set => SetValue(ref _performanceOverlayShortcut, value);
        }

        public string ScreenshotGalleryPath
        {
            get => _screenshotGalleryPath;
            set => SetValue(ref _screenshotGalleryPath, value);
        }

        public string WebBrowserPath
        {
            get => _webBrowserPath;
            set => SetValue(ref _webBrowserPath, value);
        }

        // Backup values for cancel operation
        private ControllerShortcut _controllerShortcutBackup;
        private bool _debugModeBackup;
        private bool _showRecordGameplayBackup;
        private bool _showRecordRecentBackup;
        private bool _showStreamingBackup;
        private bool _showPerformanceOverlayBackup;
        private bool _showScreenshotGalleryBackup;
        private bool _showWebBrowserBackup;
        private string _recordGameplayShortcutBackup;
        private string _recordRecentShortcutBackup;
        private string _streamingShortcutBackup;
        private string _performanceOverlayShortcutBackup;
        private string _screenshotGalleryPathBackup;
        private string _webBrowserPathBackup;

        // Parameterless constructor needed for serialization
        public OverlaySettings()
        {
        }

        // Main constructor that loads saved settings
        public OverlaySettings(PlayniteGameOverlay plugin)
        {
            this.plugin = plugin;

            // Load saved settings
            var savedSettings = plugin.LoadPluginSettings<OverlaySettings>();
            if (savedSettings != null)
            {
                if (savedSettings.ControllerShortcut != null)
                    ControllerShortcut = savedSettings.ControllerShortcut;
                if (savedSettings.DebugMode != null)
                    DebugMode = savedSettings.DebugMode;
                if (savedSettings.AspectRatio != null)
                    AspectRatio = savedSettings.AspectRatio;

                // Load toggle settings
                if(savedSettings.ShowRecordGameplay != null) ShowRecordGameplay = savedSettings.ShowRecordGameplay;
                if(savedSettings.ShowRecordRecent != null) ShowRecordRecent = savedSettings.ShowRecordRecent;
                if(savedSettings.ShowStreaming != null) ShowStreaming = savedSettings.ShowStreaming;
                if(savedSettings.ShowPerformanceOverlay != null) ShowPerformanceOverlay = savedSettings.ShowPerformanceOverlay;
                if(savedSettings.ShowScreenshotGallery != null) ShowScreenshotGallery = savedSettings.ShowScreenshotGallery;
                if(savedSettings.ShowWebBrowser != null) ShowWebBrowser = savedSettings.ShowWebBrowser;

                // Load shortcut and path settings
                if (savedSettings.RecordGameplayShortcut != null) RecordGameplayShortcut = savedSettings.RecordGameplayShortcut;
                if(savedSettings.RecordRecentShortcut != null) RecordRecentShortcut = savedSettings.RecordRecentShortcut;
                if(savedSettings.StreamingShortcut != null) StreamingShortcut = savedSettings.StreamingShortcut;
                if(savedSettings.PerformanceOverlayShortcut != null) PerformanceOverlayShortcut = savedSettings.PerformanceOverlayShortcut;
                if(savedSettings.ScreenshotGalleryPath != null) ScreenshotGalleryPath = savedSettings.ScreenshotGalleryPath;
                if(savedSettings.WebBrowserPath != null) WebBrowserPath = savedSettings.WebBrowserPath;
            }
        }

        public void BeginEdit()
        {
            // Backup current values in case user cancels
            _controllerShortcutBackup = ControllerShortcut;
            _debugModeBackup = DebugMode;

            // Backup toggle values
            _showRecordGameplayBackup = ShowRecordGameplay;
            _showRecordRecentBackup = ShowRecordRecent;
            _showStreamingBackup = ShowStreaming;
            _showPerformanceOverlayBackup = ShowPerformanceOverlay;
            _showScreenshotGalleryBackup = ShowScreenshotGallery;
            _showWebBrowserBackup = ShowWebBrowser;

            // Backup shortcut and path values
            _recordGameplayShortcutBackup = RecordGameplayShortcut;
            _recordRecentShortcutBackup = RecordRecentShortcut;
            _streamingShortcutBackup = StreamingShortcut;
            _performanceOverlayShortcutBackup = PerformanceOverlayShortcut;
            _screenshotGalleryPathBackup = ScreenshotGalleryPath;
            _webBrowserPathBackup = WebBrowserPath;
        }

        public void CancelEdit()
        {
            // Restore from backup
            ControllerShortcut = _controllerShortcutBackup;
            DebugMode = _debugModeBackup;

            // Restore toggle backups
            ShowRecordGameplay = _showRecordGameplayBackup;
            ShowRecordRecent = _showRecordRecentBackup;
            ShowStreaming = _showStreamingBackup;
            ShowPerformanceOverlay = _showPerformanceOverlayBackup;
            ShowScreenshotGallery = _showScreenshotGalleryBackup;
            ShowWebBrowser = _showWebBrowserBackup;

            // Restore shortcut and path backups
            RecordGameplayShortcut = _recordGameplayShortcutBackup;
            RecordRecentShortcut = _recordRecentShortcutBackup;
            StreamingShortcut = _streamingShortcutBackup;
            PerformanceOverlayShortcut = _performanceOverlayShortcutBackup;
            ScreenshotGalleryPath = _screenshotGalleryPathBackup;
            WebBrowserPath = _webBrowserPathBackup;
        }

        public void EndEdit()
        {
            // Save settings to persistent storage
            plugin.SavePluginSettings(this);
            plugin.ReloadOverlay(this);
        }

        public bool VerifySettings(out List<string> errors)
        {
            errors = new List<string>();
            // Add any validation logic here if needed
            return true;
        }
    }
    public enum ControllerShortcut
    {
        [Description("View + Menu (Back + Start)")]
        StartBack,

        [Description("Xbox Button (Guide Button)")]
        Guide
    }

    public enum AspectRatio
    {
        Portrait,
        Landscape,
        Square
    }
}