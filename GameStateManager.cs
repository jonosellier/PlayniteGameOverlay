using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace PlayniteGameOverlay
{
    public class GameStateManager
    {
        private readonly Logger _logger;

        public GameStateManager(Logger logger)
        {
            _logger = logger;
        }

        public Process FindProcessById(int processId)
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

        public void ReturnToGame(GameOverlayData gameData)
        {
            if (gameData != null)
            {
                var proc = FindProcessById(gameData.ProcessId);
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
        }

        public async void CloseGame(GameOverlayData gameData, CloseBehavior behavior = CloseBehavior.CloseAndEnd)
        {
            if (gameData != null)
            {
                var proc = FindProcessById(gameData.ProcessId);
                if (proc == null)
                {
                    return;
                }

                var success = proc.CloseMainWindow();
                switch (behavior)
                {
                    case CloseBehavior.CloseAndEnd:
                        if (!success)
                        {
                            proc.Kill(); // Forcefully kill the process if closing the main window fails
                        }
                        for (int i = 0; i < 30 && !proc.HasExited; i++)
                        {
                            await Task.Delay(100); // Wait for up to 3 seconds for the process to exit gracefully
                        }
                        if (!proc.HasExited)
                        {
                            _logger.Log($"Process {proc.ProcessName} did not exit gracefully after 3 seconds, killing it forcefully.", "WARNING");
                            proc.Kill(); // Forcefully kill the process if it hasn't exited
                        }
                        proc.Close();
                        break;
                    case CloseBehavior.CloseWindow:
                        if (!success)
                        {
                            proc.Kill(); // Forcefully kill the process if closing the main window fails
                        }
                        proc.Close();
                        break;
                    case CloseBehavior.EndTask:
                        proc.Kill(); // Forcefully kill the process regardless
                        proc.Close();
                        break;
                    default:
                        _logger.Log($"Unknown close behavior: {behavior}", "ERROR");
                        break;
                }

            }
        }

        public string FormatPlaytime(TimeSpan playtime)
        {
            if (playtime.TotalMinutes < 120)
                return $"{(int)playtime.TotalMinutes} mins.";

            return $"{(int)playtime.TotalHours} hrs.";
        }

        public string GetFormattedSessionTime(DateTime gameStartTime)
        {
            var playTime = DateTime.Now - gameStartTime;
            return $"{playTime.Hours}:{playTime.Minutes:D2}:{playTime.Seconds:D2}";
        }

        public AchievementInfo GetMostRecentAchievement(GameOverlayData gameData)
        {
            if (gameData?.Achievements == null || gameData.Achievements.Count == 0)
                return null;

            return new AchievementInfo
            {
                MostRecent = gameData.Achievements.FindAll(a => a.IsUnlocked).OrderByDescending(a => a.UnlockDate).FirstOrDefault(),
                TotalCount = gameData.Achievements.Count,
                UnlockedCount = gameData.Achievements.Count(a => a.IsUnlocked)
            };
        }
    }

    public class AchievementInfo
    {
        public AchievementData MostRecent { get; set; }
        public int TotalCount { get; set; }
        public int UnlockedCount { get; set; }
    }
}