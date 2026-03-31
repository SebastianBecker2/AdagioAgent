using System.Windows;
using System.Windows.Controls;
using System.Linq;

namespace AdagioMachineAgent.BootstrapperApplication
{
    /// <summary>
    /// Screen for configuring network settings (service URLs and allowed hosts).
    /// </summary>
    public class NetworkConfigurationScreen : WizardScreen
    {
        private readonly InstallerContext _context;
        private TextBox? _urlsBox;
        private ListBox? _hostList;
        private TextBox? _customHostBox;
        private ComboBox? _protocolCombo;
        private TextBox? _urlHostBox;
        private TextBox? _urlPortBox;
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

            var urlBuilderRow = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            urlBuilderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            urlBuilderRow.ColumnDefinitions.Add(new ColumnDefinition());
            urlBuilderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            urlBuilderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });

            _protocolCombo = new ComboBox
            {
                Height = 30,
                Margin = new Thickness(0, 0, 8, 0),
                ItemsSource = new[] { "https", "http" },
                SelectedIndex = _context.RequireHttps ? 0 : 1
            };
            Grid.SetColumn(_protocolCombo, 0);
            urlBuilderRow.Children.Add(_protocolCombo);

            _urlHostBox = new TextBox
            {
                Height = 30,
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 8, 0),
                Text = discoveryData.HostName ?? "localhost"
            };
            Grid.SetColumn(_urlHostBox, 1);
            urlBuilderRow.Children.Add(_urlHostBox);

            _urlPortBox = new TextBox
            {
                Height = 30,
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 8, 0),
                Text = "5443"
            };
            Grid.SetColumn(_urlPortBox, 2);
            urlBuilderRow.Children.Add(_urlPortBox);

            var addUrlButton = new Button
            {
                Content = "Add URL",
                Height = 30
            };
            addUrlButton.Click += AddUrlButton_Click;
            Grid.SetColumn(addUrlButton, 3);
            urlBuilderRow.Children.Add(addUrlButton);
            content.Children.Add(urlBuilderRow);

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
                Text = "Allowed Hosts (multi-select):",
                Margin = new Thickness(0, 0, 0, 5),
                FontWeight = FontWeights.SemiBold
            };
            content.Children.Add(hostsLabel);

            _hostList = new ListBox
            {
                Height = 60,
                Margin = new Thickness(0, 0, 0, 10),
                SelectionMode = SelectionMode.Multiple
            };

            foreach (var host in BuildHostCandidates(discoveryData))
            {
                _hostList.Items.Add(host);
            }

            content.Children.Add(_hostList);

            var customHostRow = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            customHostRow.ColumnDefinitions.Add(new ColumnDefinition());
            customHostRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });

            _customHostBox = new TextBox
            {
                Height = 30,
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 8, 0),
                ToolTip = "Add custom hostname or IP"
            };
            Grid.SetColumn(_customHostBox, 0);
            customHostRow.Children.Add(_customHostBox);

            var addHostButton = new Button
            {
                Content = "Add Host",
                Height = 30
            };
            addHostButton.Click += AddHostButton_Click;
            Grid.SetColumn(addHostButton, 1);
            customHostRow.Children.Add(addHostButton);
            content.Children.Add(customHostRow);

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

            RestoreHostSelections();
        }

        public override bool Validate()
        {
            Urls = _urlsBox?.Text?.Trim();
            AllowedHosts = GetSelectedHostsText();

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
                var trimmed = url.Trim();
                if (!System.Uri.TryCreate(trimmed, System.UriKind.Absolute, out var parsed) ||
                    (parsed.Scheme != "http" && parsed.Scheme != "https"))
                {
                    MessageBox.Show("All URLs must start with http:// or https://", "Validation Error");
                    return false;
                }

                if (_context.RequireHttps && parsed.Scheme != "https")
                {
                    MessageBox.Show("Require HTTPS is enabled. Remove HTTP endpoints.", "Validation Error");
                    return false;
                }
            }

            _context.Urls = Urls;
            _context.AllowedHosts = AllowedHosts;

            return true;
        }

        private void AddUrlButton_Click(object sender, RoutedEventArgs e)
        {
            if (_protocolCombo == null || _urlHostBox == null || _urlPortBox == null || _urlsBox == null)
            {
                return;
            }

            var scheme = _protocolCombo.SelectedItem?.ToString() ?? "https";
            var host = _urlHostBox.Text.Trim();
            var portText = _urlPortBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(host) || !int.TryParse(portText, out var port) || port < 1 || port > 65535)
            {
                MessageBox.Show("Enter a valid host and port before adding a URL.", "URL Builder", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var url = $"{scheme}://{host}:{port}";
            var existing = _urlsBox.Text
                .Split(new[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList();

            if (!existing.Contains(url, System.StringComparer.OrdinalIgnoreCase))
            {
                existing.Add(url);
                _urlsBox.Text = string.Join("; ", existing);
            }
        }

        private void AddHostButton_Click(object sender, RoutedEventArgs e)
        {
            if (_customHostBox == null || _hostList == null)
            {
                return;
            }

            var host = _customHostBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(host))
            {
                return;
            }

            var exists = _hostList.Items.Cast<string>().Any(item => item.Equals(host, System.StringComparison.OrdinalIgnoreCase));
            if (!exists)
            {
                _hostList.Items.Add(host);
            }

            _hostList.SelectedItems.Add(host);
            _customHostBox.Text = string.Empty;
        }

        private void RestoreHostSelections()
        {
            if (_hostList == null)
            {
                return;
            }

            var selectedHosts = _context.AllowedHosts
                .Split(new[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToHashSet(System.StringComparer.OrdinalIgnoreCase);

            foreach (var host in _hostList.Items.Cast<string>())
            {
                if (selectedHosts.Contains(host))
                {
                    _hostList.SelectedItems.Add(host);
                }
            }
        }

        private string GetSelectedHostsText()
        {
            if (_hostList == null)
            {
                return string.Empty;
            }

            var selected = _hostList.SelectedItems.Cast<string>().ToList();
            return string.Join(";", selected);
        }

        private static string[] BuildHostCandidates(DiscoveryData discoveryData)
        {
            var hosts = new System.Collections.Generic.List<string>
            {
                "localhost",
                "127.0.0.1"
            };

            if (!string.IsNullOrWhiteSpace(discoveryData.HostName))
            {
                hosts.Add(discoveryData.HostName);
            }

            hosts.AddRange(discoveryData.DiscoveredIPAddresses);
            return hosts
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(System.StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }
}
