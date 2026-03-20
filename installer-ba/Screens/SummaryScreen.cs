using System.Windows;
using System.Windows.Controls;

namespace AdagioMachineAgent.BootstrapperApplication
{
    /// <summary>
    /// Summary screen showing configuration review before installation.
    /// </summary>
    public class SummaryScreen : WizardScreen
    {
        public SummaryScreen()
        {
            InitializeUI();
        }

        private void InitializeUI()
        {
            var mainGrid = new Grid
            {
                Background = System.Windows.Media.Brushes.White,
                Margin = new Thickness(40)
            };

            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            var content = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };

            // Title
            var title = new TextBlock
            {
                Text = "Installation Summary",
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 20)
            };
            content.Children.Add(title);

            var description = new TextBlock
            {
                Text = "Review your configuration below. Click 'Install' to proceed with the installation.",
                FontSize = 12,
                Foreground = System.Windows.Media.Brushes.Gray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 20)
            };
            content.Children.Add(description);

            // Summary info - note: will be populated from main window
            var summaryBox = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(245, 245, 245)),
                BorderBrush = System.Windows.Media.Brushes.LightGray,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 20)
            };

            var summaryContent = new StackPanel();

            // Certificate Info
            var certSection = CreateSummarySection("Certificate Configuration");
            certSection.Children.Add(new TextBlock { Text = "Mode: (will be populated from wizard)", Margin = new Thickness(10, 0, 0, 5) });
            summaryContent.Children.Add(certSection);

            // API Key Info
            var apiSection = CreateSummarySection("Security Configuration");
            apiSection.Children.Add(new TextBlock { Text = "API Key Mode: (will be populated from wizard)", Margin = new Thickness(10, 0, 0, 5) });
            apiSection.Children.Add(new TextBlock { Text = "Require HTTPS: (will be populated from wizard)", Margin = new Thickness(10, 0, 0, 5) });
            summaryContent.Children.Add(apiSection);

            // Network Info
            var networkSection = CreateSummarySection("Network Configuration");
            networkSection.Children.Add(new TextBlock { Text = "Service URLs: (will be populated from wizard)", Margin = new Thickness(10, 0, 0, 5) });
            networkSection.Children.Add(new TextBlock { Text = "Allowed Hosts: (will be populated from wizard)", Margin = new Thickness(10, 0, 0, 5) });
            summaryContent.Children.Add(networkSection);

            // Path Info
            var pathSection = CreateSummarySection("Path Security");
            pathSection.Children.Add(new TextBlock { Text = "Executable Paths: (will be populated from wizard)", Margin = new Thickness(10, 0, 0, 5) });
            pathSection.Children.Add(new TextBlock { Text = "Writable Paths: (will be populated from wizard)", Margin = new Thickness(10, 0, 0, 5) });
            pathSection.Children.Add(new TextBlock { Text = "Readable Paths: (will be populated from wizard)", Margin = new Thickness(10, 0, 0, 5) });
            summaryContent.Children.Add(pathSection);

            summaryBox.Child = summaryContent;
            content.Children.Add(summaryBox);

            // Warning info
            var warningBox = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 250, 205)),
                BorderBrush = System.Windows.Media.Brushes.Gold,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 0)
            };

            var warningContent = new StackPanel();
            var warningTitle = new TextBlock
            {
                Text = "⚠ Important Information",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            };
            warningContent.Children.Add(warningTitle);

            var warningText = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                Foreground = System.Windows.Media.Brushes.DarkGoldenrod,
                Text = "• If generating a new CA certificate, you must export the CA PEM file for client validation\n" +
                       "• If generating a new API key, save the key after installation - it cannot be retrieved later\n" +
                       "• These settings can be modified in the appsettings.json file after installation\n" +
                       "• The installation will create necessary directories and configure Windows service autostart"
            };
            warningContent.Children.Add(warningText);

            warningBox.Child = warningContent;
            content.Children.Add(warningBox);

            scrollViewer.Content = content;
            mainGrid.Children.Add(scrollViewer);
            this.Content = mainGrid;
        }

        private StackPanel CreateSummarySection(string title)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 15) };
            var titleBlock = new TextBlock
            {
                Text = title,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            };
            panel.Children.Add(titleBlock);
            return panel;
        }

        public override bool Validate() => true;
    }
}
