using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace AdagioMachineAgent.BootstrapperApplication
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// Manages wizard screen container and navigation.
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly InstallerContext _context;
        private readonly List<WizardScreen> _screens = new();
        private int _currentScreenIndex = 0;

        public MainWindow()
        {
            InitializeComponent();
            _context = new InstallerContext();

            // Initialize wizard screens
            _screens.Add(new WelcomeScreen());
            _screens.Add(new CertificateModeScreen(_context));
            _screens.Add(new CertificateExportScreen(_context));
            _screens.Add(new ApiKeyModeScreen(_context));
            _screens.Add(new NetworkConfigurationScreen(_context));
            _screens.Add(new PathSecurityScreen(_context));
            _screens.Add(new SummaryScreen(_context));

            // Start with first screen
            ShowCurrentScreen();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateNavigationButtons();
        }

        private void ShowCurrentScreen()
        {
            if (_currentScreenIndex >= 0 && _currentScreenIndex < _screens.Count)
            {
                var screen = _screens[_currentScreenIndex];
                screen.OnBeforeShown();
                ScreenContainer.Content = screen;
                UpdateNavigationButtons();
            }
        }

        private void UpdateNavigationButtons()
        {
            PreviousButton.IsEnabled = _currentScreenIndex > 0;
            PreviousButton.Visibility = _currentScreenIndex > 0 ? Visibility.Visible : Visibility.Collapsed;

            // Last screen shows "Install" instead of "Next"
            if (_currentScreenIndex == _screens.Count - 1)
            {
                NextButton.Content = "Install";
            }
            else
            {
                NextButton.Content = "Next →";
            }

            // On welcome screen, show "Start" instead of "Next"
            if (_currentScreenIndex == 0)
            {
                NextButton.Content = "Start";
            }
        }

        private void PreviousButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentScreenIndex > 0)
            {
                _currentScreenIndex--;
                ShowCurrentScreen();
            }
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            // Validate current screen
            var currentScreen = _screens[_currentScreenIndex];
            if (!currentScreen.Validate())
            {
                MessageBox.Show("Please check your input and try again.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_currentScreenIndex < _screens.Count - 1)
            {
                _currentScreenIndex++;
                ShowCurrentScreen();
            }
            else
            {
                // Last screen - proceed with installation
                ExecuteInstallation();
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Cancel the installation?", "Confirm Cancel", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                Environment.Exit(1);
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // Allow normal close
        }

        private void ExecuteInstallation()
        {
            var correlationId = Guid.NewGuid().ToString("N");

            try
            {
                // Collect configuration from all screens
                var config = new InstallerResponseFile
                {
                    Timestamp = DateTime.UtcNow.ToString("O"),
                    Security = GetSecurityConfiguration(),
                    Network = GetNetworkConfiguration(),
                    AgentOptions = GetAgentOptionsConfiguration(),
                    Discovery = _context.Discovery
                };

                // Generate response file
                string responseFilePath = GenerateResponseFile(config);

                // Write response file path to a well-known location so bundle can read it
                WriteResponseFilePathMarker(responseFilePath);

                // Exit with success (0) so bundle proceeds with MSI execution
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                var diagnosticsPath = WriteInstallerFailureDiagnostics(ex, correlationId);
                var prompt =
                    $"Installation setup failed. Correlation ID: {correlationId}\n\n" +
                    $"Diagnostics file: {diagnosticsPath}\n\n" +
                    "Yes = Retry\nNo = Open diagnostics folder\nCancel = Exit installer";

                var result = MessageBox.Show(
                    prompt,
                    "Installation Setup Error",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Error);

                if (result == MessageBoxResult.Yes)
                {
                    ExecuteInstallation();
                    return;
                }

                if (result == MessageBoxResult.No)
                {
                    OpenDiagnosticsFolder(diagnosticsPath);
                }

                Environment.Exit(1);
            }
        }

        private SecurityOptions GetSecurityConfiguration()
        {
            return new SecurityOptions
            {
                CertificateMode = _context.CertificateMode,
                ProvidedCertificatePath = _context.ProvidedCertificatePath,
                ProvidedCertificatePassword = _context.ProvidedCertificatePassword,
                CaCertificatePemPath = _context.CaCertificatePemPath,
                CaCertificatePfxPath = _context.CaCertificatePfxPath,
                ApiKeyMode = _context.ApiKeyMode,
                ProvidedApiKey = _context.ProvidedApiKey,
                RequireHttps = _context.RequireHttps,
                RequireApiKey = _context.RequireApiKey
            };
        }

        private NetworkOptions GetNetworkConfiguration()
        {
            return new NetworkOptions
            {
                Urls = _context.Urls,
                AllowedHosts = _context.AllowedHosts
            };
        }

        private AgentOptions GetAgentOptionsConfiguration()
        {
            return new AgentOptions
            {
                AllowedExecutablePaths = _context.AllowedExecutablePaths,
                AllowedWritablePaths = _context.AllowedWritablePaths,
                AllowedReadablePaths = _context.AllowedReadablePaths
            };
        }

        private string GenerateResponseFile(InstallerResponseFile config)
        {
            string responseDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AdagioInstaller");
            System.IO.Directory.CreateDirectory(responseDir);
            string responseFile = System.IO.Path.Combine(responseDir, $"response-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");

            var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
            string json = System.Text.Json.JsonSerializer.Serialize(config, options);
            System.IO.File.WriteAllText(responseFile, json);

            return responseFile;
        }

        private void WriteResponseFilePathMarker(string responseFilePath)
        {
            // Write to a marker file that the Burn bundle can read via environment variable or temp location
            string markerDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AdagioInstaller");
            System.IO.Directory.CreateDirectory(markerDir);
            string markerFile = System.IO.Path.Combine(markerDir, "response-path.txt");
            System.IO.File.WriteAllText(markerFile, responseFilePath);
        }

        private static string WriteInstallerFailureDiagnostics(Exception ex, string correlationId)
        {
            string diagnosticsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "AdagioMachineAgent");

            string fallbackDir = Path.Combine(Path.GetTempPath(), "AdagioInstaller");
            string diagnosticsPath = Path.Combine(diagnosticsDir, "installer-ba-failure.json");

            var payload = new
            {
                generatedAtUtc = DateTimeOffset.UtcNow.ToString("u"),
                correlationId,
                error = ex.Message,
                exceptionType = ex.GetType().FullName,
                stackTrace = ex.StackTrace,
            };

            try
            {
                Directory.CreateDirectory(diagnosticsDir);
                File.WriteAllText(diagnosticsPath, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
                return diagnosticsPath;
            }
            catch
            {
                Directory.CreateDirectory(fallbackDir);
                diagnosticsPath = Path.Combine(fallbackDir, "installer-ba-failure.json");
                File.WriteAllText(diagnosticsPath, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
                return diagnosticsPath;
            }
        }

        private static void OpenDiagnosticsFolder(string diagnosticsPath)
        {
            try
            {
                var folder = Path.GetDirectoryName(diagnosticsPath);
                if (string.IsNullOrWhiteSpace(folder))
                {
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = folder,
                    UseShellExecute = true,
                });
            }
            catch
            {
                // Best effort only.
            }
        }
    }
}
