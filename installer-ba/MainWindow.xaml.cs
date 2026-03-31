using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
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
        private readonly InstallProgressScreen _progressScreen;
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
            _progressScreen = new InstallProgressScreen();
            _screens.Add(_progressScreen);

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
            // Hide all navigation controls while the install is running or complete.
            if (_currentScreenIndex == _screens.IndexOf(_progressScreen))
            {
                PreviousButton.Visibility = Visibility.Collapsed;
                NextButton.Visibility = Visibility.Collapsed;
                CancelButton.Visibility = Visibility.Collapsed;
                return;
            }

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
                // Collect configuration from all screens.
                var config = new InstallerResponseFile
                {
                    Timestamp = DateTime.UtcNow.ToString("O"),
                    Security = GetSecurityConfiguration(),
                    Network = GetNetworkConfiguration(),
                    AgentOptions = GetAgentOptionsConfiguration(),
                    Discovery = _context.Discovery
                };

                // Persist the response file so the bundle can pass it to the MSI.
                string responseFilePath = GenerateResponseFile(config);

                // Navigate to the installation progress screen.
                _currentScreenIndex = _screens.IndexOf(_progressScreen);
                ShowCurrentScreen();

                // Launch the Burn bundle quietly on a background thread while the
                // progress screen remains visible and streams the log output.
                _ = LaunchBundleAsync(responseFilePath, correlationId);
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

        // -------------------------------------------------------------------------
        // Bundle launch helpers
        // -------------------------------------------------------------------------

        /// <summary>
        /// Locates the Burn bundle EXE that is expected to be co-deployed in the
        /// same directory as this wizard.
        /// </summary>
        private static string? FindBundleExe()
        {
            var candidate = Path.Combine(AppContext.BaseDirectory, "AdagioMachineAgent.Bundle.exe");
            return File.Exists(candidate) ? candidate : null;
        }

        /// <summary>
        /// Launches the Burn bundle in quiet mode, streams relevant log lines to the
        /// progress screen, and reports the final success/failure state.
        /// </summary>
        private async Task LaunchBundleAsync(string responseFilePath, string correlationId)
        {
            string logDir = Path.Combine(Path.GetTempPath(), "AdagioInstaller");
            Directory.CreateDirectory(logDir);
            string logPath = Path.Combine(logDir, $"bundle-{DateTime.UtcNow:yyyyMMddHHmmss}.log");

            _progressScreen.SetStatus("Locating installer bundle\u2026", logPath);
            _progressScreen.AppendLog($"[{DateTime.Now:HH:mm:ss}] Correlation ID : {correlationId}");
            _progressScreen.AppendLog($"[{DateTime.Now:HH:mm:ss}] Response file  : {responseFilePath}");

            string? bundlePath = FindBundleExe();
            if (bundlePath == null)
            {
                _progressScreen.AppendLog($"[ERROR] AdagioMachineAgent.Bundle.exe not found in: {AppContext.BaseDirectory}");
                _progressScreen.AppendLog("[ERROR] Ensure the wizard and bundle are deployed in the same folder.");
                _progressScreen.SetStatus("Installation failed: bundle not found.", logPath);
                _progressScreen.SetComplete(false, logPath);
                return;
            }

            _progressScreen.AppendLog($"[{DateTime.Now:HH:mm:ss}] Bundle         : {bundlePath}");
            _progressScreen.SetStatus("Running installation\u2026");

            try
            {
                // Run the bundle silently. The wizard is already elevated (requireAdministrator
                // manifest) so no further UAC prompt is needed for the child process.
                var psi = new ProcessStartInfo
                {
                    FileName = bundlePath,
                    // /install  – install mode
                    // /quiet    – no UI from WixStdBA
                    // /log      – write Burn log to known path
                    // ADAGIO_RESPONSE_FILE_PATH – overridable bundle variable consumed by MSI
                    Arguments = $"/install /quiet /log \"{logPath}\" ADAGIO_RESPONSE_FILE_PATH=\"{responseFilePath}\"",
                    UseShellExecute = true
                };

                var process = Process.Start(psi);
                if (process == null)
                {
                    _progressScreen.AppendLog("[ERROR] Failed to start the installer process.");
                    _progressScreen.SetComplete(false, logPath);
                    return;
                }

                long lastLogPosition = 0;

                while (!process.HasExited)
                {
                    await Task.Delay(1500).ConfigureAwait(false);
                    lastLogPosition = TailBurnLog(logPath, lastLogPosition);
                }

                // Drain any remaining log content written after the process exits.
                TailBurnLog(logPath, lastLogPosition);

                int exitCode = process.ExitCode;
                _progressScreen.AppendLog($"[{DateTime.Now:HH:mm:ss}] Bundle exited with code {exitCode}.");
                bool success = exitCode == 0;
                _progressScreen.SetStatus(
                    success ? "Installation complete." : $"Installation failed (exit code {exitCode}).",
                    logPath);
                _progressScreen.SetComplete(success, logPath);
            }
            catch (Exception ex)
            {
                _progressScreen.AppendLog($"[ERROR] {ex.Message}");
                _progressScreen.SetComplete(false, logPath);
            }
        }

        /// <summary>
        /// Reads new content appended to the Burn log file since <paramref name="position"/>
        /// and forwards relevant lines to the progress screen.
        /// Returns the updated file position.
        /// </summary>
        private long TailBurnLog(string logPath, long position)
        {
            if (!File.Exists(logPath)) return position;

            try
            {
                using var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (fs.Length <= position) return position;

                fs.Seek(position, SeekOrigin.Begin);

                // Burn writes logs in UTF-16 LE on Windows.
                using var reader = new StreamReader(fs, Encoding.Unicode, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    var t = line.Trim();
                    if (t.Length == 0) continue;

                    // Surface lines that carry meaningful progress or error signals.
                    if (t.Contains("Error", StringComparison.OrdinalIgnoreCase)
                        || t.Contains("Apply", StringComparison.OrdinalIgnoreCase)
                        || t.Contains("Package", StringComparison.OrdinalIgnoreCase)
                        || t.Contains("Progress", StringComparison.OrdinalIgnoreCase)
                        || t.Contains("Install", StringComparison.OrdinalIgnoreCase)
                        || t.Contains("Rollback", StringComparison.OrdinalIgnoreCase)
                        || t.Contains("Complete", StringComparison.OrdinalIgnoreCase))
                    {
                        _progressScreen.AppendLog(t);
                    }
                }

                return fs.Position;
            }
            catch
            {
                return position;
            }
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
