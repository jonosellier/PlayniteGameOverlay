using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PlayniteGameOverlay
{
    public partial class OverlayWindow
    {
        private Button audioButton;
        private Border audioPanel;
        private bool audioLoading;
        private Action closeAudioSelection;

        public void AudioBackOrHide()
        {
            if (closeAudioSelection != null) closeAudioSelection();
            else Hide();
        }

        private void InitializeAudioSelection()
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
            content.Children.Add(new System.Windows.Shapes.Path {
                Data = Geometry.Parse("M3,9 L7,9 L12,4 L12,20 L7,15 L3,15 Z M16,8 C19,10 19,14 16,16 M19,4 C25,8 25,16 19,20"),
                Stroke = Brushes.White, StrokeThickness = 1.8, StrokeLineJoin = PenLineJoin.Round,
                Width = 26, Height = 26, Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,15,0)
            });
            content.Children.Add(new TextBlock { Text = "Audio output", VerticalAlignment = VerticalAlignment.Center });
            audioButton = new Button { Content = content, Style = CloseGameButton.Style,
                Width = CloseGameButton.Width, Height = CloseGameButton.Height,
                FontSize = CloseGameButton.FontSize, FontWeight = CloseGameButton.FontWeight,
                Foreground = CloseGameButton.Foreground, BorderThickness = CloseGameButton.BorderThickness,
                Padding = CloseGameButton.Padding, Margin = new Thickness(0,0,0,20) };
            var menu = (StackPanel)CloseGameButton.Parent;
            menu.Children.Insert(menu.Children.IndexOf(CloseGameButton), audioButton);
            audioButton.Click += async (s, e) => await OpenAudioSelection();
            IsVisibleChanged += (s, e) => { if (!IsVisible && closeAudioSelection != null) closeAudioSelection(); };
            PreviewKeyDown += (s, e) => {
                if (audioPanel != null && e.Key == System.Windows.Input.Key.Escape) {
                    AudioBackOrHide(); e.Handled = true;
                }
            };
        }

        private async Task OpenAudioSelection()
        {
            if (audioLoading || audioPanel != null) return;
            audioLoading = true;
            var host = (Grid)((System.Windows.Controls.Viewbox)Content).Child;
            var list = new StackPanel();
            list.Children.Add(new TextBlock { Text = "Audio output", FontSize = 28, Foreground = Brushes.White, Margin = new Thickness(10) });
            var status = new TextBlock { Text = "Loading audio outputs…", FontSize = 18, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10) };
            list.Children.Add(status);
            audioPanel = new Border { Background = new SolidColorBrush(Color.FromRgb(36,36,36)),
                BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
                Padding = new Thickness(20), Width = 760, MaxHeight = 760,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Child = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
            System.Windows.Input.KeyboardNavigation.SetTabNavigation(audioPanel, System.Windows.Input.KeyboardNavigationMode.Cycle);
            var previous = host.Children.Cast<UIElement>().ToDictionary(child => child, child => child.IsEnabled);
            foreach (var child in previous.Keys) child.IsEnabled = false;
            Action close = () => { if (audioPanel == null) return; host.Children.Remove(audioPanel); audioPanel = null; closeAudioSelection = null;
                foreach (var child in previous) child.Key.IsEnabled = child.Value; audioButton.Focus(); };
            closeAudioSelection = close;
            host.Children.Add(audioPanel);
            Button first = null;
            try
            {
                var devices = await Task.Run(() => AudioOutputService.GetOutputs());
                if (audioPanel == null) return;
                status.Text = devices.Count == 0 ? "No audio outputs available." : "Choose an output. Some games require restarting their audio or restarting the game.";
                foreach (var device in devices)
                {
                    var id = device.Id;
                    bool current = device.IsDefault;
                    var button = new Button { Content = (current ? "✓  " : "") + device.Name,
                        Style = CloseGameButton.Style, Foreground = Brushes.White, FontSize = 20, MinHeight = 65, Margin = new Thickness(5) };
                    button.GotKeyboardFocus += (s,e) => button.BringIntoView();
                    button.Click += async (s,e) => {
                        if (audioLoading) return;
                        audioLoading = true; status.Text = "Switching audio output…";
                        try {
                            await Task.Run(() => {
                                AudioOutputService.SetDefault(id);
                            });
                            close();
                        } catch (Exception ex) { status.Text = ex.Message; }
                        finally { audioLoading = false; }
                    };
                    list.Children.Add(button); if (first == null || current) first = button;
                }
            }
            catch (Exception ex) { status.Text = "Unable to list audio outputs: " + ex.Message; }
            finally { audioLoading = false; }
            var back = new Button { Content = "Back", Style = CloseGameButton.Style, Foreground = Brushes.White, FontSize = 20, Height = 65, Margin = new Thickness(5) };
            back.Click += (s,e) => close(); list.Children.Add(back); (first ?? back).Focus();
        }
    }
}
