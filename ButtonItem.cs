using System;

namespace PlayniteGameOverlay
{
    public enum ButtonAction
    {
        Directory,
        KeyboardShortcut,
        Uri,
        Executable,
        Shortcut
    }

    public class ButtonItem
    {
        public string IconPath { get; set; }
        public string Title { get; set; }
        public ButtonAction ActionType { get; set; }
        public string Path { get; set; }
    }
}