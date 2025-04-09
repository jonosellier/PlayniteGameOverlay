using System;

namespace PlayniteGameOverlay
{
    public class ButtonItem
    {
        public string IconPath { get; set; }
        public string Title { get; set; }
        public string Path { get; set; }
        public Action ClickAction { get; set; }
    }
}