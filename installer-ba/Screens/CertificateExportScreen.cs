using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace AdagioMachineAgent.BootstrapperApplication
{
    /// <summary>
    /// Screen for configuring certificate artifact export locations.
    /// </summary>
    public class CertificateExportScreen : WizardScreen
    {
        private readonly InstallerContext _context;
        private TextBox? _caPemPathBox;
        private TextBox? _caPfxPathBox;
        private TextBlock? _modeNotice;

        public CertificateExportScreen(InstallerContext context)
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

            var content = new StackPanel();

            content.Children.Add(new TextBlock
            {
                Text = "Certificate Export Configuration",
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 20)
            });

            content.Children.Add(new TextBlock
            {
                Text = "Set where generated TLS trust artifacts should be written for client onboarding and backup.",
                FontSize = 12,
                Foreground = System.Windows.Media.Brushes.Gray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 16)
            });

            _modeNotice = new TextBlock
            {
                Margin = new Thickness(0, 0, 0, 16),
                FontSize = 11,
                Foreground = System.Windows.Media.Brushes.DarkSlateGray,
                TextWrapping = TextWrapping.Wrap
            };
            content.Children.Add(_modeNotice);

            content.Children.Add(new TextBlock
            {
                Text = "CA PEM export path:",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 5)
            });

            var pemRow = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            pemRow.ColumnDefinitions.Add(new ColumnDefinition());
            pemRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            pemRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });

            _caPemPathBox = new TextBox
            {
                Height = 32,
                Padding = new Thickness(8),
                Text = _context.CaCertificatePemPath
            };
            Grid.SetColumn(_caPemPathBox, 0);
            pemRow.Children.Add(_caPemPathBox);

            var browsePemButton = new Button
            {
                Content = "Browse",
                Margin = new Thickness(8, 0, 0, 0),
                Height = 32
            };
            browsePemButton.Click += BrowsePemButton_Click;
            Grid.SetColumn(browsePemButton, 1);
            pemRow.Children.Add(browsePemButton);

            var copyPemButton = new Button
            {
                Content = "Copy",
                Margin = new Thickness(8, 0, 0, 0),
                Height = 32
            };
            copyPemButton.Click += (_, _) => CopyPathToClipboard(_caPemPathBox?.Text, "CA PEM path copied.");
            Grid.SetColumn(copyPemButton, 2);
            pemRow.Children.Add(copyPemButton);
            content.Children.Add(pemRow);

            content.Children.Add(new TextBlock
            {
                Text = "CA PFX export path:",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 5)
            });

            var pfxRow = new Grid();
            pfxRow.ColumnDefinitions.Add(new ColumnDefinition());
            pfxRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            pfxRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });

            _caPfxPathBox = new TextBox
            {
                Height = 32,
                Padding = new Thickness(8),
                Text = _context.CaCertificatePfxPath
            };
            Grid.SetColumn(_caPfxPathBox, 0);
            pfxRow.Children.Add(_caPfxPathBox);

            var browsePfxButton = new Button
            {
                Content = "Browse",
                Margin = new Thickness(8, 0, 0, 0),
                Height = 32
            };
            browsePfxButton.Click += BrowsePfxButton_Click;
            Grid.SetColumn(browsePfxButton, 1);
            pfxRow.Children.Add(browsePfxButton);

            var copyPfxButton = new Button
            {
                Content = "Copy",
                Margin = new Thickness(8, 0, 0, 0),
                Height = 32
            };
            copyPfxButton.Click += (_, _) => CopyPathToClipboard(_caPfxPathBox?.Text, "CA PFX path copied.");
            Grid.SetColumn(copyPfxButton, 2);
            pfxRow.Children.Add(copyPfxButton);
            content.Children.Add(pfxRow);

            mainGrid.Children.Add(content);
            Content = mainGrid;

            UpdateModeNotice();
        }

        public override void OnBeforeShown()
        {
            UpdateModeNotice();
        }

        public override bool Validate()
        {
            var caPemPath = _caPemPathBox?.Text?.Trim();
            var caPfxPath = _caPfxPathBox?.Text?.Trim();

            if (string.IsNullOrWhiteSpace(caPemPath) || string.IsNullOrWhiteSpace(caPfxPath))
            {
                MessageBox.Show("Both CA export paths are required.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (!Path.IsPathRooted(caPemPath) || !Path.IsPathRooted(caPfxPath))
            {
                MessageBox.Show("Certificate export paths must be absolute paths.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            _context.CaCertificatePemPath = caPemPath;
            _context.CaCertificatePfxPath = caPfxPath;
            return true;
        }

        private void UpdateModeNotice()
        {
            var generatedMode = _context.CertificateMode == "GeneratedCa" || _context.CertificateMode == "GeneratedLeaf";
            if (_modeNotice != null)
            {
                _modeNotice.Text = generatedMode
                    ? "Current certificate mode generates new certificate material. These paths will be used for exported trust artifacts."
                    : "Current certificate mode uses a provided certificate. Export paths are optional but retained for future reruns.";
            }

            if (_caPemPathBox != null)
            {
                _caPemPathBox.IsEnabled = true;
            }

            if (_caPfxPathBox != null)
            {
                _caPfxPathBox.IsEnabled = true;
            }
        }

        private void BrowsePemButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "PEM files (*.pem)|*.pem|All files (*.*)|*.*",
                FileName = "agent-ca.pem",
                OverwritePrompt = false
            };

            if (dialog.ShowDialog() == true && _caPemPathBox != null)
            {
                _caPemPathBox.Text = dialog.FileName;
            }
        }

        private void BrowsePfxButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "PFX files (*.pfx)|*.pfx|All files (*.*)|*.*",
                FileName = "agent-ca.pfx",
                OverwritePrompt = false
            };

            if (dialog.ShowDialog() == true && _caPfxPathBox != null)
            {
                _caPfxPathBox.Text = dialog.FileName;
            }
        }

        private static void CopyPathToClipboard(string? path, string confirmation)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                MessageBox.Show("No path value to copy.", "Copy Path", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Clipboard.SetText(path);
            MessageBox.Show(confirmation, "Copy Path", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
