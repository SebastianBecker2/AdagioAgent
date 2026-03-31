using System.Windows;
using System.Windows.Controls;
using System.Diagnostics;
using System.IO;

namespace AdagioMachineAgent.BootstrapperApplication
{
    /// <summary>
    /// Summary screen showing configuration review before installation.
    /// </summary>
    public class SummaryScreen : WizardScreen
    {
        private readonly InstallerContext _context;
        private TextBlock? _certificateModeValue;
        private TextBlock? _certificatePathValue;
        private TextBlock? _certificatePemExportValue;
        private TextBlock? _certificatePfxExportValue;
        private TextBlock? _apiKeyModeValue;
        private TextBlock? _requireHttpsValue;
        private TextBlock? _requireApiKeyValue;
        private TextBlock? _urlsValue;
        private TextBlock? _allowedHostsValue;
        private TextBlock? _executablePathsValue;
        private TextBlock? _writablePathsValue;
        private TextBlock? _readablePathsValue;

        public SummaryScreen(InstallerContext context)
        {
            _context = context;
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
            _certificateModeValue = CreateSummaryValue();
            _certificatePathValue = CreateSummaryValue();
            _certificatePemExportValue = CreateSummaryValue();
            _certificatePfxExportValue = CreateSummaryValue();
            certSection.Children.Add(_certificateModeValue);
            certSection.Children.Add(_certificatePathValue);
            certSection.Children.Add(_certificatePemExportValue);
            certSection.Children.Add(_certificatePfxExportValue);
            summaryContent.Children.Add(certSection);

            // API Key Info
            var apiSection = CreateSummarySection("Security Configuration");
            _apiKeyModeValue = CreateSummaryValue();
            _requireHttpsValue = CreateSummaryValue();
            _requireApiKeyValue = CreateSummaryValue();
            apiSection.Children.Add(_apiKeyModeValue);
            apiSection.Children.Add(_requireHttpsValue);
            apiSection.Children.Add(_requireApiKeyValue);
            summaryContent.Children.Add(apiSection);

            // Network Info
            var networkSection = CreateSummarySection("Network Configuration");
            _urlsValue = CreateSummaryValue();
            _allowedHostsValue = CreateSummaryValue();
            networkSection.Children.Add(_urlsValue);
            networkSection.Children.Add(_allowedHostsValue);
            summaryContent.Children.Add(networkSection);

            // Path Info
            var pathSection = CreateSummarySection("Path Security");
            _executablePathsValue = CreateSummaryValue();
            _writablePathsValue = CreateSummaryValue();
            _readablePathsValue = CreateSummaryValue();
            pathSection.Children.Add(_executablePathsValue);
            pathSection.Children.Add(_writablePathsValue);
            pathSection.Children.Add(_readablePathsValue);
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

            var actionsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 16, 0, 0)
            };

            var copyDetailsButton = new Button
            {
                Content = "Copy Connection Details",
                Width = 180,
                Height = 34,
                Margin = new Thickness(0, 0, 10, 0)
            };
            copyDetailsButton.Click += (_, _) =>
            {
                Clipboard.SetText(BuildConnectionDetailsText());
                MessageBox.Show("Connection details copied to clipboard.", "Summary", MessageBoxButton.OK, MessageBoxImage.Information);
            };
            actionsPanel.Children.Add(copyDetailsButton);

            var openExportFolderButton = new Button
            {
                Content = "Open Export Folder",
                Width = 150,
                Height = 34
            };
            openExportFolderButton.Click += (_, _) => OpenExportFolder();
            actionsPanel.Children.Add(openExportFolderButton);
            content.Children.Add(actionsPanel);

            scrollViewer.Content = content;
            mainGrid.Children.Add(scrollViewer);
            this.Content = mainGrid;

            RefreshSummary();
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

        private TextBlock CreateSummaryValue()
        {
            return new TextBlock
            {
                Margin = new Thickness(10, 0, 0, 5),
                TextWrapping = TextWrapping.Wrap
            };
        }

        private void RefreshSummary()
        {
            if (_certificateModeValue == null ||
                _certificatePathValue == null ||
                _apiKeyModeValue == null ||
                _requireHttpsValue == null ||
                _requireApiKeyValue == null ||
                _certificatePemExportValue == null ||
                _certificatePfxExportValue == null ||
                _urlsValue == null ||
                _allowedHostsValue == null ||
                _executablePathsValue == null ||
                _writablePathsValue == null ||
                _readablePathsValue == null)
            {
                return;
            }

            _certificateModeValue.Text = $"Mode: {_context.CertificateMode}";
            _certificatePathValue.Text = _context.CertificateMode == "Provided"
                ? $"Provided Certificate: {_context.ProvidedCertificatePath ?? "(missing)"}"
                : "Provided Certificate: N/A";
            _certificatePemExportValue.Text = $"CA PEM Export: {_context.CaCertificatePemPath}";
            _certificatePfxExportValue.Text = $"CA PFX Export: {_context.CaCertificatePfxPath}";

            _apiKeyModeValue.Text = _context.ApiKeyMode == "Provided"
                ? "API Key Mode: Provided (value hidden)"
                : "API Key Mode: Generate automatically during install";
            _requireHttpsValue.Text = $"Require HTTPS: {_context.RequireHttps}";
            _requireApiKeyValue.Text = $"Require API Key: {_context.RequireApiKey}";

            _urlsValue.Text = $"Service URLs: {_context.Urls}";
            _allowedHostsValue.Text = $"Allowed Hosts: {_context.AllowedHosts}";

            _executablePathsValue.Text = $"Executable Paths: {string.Join("; ", _context.AllowedExecutablePaths)}";
            _writablePathsValue.Text = $"Writable Paths: {string.Join("; ", _context.AllowedWritablePaths)}";
            _readablePathsValue.Text = $"Readable Paths: {string.Join("; ", _context.AllowedReadablePaths)}";
        }

        public override void OnBeforeShown()
        {
            RefreshSummary();
        }

        public override bool Validate() => true;

        private string BuildConnectionDetailsText()
        {
            return
                "Adagio Machine Agent Connection Details\n" +
                $"Certificate mode: {_context.CertificateMode}\n" +
                $"CA PEM path: {_context.CaCertificatePemPath}\n" +
                $"CA PFX path: {_context.CaCertificatePfxPath}\n" +
                $"API key mode: {_context.ApiKeyMode}\n" +
                $"Require HTTPS: {_context.RequireHttps}\n" +
                $"Require API key: {_context.RequireApiKey}\n" +
                $"URLs: {_context.Urls}\n" +
                $"Allowed hosts: {_context.AllowedHosts}";
        }

        private void OpenExportFolder()
        {
            var exportDirectory = Path.GetDirectoryName(_context.CaCertificatePemPath);
            if (string.IsNullOrWhiteSpace(exportDirectory))
            {
                MessageBox.Show("Export directory is not set.", "Summary", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                Directory.CreateDirectory(exportDirectory);
                Process.Start(new ProcessStartInfo
                {
                    FileName = exportDirectory,
                    UseShellExecute = true
                });
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Unable to open export folder: {ex.Message}", "Summary", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
