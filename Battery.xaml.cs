using System;
using System.Threading;
using System.Windows.Forms;
using UserControl = System.Windows.Controls.UserControl;

namespace PlayniteGameOverlay
{
    /// <summary>
    /// Interaction logic for Battery.xaml
    /// </summary>
    public partial class Battery : UserControl
    {
        private readonly int MAX_BAR_WIDTH = 120;
        public int barWidth = 0;
        public Battery()
        {
            InitializeComponent();
            while (true)
            {
                // Get battery information
                PowerStatus power = SystemInformation.PowerStatus;
                float batteryPercentage = power.BatteryLifePercent;
                barWidth = (int)(MAX_BAR_WIDTH * batteryPercentage);
                // Wait for one minute
                BatteryBar.Width = barWidth;
                Thread.Sleep(60000); // 60000 milliseconds = 1 minute
            }
        }
    }
}
