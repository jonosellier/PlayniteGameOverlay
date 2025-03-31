using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;

namespace PlayniteGameOverlay
{
    public class PlayniteGameOverlay : GenericPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private const bool IS_DEBUG = true;

        private OverlayWindow overlayWindow;
        private IPlayniteAPI playniteAPI;
        private GlobalKeyboardHook keyboardHook;

        public override Guid Id { get; } = Guid.Parse("fc75626e-ec69-4287-972a-b86298555ebb");

        DateTime gameStarted;

        public PlayniteGameOverlay(IPlayniteAPI api) : base(api)
        {
            playniteAPI = api;
            Properties = new GenericPluginProperties { HasSettings = false };
        }

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            logger.Info("Starting Overlay Extension...");

            // Initialize overlay window
            overlayWindow = new OverlayWindow();
            overlayWindow.Hide();

            // Set up show Playnite handler
            overlayWindow.OnShowPlayniteRequested += ShowPlaynite;

            // Initialize global keyboard hook
            keyboardHook = new GlobalKeyboardHook();
            keyboardHook.KeyPressed += OnKeyPressed;
        }

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            logger.Info("Stopping Overlay...");

            // Cleanup resources
            overlayWindow?.Close();
            keyboardHook?.Dispose();
        }

        private void OnKeyPressed(Keys key, bool altPressed)
        {
            // Alt + ` (backtick) to toggle overlay
            if (altPressed && key == Keys.Oem3)
            {
                var runningGame = playniteAPI.Database.Games.FirstOrDefault(g => g.IsRunning);

                if (runningGame != null)
                {
                    if (overlayWindow.IsVisible)
                        overlayWindow.Hide();
                    else
                        ShowGameOverlay(runningGame);
                }
                else
                {
                    ShowPlaynite();
                }
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
                overlayWindow.UpdateGameOverlay(gameOverlayData);
            }
            catch (Exception ex)
            {
                logger.Error($"Error preparing game overlay data: {ex}");
            }
        }

        public override void OnGameStopped(OnGameStoppedEventArgs args)
        {
            overlayWindow.UpdateGameOverlay(null);
        }

        private void ShowGameOverlay(Game game)
        {
            if (game == null) return;

            var gameOverlayData = CreateGameOverlayData(game, FindRunningGameProcess(game)?.Id, gameStarted);
            overlayWindow.UpdateGameOverlay(gameOverlayData);
            overlayWindow.ShowOverlay();
        }

        private GameOverlayData CreateGameOverlayData(Game game, int? processId, DateTime StartTime)
        {
            if (game == null) return null;

            return new GameOverlayData
            {
                GameName = game.Name,
                ProcessId = processId ?? -1,
                GameStartTime = StartTime,
                Playtime = TimeSpan.FromSeconds(game.Playtime),
                CoverImagePath = GetFullCoverImagePath(game)
            };
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
            if (IS_DEBUG)
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

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_RESTORE = 9;
    }
}