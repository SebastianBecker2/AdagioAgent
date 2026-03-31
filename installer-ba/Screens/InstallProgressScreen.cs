using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AdagioMachineAgent.BootstrapperApplication
{
    /// <summary>
    /// Final wizard screen that drives the Burn bundle in quiet mode and displays live
    /// installation progress from the bundle log file.
    /// </summary>
    public class InstallProgressScreen : WizardScreen
    {
        private TextBlock? _statusLabel;
        private ProgressBar? _progressBar;
        private TextBox? _logBox;
        private Button? _openLogButton;
        private Button? _exitButton;

        private string? _logPath;
        private bool _completed;

        public InstallProgressScreen()
        {
            InitializeUI();
        }

        // -------------------------------------------------------------------------
        // UI
        // -------------------------------------------------------------------------

        private void InitializeUI()
        {
            var grid = new Grid
            {
                Background = Brushes.White,
                Margin = new Thickness(40, 32, 40, 40)
            };

            var content = new StackPanel();

            var title = new TextBlock
            {
                Text = "Installing Adagio Machine Agent",
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.Black,
                Margin = new Thickness(0, 0, 0, 16)
            };
            content.Children.Add(title);

            _statusLabel = new TextBlock
            {
                Text = "Preparing installation\u2026",
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                Margin = new Thickness(0, 0, 0, 12)
            };
            content.Children.Add(_statusLabel);

            _progressBar = new ProgressBar
            {
                IsIndeterminate = true,
                Height = 18,
                Margin = new Thickness(0, 0, 0, 20)
            };
            content.Children.Add(_progressBar);

            var logLabel = new TextBlock
            {
                Text = "Installation log:",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(120, 120, 120)),
                Margin = new Thickness(0, 0, 0, 4)
            };
            content.Children.Add(logLabel);

            _logBox = new TextBox
            {
                IsReadOnly = true,
                Height = 200,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 16),
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap
            };
            content.Children.Add(_logBox);

            var buttonRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 4, 0, 0)
            };

            _openLogButton = new Button
            {
                Content = "Open Log File",
                Width = 120,
                Height = 36,
                Margin = new Thickness(0, 0, 10, 0),
                IsEnabled = false,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0, 102, 204)),
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(204, 204, 204))
            };
            _openLogButton.Click += OpenLogButton_Click;
            buttonRow.Children.Add(_openLogButton);

            _exitButton = new Button
            {
                Content = "Exit",
                Width = 100,
                Height = 36,
                Visibility = Visibility.Collapsed,
                FontSize = 12,
                Background = new SolidColorBrush(Color.FromRgb(0, 102, 204)),
                Foreground = Brushes.White
            };
            _exitButton.Click += ExitButton_Click;
            buttonRow.Children.Add(_exitButton);

            content.Children.Add(buttonRow);
            grid.Children.Add(content);
            Content = grid;
        }

        // -------------------------------------------------------------------------
        // Public API called by MainWindow during the install task
        // -------------------------------------------------------------------------

        /// <summary>Updates the status line and, when first supplied, enables Open Log.</summary>
        public void SetStatus(string status, string? logPath = null)
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (_statusLabel != null)
                    _statusLabel.Text = status;

                if (logPath != null && _logPath == null)
                {
                    _logPath = logPath;
                    if (_openLogButton != null)
                        _openLogButton.IsEnabled = true;
                }
            });
        }

        /// <summary>Appends a line of text to the log output box.</summary>
        public void AppendLog(string line)
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (_logBox == null) return;
                _logBox.AppendText(line + "\n");
                _logBox.ScrollToEnd();
            });
        }

        /// <summary>Marks the install as finished with success or failure styling.</summary>
        public void SetComplete(bool success, string? logPath = null)
        {
            Dispatcher.InvokeAsync(() =>
            {
                _completed = true;
                if (logPath != null) _logPath = logPath;

                if (_progressBar != null)
                {
                    _progressBar.IsIndeterminate = false;
                    _progressBar.Value = 100;
                    _progressBar.Foreground = success
                        ? new SolidColorBrush(Color.FromRgb(56, 142, 60))
                        : new SolidColorBrush(Color.FromRgb(211, 47, 47));
                }

                if (_statusLabel != null)
                {
                    _statusLabel.Text = success
                        ? "Installation completed successfully."
                        : "Installation failed. Review the log for details.";
                    _statusLabel.Foreground = success
                        ? new SolidColorBrush(Color.FromRgb(56, 142, 60))
                        : new SolidColorBrush(Color.FromRgb(211, 47, 47));
                }

                if (_openLogButton != null)
                    _openLogButton.IsEnabled = _logPath != null;

                if (_exitButton != null)
                {
                    _exitButton.Visibility = Visibility.Visible;
                    _exitButton.Content = success ? "Exit" : "Exit (Failed)";
                    _exitButton.Background = success
                        ? new SolidColorBrush(Color.FromRgb(56, 142, 60))
                        : new SolidColorBrush(Color.FromRgb(211, 47, 47));
                }
            });
        }

        // -------------------------------------------------------------------------
        // WizardScreen
        // -------------------------------------------------------------------------

        /// <summary>
        /// Returns true only after the install has finished so the containing window
        /// cannot navigate away mid-install.
        /// </summary>
        public override bool Validate() => _completed;

        // -------------------------------------------------------------------------
        // Event handlers
        // -------------------------------------------------------------------------

        private void OpenLogButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(_logPath))
                    Process.Start(new ProcessStartInfo { FileName = _logPath, UseShellExecute = true });
            }
            catch { /* best-effort */ }
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown(_completed ? 0 : 1);
        }
    }
}
