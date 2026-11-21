using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CodexUsageBubble {
    internal sealed class AboutWindow : Window {
        static readonly BitmapSource headerIcon = LoadHeaderIcon();

        internal AboutWindow(WidgetTheme theme) {
            Title = "About CodexUsageWidget";
            Width = 560;
            Height = 580;
            MinWidth = 440;
            MinHeight = 400;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = false;
            UseLayoutRounding = true;
            FontFamily = new FontFamily("Segoe UI");
            Background = theme.Color("Surface");
            Foreground = theme.Color("Text");

            var layout = new DockPanel { Margin = new Thickness(22, 18, 22, 14) };
            var footer = CreateFooter(theme);
            DockPanel.SetDock(footer, Dock.Bottom);
            layout.Children.Add(footer);

            var body = new StackPanel();
            body.Children.Add(CreateHeader(theme));
            var heading = Text("Features", 17, theme.Color("Text"));
            heading.FontWeight = FontWeights.SemiBold;
            heading.Margin = new Thickness(0, 11, 0, 6);
            body.Children.Add(heading);

            var features = new StackPanel();
            AddFeature(features, theme, "Floating display", "Drag or resize the always-on-top widget. Hover a usage bar for the exact reset time.");
            AddFeature(features, theme, "Minimal mode", "Circle or square, four sizes. Double-click to swap remaining percentages for countdowns. Background bars track reset progress.");
            AddFeature(features, theme, "Themes and snapping", "Midnight, Ocean, or Light. Remembers its position and can snap to screen edges or follow windows.");
            AddFeature(features, theme, "Refresh and tray", "Adaptive refresh. Right-click to refresh or pause. Close hides to the tray by default. Tray Exit stops the widget.");
            AddFeature(features, theme, "Auto Reset Messages", "Send a one-time or recurring prompt to an existing, new, or dedicated chat after reset or at a chosen time. Keep the widget running for armed messages, which use normal allowance.");
            body.Children.Add(new Border {
                Background = theme.Color("MenuBackground"),
                BorderBrush = theme.Color("Outline"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12, 0, 12, 0),
                Child = features
            });

            var privacy = Text(@"Preferences, diagnostics, and armed message details stay under %LOCALAPPDATA%\CodexUsageBubble by default.", 12, theme.Color("Secondary"));
            privacy.Margin = new Thickness(0, 8, 0, 3);
            body.Children.Add(privacy);
            body.Children.Add(Text("MIT license · Independent project with no affiliation with OpenAI.", 12, theme.Color("Secondary")));

            layout.Children.Add(new ScrollViewer {
                Content = body,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            });
            Content = layout;
        }

        static BitmapSource LoadHeaderIcon() {
            using (var stream = typeof(AboutWindow).Assembly.GetManifestResourceStream("CodexUsageBubble.ring.ico")) {
                if (stream == null) { return null; }

                var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                var frame = decoder.Frames.OrderByDescending(image => image.PixelWidth).First();
                frame.Freeze();
                return frame;
            }
        }

        static Border CreateHeader(WidgetTheme theme) {
            var header = new DockPanel { LastChildFill = true };
            var icon = new Image { Width = 38, Height = 38, Source = headerIcon };
            RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
            var iconFrame = new Border {
                Width = 52,
                Height = 52,
                Margin = new Thickness(0, 0, 13, 0),
                VerticalAlignment = VerticalAlignment.Top,
                CornerRadius = new CornerRadius(12),
                Background = theme.Color("Track"),
                Child = icon
            };
            DockPanel.SetDock(iconFrame, Dock.Left);
            header.Children.Add(iconFrame);

            var title = Text("CodexUsageWidget", 26, theme.Color("Text"));
            title.FontWeight = FontWeights.SemiBold;
            title.Margin = new Thickness(0, 0, 0, 4);
            var copy = new StackPanel();
            copy.Children.Add(title);
            copy.Children.Add(Text("See remaining five-hour and weekly usage and reset times for the account signed in to Codex.", 13, theme.Color("Secondary")));
            header.Children.Add(copy);
            return new Border {
                Background = theme.Color("MenuBackground"),
                BorderBrush = theme.Color("Outline"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(12),
                Child = header
            };
        }

        static void AddFeature(Panel features, WidgetTheme theme, string name, string detail) {
            var row = new Grid { Margin = new Thickness(0, 5, 0, 5) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(114) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var label = Text(name, 13, theme.Color("Accent"));
            label.FontWeight = FontWeights.SemiBold;
            label.Margin = new Thickness(0, 0, 8, 0);
            row.Children.Add(label);
            var description = Text(detail, 12.5, theme.Color("Text"));
            Grid.SetColumn(description, 1);
            row.Children.Add(description);

            if (features.Children.Count > 0) {
                features.Children.Add(new Border { Height = 1, Background = theme.Color("MenuSeparator") });
            }
            features.Children.Add(row);
        }

        Border CreateFooter(WidgetTheme theme) {
            var actions = new DockPanel();
            var close = new Button {
                Content = "Close",
                MinWidth = 74,
                Padding = new Thickness(12, 5, 12, 5),
                IsCancel = true,
                Background = theme.Color("Track"),
                BorderBrush = theme.Color("Outline"),
                Foreground = theme.Color("Text"),
                Template = (ControlTemplate)XamlReader.Parse(@"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                 xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type Button}'>
    <Border x:Name='Frame' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}'
            BorderThickness='1' CornerRadius='6' Padding='{TemplateBinding Padding}'>
        <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center' RecognizesAccessKey='True'/>
    </Border>
    <ControlTemplate.Triggers>
        <Trigger Property='IsMouseOver' Value='True'>
            <Setter TargetName='Frame' Property='Opacity' Value='0.8'/>
        </Trigger>
    </ControlTemplate.Triggers>
</ControlTemplate>")
            };
            close.Click += delegate { Close(); };
            DockPanel.SetDock(close, Dock.Right);
            actions.Children.Add(close);

            var link = new Hyperlink(new Run("README and screenshots on GitHub")) {
                NavigateUri = new Uri("https://github.com/cloudd901/CodexUsageWidget#readme"),
                Foreground = theme.Color("Accent")
            };
            link.RequestNavigate += delegate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e) {
                e.Handled = true;
                try {
                    Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
                }
                catch (Win32Exception) {
                    MessageBox.Show(this, "Windows could not open the GitHub README in your browser.", Title);
                }
            };
            actions.Children.Add(new TextBlock(link) {
                FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0)
            });
            return new Border {
                Margin = new Thickness(0, 10, 0, 0),
                Padding = new Thickness(0, 10, 0, 0),
                BorderBrush = theme.Color("MenuSeparator"),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Child = actions
            };
        }

        static TextBlock Text(string text, double size, Brush color) {
            return new TextBlock { Text = text, FontSize = size, Foreground = color, TextWrapping = TextWrapping.Wrap };
        }
    }
}
