using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Plugins;
using System;
using System.Windows.Forms;

namespace PlayniteGameOverlay
{
    public class PlayniteGameOverlay : GenericPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private PlayniteGameOverlaySettingsViewModel settings { get; set; }

        public override Guid Id { get; } = Guid.Parse("fc75626e-ec69-4287-972a-b86298555ebb");

        public PlayniteGameOverlay(IPlayniteAPI api) : base(api)
        {
            playniteAPI = api;
            Properties = new GenericPluginProperties { HasSettings = false };
        }

        private OverlayWindow overlayWindow;
        private IPlayniteAPI playniteAPI;
        private GlobalKeyboardHook keyboardHook;

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            logger.Info("Starting Overlay Extension...");

            overlayWindow = new OverlayWindow(playniteAPI);
            overlayWindow.Hide();

            keyboardHook = new GlobalKeyboardHook();
            keyboardHook.KeyPressed += OnKeyPressed;
        }

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            logger.Info("Stopping Overlay...");
            overlayWindow?.Close();
            keyboardHook?.Dispose();
        }

        private void OnKeyPressed(Keys key, bool altPressed)
        {
            if (altPressed && key == Keys.Oem3) // Alt + ` (backtick)
            {
                if(overlayWindow.activeGame != null)
                {
                    if (overlayWindow.IsVisible)
                        overlayWindow.Hide();
                    else
                        overlayWindow.ShowOverlay();
                } else
                {
                    overlayWindow.ShowPlaynite();
                }
            }

            if (key == Keys.Escape && overlayWindow.IsVisible)
            {
                overlayWindow.Hide();
            }
        }

        public override void OnGameStarted(OnGameStartedEventArgs args)
        {
            overlayWindow.activeGame = args.Game;
            overlayWindow.Pid = args.StartedProcessId;
            overlayWindow.gameStarted = DateTime.Now;
        }

        public override void OnGameStopped(OnGameStoppedEventArgs args)
        {
            overlayWindow.activeGame = null;
            overlayWindow.Pid = null;
        }

        public override System.Windows.Controls.UserControl GetSettingsView(bool firstRunSettings)
        {
            return new PlayniteGameOverlaySettingsView();
        }
    }
}