using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Navigation;

namespace PlayniteGameOverlay
{
    public partial class OverlaySettingsView : UserControl
    {
        public OverlaySettingsView()
        {
            InitializeComponent();
        }

        public Dictionary<ControllerShortcut, string> ControllerShortcutDisplayNames { get; } = new Dictionary<ControllerShortcut, string>()
        {
            { ControllerShortcut.StartBack, "View + Menu (Back + Start)" },
            { ControllerShortcut.Guide, "Xbox Button (Guide Button)" }
        };

        public event PropertyChangedEventHandler PropertyChanged;

        private ControllerShortcut _controllerShortcut;

        public ControllerShortcut ControllerShortcut
        {
            get => _controllerShortcut;
            set
            {
                _controllerShortcut = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ControllerShortcut)));
            }
        }


        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }
    }

    public class EnumDescriptionConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Enum enumValue)
            {
                return GetEnumDescription(enumValue);
            }
            if(value != null)
            {
                return value.ToString();
            }
            return "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Enum.Parse(targetType, value.ToString());
        }

        private string GetEnumDescription(Enum value)
        {
            FieldInfo field = value.GetType().GetField(value.ToString());
            DescriptionAttribute attribute = field?.GetCustomAttribute<DescriptionAttribute>();

            return attribute?.Description ?? value.ToString();
        }
    }
}