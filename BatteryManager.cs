using System;
using System.Windows.Forms;

namespace PlayniteGameOverlay
{
    public class BatteryManager
    {
        public bool HasBattery => SystemInformation.PowerStatus.BatteryChargeStatus != BatteryChargeStatus.NoSystemBattery;

        public BatteryStatus GetStatus()
        {
            PowerStatus power = SystemInformation.PowerStatus;
            float batteryPercentage = power.BatteryLifePercent;

            return new BatteryStatus
            {
                Percentage = batteryPercentage,
                FormattedPercentage = $"{(int)(batteryPercentage * 100)}%"
            };
        }
    }

    public class BatteryStatus
    {
        public float Percentage { get; set; }
        public string FormattedPercentage { get; set; }
    }
}