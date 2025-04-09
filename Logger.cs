using System.Diagnostics;

namespace PlayniteGameOverlay
{
    public class Logger
    {
        private readonly bool _debugMode;

        public Logger(bool debugMode)
        {
            _debugMode = debugMode;
        }

        public void Log(string msg, string tag = "DEBUG")
        {
            if (_debugMode)
            {
                Debug.WriteLine($"GameOverlay[{tag}]: {msg}");
            }
        }
    }
}