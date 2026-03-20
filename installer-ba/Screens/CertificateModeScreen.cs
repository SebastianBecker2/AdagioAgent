using System.Windows;
using System.Windows.Controls;

namespace AdagioMachineAgent.BootstrapperApplication
{
    /// <summary>
    /// Screen for selecting certificate mode (Generated CA, Generated Leaf, Provided).
    /// </summary>
    public class CertificateModeScreen : WizardScreen
    {
        private RadioButton? _generatedCaRadio;
        private RadioButton? _generatedLeafRadio;
        private RadioButton? _providedRadio;
        private TextBox? _certPathBox;
        private PasswordBox? _certPasswordBox;

        public string? SelectedCertificateMode { get; private set; }
        public string? ProvidedCertPath { get; private set; }
        public string? ProvidedCertPassword { get; private set; }

        public CertificateModeScreen()
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
                Text = "Certificate Configuration",
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 20)
            };
            content.Children.Add(title);

            // Description
            var desc = new TextBlock
            {
                Text = "Select how the agent should manage TLS certificates.",
                FontSize = 12,
                Foreground = System.Windows.Media.Brushes.Gray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 20)
            };
            content.Children.Add(desc);

            // GeneratedCa Option
            _generatedCaRadio = new RadioButton
            {
                Content = "Generate a Certificate Authority (CA) - Recommended for new deployments",
                Margin = new Thickness(0, 10, 0, 0),
                IsChecked = true
            };
            _generatedCaRadio.Checked += (s, e) => UpdateUI();
            content.Children.Add(_generatedCaRadio);

            var generatedCaDesc = new TextBlock
            {
                Text = "The agent will auto-generate a new CA certificate and key. Export the CA PEM for client validation.",
                FontSize = 11,
                Foreground = System.Windows.Media.Brushes.Gray,
                Margin = new Thickness(20, 5, 0, 0)
            };
            content.Children.Add(generatedCaDesc);

            // GeneratedLeaf Option
            _generatedLeafRadio = new RadioButton
            {
                Content = "Generate a Leaf Certificate (self-signed)",
                Margin = new Thickness(0, 15, 0, 0)
            };
            _generatedLeafRadio.Checked += (s, e) => UpdateUI();
            content.Children.Add(_generatedLeafRadio);

            var generatedLeafDesc = new TextBlock
            {
                Text = "The agent will auto-generate a self-signed leaf certificate. Use for testing only.",
                FontSize = 11,
                Foreground = System.Windows.Media.Brushes.Gray,
                Margin = new Thickness(20, 5, 0, 0)
            };
            content.Children.Add(generatedLeafDesc);

            // Provided Option
            _providedRadio = new RadioButton
            {
                Content = "Use an existing certificate (PFX/PKCS12)",
                Margin = new Thickness(0, 15, 0, 0)
            };
            _providedRadio.Checked += (s, e) => UpdateUI();
            content.Children.Add(_providedRadio);

            var providedDesc = new TextBlock
            {
                Text = "Provide the path to an existing certificate file and its password.",
                FontSize = 11,
                Foreground = System.Windows.Media.Brushes.Gray,
                Margin = new Thickness(20, 5, 0, 10)
            };
            content.Children.Add(providedDesc);

            // Provided certificate input fields (initially disabled)
            var certPathPanel = new StackPanel { Margin = new Thickness(20, 0, 0, 0) };
            var certPathLabel = new TextBlock { Text = "Certificate Path (PFX):", Margin = new Thickness(0, 0, 0, 5), FontWeight = FontWeights.SemiBold };
            _certPathBox = new TextBox { Height = 32, Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, 10) };
            certPathPanel.Children.Add(certPathLabel);
            certPathPanel.Children.Add(_certPathBox);

            var certPasswordLabel = new TextBlock { Text = "Certificate Password:", Margin = new Thickness(0, 0, 0, 5), FontWeight = FontWeights.SemiBold };
            _certPasswordBox = new PasswordBox { Height = 32, Padding = new Thickness(8) };
            certPathPanel.Children.Add(certPasswordLabel);
            certPathPanel.Children.Add(_certPasswordBox);

            content.Children.Add(certPathPanel);

            scrollViewer.Content = content;
            mainGrid.Children.Add(scrollViewer);
            this.Content = mainGrid;

            UpdateUI();
        }

        private void UpdateUI()
        {
            bool isProvidedSelected = _providedRadio?.IsChecked == true;
            if (_certPathBox != null)
                _certPathBox.IsEnabled = isProvidedSelected;
            if (_certPasswordBox != null)
                _certPasswordBox.IsEnabled = isProvidedSelected;
        }

        public override bool Validate()
        {
            if (_generatedCaRadio?.IsChecked == true)
            {
                SelectedCertificateMode = "GeneratedCa";
                return true;
            }
            if (_generatedLeafRadio?.IsChecked == true)
            {
                SelectedCertificateMode = "GeneratedLeaf";
                return true;
            }
            if (_providedRadio?.IsChecked == true)
            {
                SelectedCertificateMode = "Provided";
                ProvidedCertPath = _certPathBox?.Text;
                ProvidedCertPassword = _certPasswordBox?.Password;

                if (string.IsNullOrWhiteSpace(ProvidedCertPath) || string.IsNullOrWhiteSpace(ProvidedCertPassword))
                {
                    MessageBox.Show("Certificate path and password are required when using a provided certificate.", "Validation Error");
                    return false;
                }

                if (!System.IO.File.Exists(ProvidedCertPath))
                {
                    MessageBox.Show("Certificate file not found.", "Validation Error");
                    return false;
                }

                return true;
            }

            return false;
        }
    }
}
