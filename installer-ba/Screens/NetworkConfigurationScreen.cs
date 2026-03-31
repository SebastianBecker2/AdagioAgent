using System.Windows;
using System.Windows.Controls;

namespace AdagioMachineAgent.BootstrapperApplication
{
    /// <summary>
    /// Screen for configuring network settings (service URLs and allowed hosts).
    /// </summary>
    public class NetworkConfigurationScreen : WizardScreen
    {
        private readonly InstallerContext _context;
        private TextBox? _urlsBox;
        private TextBox? _hostsBox;
        private TextBlock? _discoveryInfo;

        public string? Urls { get; private set; }
        public string? AllowedHosts { get; private set; }

        public NetworkConfigurationScreen(InstallerContext context)
        {
            _context = context;
            InitializeUI(context.Discovery);
        }

        private void InitializeUI(DiscoveryData discoveryData)
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
                Text = "Network Configuration",
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 20)
            };
            content.Children.Add(title);

            // Discovery Info
            var discoveryPanel = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(230, 245, 255)),
                BorderBrush = System.Windows.Media.Brushes.LightBlue,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 20)
            };

            var discoveryContent = new StackPanel();
            var discoveryTitle = new TextBlock
            {
                Text = "Discovered Configuration",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            };
            discoveryContent.Children.Add(discoveryTitle);

            _discoveryInfo = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                Foreground = System.Windows.Media.Brushes.DarkGray
            };

            var infoText = $"Hostname: {discoveryData.HostName}\n" +
                           $"IP Addresses: {string.Join(", ", discoveryData.DiscoveredIPAddresses)}";
            _discoveryInfo.Text = infoText;

            discoveryContent.Children.Add(_discoveryInfo);
            discoveryPanel.Child = discoveryContent;
            content.Children.Add(discoveryPanel);

            // URLs Configuration
            var urlsLabel = new TextBlock
            {
                Text = "Service URLs (semicolon-separated):",
                Margin = new Thickness(0, 0, 0, 5),
                FontWeight = FontWeights.SemiBold
            };
            content.Children.Add(urlsLabel);

            _urlsBox = new TextBox
            {
                Height = 60,
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 10),
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                Text = _context.Urls,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            content.Children.Add(_urlsBox);

            var urlsDesc = new TextBlock
            {
                Text = "Example: https://localhost:5001; https://agent.example.com:5443",
                FontSize = 11,
                Foreground = System.Windows.Media.Brushes.Gray,
                Margin = new Thickness(0, 0, 0, 15)
            };
            content.Children.Add(urlsDesc);

            // Allowed Hosts
            var hostsLabel = new TextBlock
            {
                Text = "Allowed Hosts (semicolon-separated):",
                Margin = new Thickness(0, 0, 0, 5),
                FontWeight = FontWeights.SemiBold
            };
            content.Children.Add(hostsLabel);

            _hostsBox = new TextBox
            {
                Height = 60,
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 10),
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                Text = _context.AllowedHosts,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            content.Children.Add(_hostsBox);

            var hostsDesc = new TextBlock
            {
                Text = "These hosts are trusted by the agent. Include all expected client hostnames.",
                FontSize = 11,
                Foreground = System.Windows.Media.Brushes.Gray,
                Margin = new Thickness(0, 0, 0, 0)
            };
            content.Children.Add(hostsDesc);

            scrollViewer.Content = content;
            mainGrid.Children.Add(scrollViewer);
            this.Content = mainGrid;
        }

        public override bool Validate()
        {
            Urls = _urlsBox?.Text?.Trim();
            AllowedHosts = _hostsBox?.Text?.Trim();

            if (string.IsNullOrWhiteSpace(Urls))
            {
                MessageBox.Show("Service URLs are required.", "Validation Error");
                return false;
            }

            if (string.IsNullOrWhiteSpace(AllowedHosts))
            {
                MessageBox.Show("Allowed hosts are required.", "Validation Error");
                return false;
            }

            // Basic URL format validation
            var urls = Urls.Split(new[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries);
            foreach (var url in urls)
            {
                if (!url.Trim().StartsWith("http://") && !url.Trim().StartsWith("https://"))
                {
                    MessageBox.Show("All URLs must start with http:// or https://", "Validation Error");
                    return false;
                }
            }

            _context.Urls = Urls;
            _context.AllowedHosts = AllowedHosts;

            return true;
        }
    }
}
