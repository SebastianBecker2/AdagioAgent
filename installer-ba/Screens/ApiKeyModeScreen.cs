using System.Windows;
using System.Windows.Controls;

namespace AdagioMachineAgent.BootstrapperApplication
{
    /// <summary>
    /// Screen for selecting API key mode (Generate, Provided) and security toggles.
    /// </summary>
    public class ApiKeyModeScreen : WizardScreen
    {
        private RadioButton? _generateRadio;
        private RadioButton? _providedRadio;
        private TextBox? _apiKeyBox;
        private CheckBox? _requireHttpsCheck;
        private CheckBox? _requireApiKeyCheck;

        public string? SelectedApiKeyMode { get; private set; }
        public string? ProvidedApiKey { get; private set; }
        public bool RequireHttps { get; private set; } = true;
        public bool RequireApiKey { get; private set; } = true;

        public ApiKeyModeScreen()
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
                Text = "Security Configuration",
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 20)
            };
            content.Children.Add(title);

            // API Key Mode Section
            var apiKeyDesc = new TextBlock
            {
                Text = "Configure API key management:",
                FontSize = 12,
                Foreground = System.Windows.Media.Brushes.Gray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 15)
            };
            content.Children.Add(apiKeyDesc);

            // Generate API Key Option
            _generateRadio = new RadioButton
            {
                Content = "Generate a new API key automatically",
                Margin = new Thickness(0, 10, 0, 0),
                IsChecked = true
            };
            _generateRadio.Checked += (s, e) => UpdateUI();
            content.Children.Add(_generateRadio);

            var generateDesc = new TextBlock
            {
                Text = "The agent will generate a secure API key. You'll need to save this key after installation.",
                FontSize = 11,
                Foreground = System.Windows.Media.Brushes.Gray,
                Margin = new Thickness(20, 5, 0, 0)
            };
            content.Children.Add(generateDesc);

            // Provided API Key Option
            _providedRadio = new RadioButton
            {
                Content = "Use an existing API key",
                Margin = new Thickness(0, 15, 0, 0)
            };
            _providedRadio.Checked += (s, e) => UpdateUI();
            content.Children.Add(_providedRadio);

            var providedDesc = new TextBlock
            {
                Text = "Provide an existing Base64-encoded API key from a previous installation.",
                FontSize = 11,
                Foreground = System.Windows.Media.Brushes.Gray,
                Margin = new Thickness(20, 5, 0, 10)
            };
            content.Children.Add(providedDesc);

            // Provided API Key input (initially disabled)
            var apiKeyPanel = new StackPanel { Margin = new Thickness(20, 0, 0, 0) };
            var apiKeyLabel = new TextBlock { Text = "API Key (Base64):", Margin = new Thickness(0, 0, 0, 5), FontWeight = FontWeights.SemiBold };
            _apiKeyBox = new TextBox { Height = 32, Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap, AcceptsReturn = true };
            apiKeyPanel.Children.Add(apiKeyLabel);
            apiKeyPanel.Children.Add(_apiKeyBox);
            content.Children.Add(apiKeyPanel);

            // Security Options Section
            var securitySeparator = new Separator { Margin = new Thickness(0, 20, 0, 20) };
            content.Children.Add(securitySeparator);

            var securityTitle = new TextBlock
            {
                Text = "Security Options",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 15)
            };
            content.Children.Add(securityTitle);

            // Require HTTPS
            _requireHttpsCheck = new CheckBox
            {
                Content = "Require HTTPS for all connections",
                IsChecked = true,
                Margin = new Thickness(0, 10, 0, 10),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            content.Children.Add(_requireHttpsCheck);

            var httpsDesc = new TextBlock
            {
                Text = "When enabled, all service URLs must use HTTPS. Recommended for production.",
                FontSize = 11,
                Foreground = System.Windows.Media.Brushes.Gray,
                Margin = new Thickness(20, 0, 0, 10)
            };
            content.Children.Add(httpsDesc);

            // Require API Key
            _requireApiKeyCheck = new CheckBox
            {
                Content = "Require API key for all requests",
                IsChecked = true,
                Margin = new Thickness(0, 10, 0, 10),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            content.Children.Add(_requireApiKeyCheck);

            var apiKeyReqDesc = new TextBlock
            {
                Text = "When enabled, all requests must include a valid API key. Recommended for production.",
                FontSize = 11,
                Foreground = System.Windows.Media.Brushes.Gray,
                Margin = new Thickness(20, 0, 0, 0)
            };
            content.Children.Add(apiKeyReqDesc);

            scrollViewer.Content = content;
            mainGrid.Children.Add(scrollViewer);
            this.Content = mainGrid;

            UpdateUI();
        }

        private void UpdateUI()
        {
            bool isProvidedSelected = _providedRadio?.IsChecked == true;
            if (_apiKeyBox != null)
                _apiKeyBox.IsEnabled = isProvidedSelected;
        }

        public override bool Validate()
        {
            if (_generateRadio?.IsChecked == true)
            {
                SelectedApiKeyMode = "Generate";
            }
            else if (_providedRadio?.IsChecked == true)
            {
                SelectedApiKeyMode = "Provided";
                ProvidedApiKey = _apiKeyBox?.Text;

                if (string.IsNullOrWhiteSpace(ProvidedApiKey))
                {
                    MessageBox.Show("API key is required when using a provided key.", "Validation Error");
                    return false;
                }
            }

            RequireHttps = _requireHttpsCheck?.IsChecked ?? true;
            RequireApiKey = _requireApiKeyCheck?.IsChecked ?? true;

            return true;
        }
    }
}
