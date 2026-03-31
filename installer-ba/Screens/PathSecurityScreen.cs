using System.Windows;
using System.Windows.Controls;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AdagioMachineAgent.BootstrapperApplication
{
    /// <summary>
    /// Screen for configuring path security settings (executable and path allowlists).
    /// </summary>
    public class PathSecurityScreen : WizardScreen
    {
        private readonly InstallerContext _context;
        private TextBox? _executablePathsBox;
        private TextBox? _writablePathsBox;
        private TextBox? _readablePathsBox;

        public string? AllowedExecutablePaths { get; private set; }
        public string? AllowedWritablePaths { get; private set; }
        public string? AllowedReadablePaths { get; private set; }

        public PathSecurityScreen(InstallerContext context)
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
                Text = "Path Security Configuration",
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 20)
            };
            content.Children.Add(title);

            var description = new TextBlock
            {
                Text = "Define which paths the agent is allowed to access. Use semicolon-separated paths.",
                FontSize = 12,
                Foreground = System.Windows.Media.Brushes.Gray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 20)
            };
            content.Children.Add(description);

            // Executable Paths
            var execLabel = new TextBlock
            {
                Text = "Allowed Executable Paths:",
                Margin = new Thickness(0, 0, 0, 5),
                FontWeight = FontWeights.SemiBold
            };
            content.Children.Add(execLabel);

            _executablePathsBox = new TextBox
            {
                Height = 50,
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 5),
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                Text = string.Join("; ", _context.AllowedExecutablePaths),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            content.Children.Add(_executablePathsBox);

            var execDesc = new TextBlock
            {
                Text = "Paths where the agent can execute programs. Example: C:\\Windows\\System32; C:\\MyCustomTools",
                FontSize = 11,
                Foreground = System.Windows.Media.Brushes.Gray,
                Margin = new Thickness(0, 0, 0, 15)
            };
            content.Children.Add(execDesc);

            // Writable Paths
            var writableLabel = new TextBlock
            {
                Text = "Allowed Writable Paths:",
                Margin = new Thickness(0, 0, 0, 5),
                FontWeight = FontWeights.SemiBold
            };
            content.Children.Add(writableLabel);

            _writablePathsBox = new TextBox
            {
                Height = 50,
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 5),
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                Text = string.Join("; ", _context.AllowedWritablePaths),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            content.Children.Add(_writablePathsBox);

            var writableDesc = new TextBlock
            {
                Text = "Paths where the agent can write files and logs. Example: C:\\Logs; C:\\Temporary",
                FontSize = 11,
                Foreground = System.Windows.Media.Brushes.Gray,
                Margin = new Thickness(0, 0, 0, 15)
            };
            content.Children.Add(writableDesc);

            // Readable Paths
            var readableLabel = new TextBlock
            {
                Text = "Allowed Readable Paths:",
                Margin = new Thickness(0, 0, 0, 5),
                FontWeight = FontWeights.SemiBold
            };
            content.Children.Add(readableLabel);

            _readablePathsBox = new TextBox
            {
                Height = 50,
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 5),
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                Text = string.Join("; ", _context.AllowedReadablePaths),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            content.Children.Add(_readablePathsBox);

            var readableDesc = new TextBlock
            {
                Text = "Paths where the agent can read files and configurations. Example: C:\\Program Files; C:\\Config",
                FontSize = 11,
                Foreground = System.Windows.Media.Brushes.Gray,
                Margin = new Thickness(0, 0, 0, 0)
            };
            content.Children.Add(readableDesc);

            scrollViewer.Content = content;
            mainGrid.Children.Add(scrollViewer);
            this.Content = mainGrid;
        }

        public override bool Validate()
        {
            AllowedExecutablePaths = _executablePathsBox?.Text?.Trim();
            AllowedWritablePaths = _writablePathsBox?.Text?.Trim();
            AllowedReadablePaths = _readablePathsBox?.Text?.Trim();

            var executablePaths = ParsePathList(AllowedExecutablePaths);
            var writablePaths = ParsePathList(AllowedWritablePaths);
            var readablePaths = ParsePathList(AllowedReadablePaths);

            if (executablePaths.Count == 0)
            {
                MessageBox.Show("Allowed executable paths are required.", "Validation Error");
                return false;
            }

            if (writablePaths.Count == 0)
            {
                MessageBox.Show("Allowed writable paths are required.", "Validation Error");
                return false;
            }

            if (readablePaths.Count == 0)
            {
                MessageBox.Show("Allowed readable paths are required.", "Validation Error");
                return false;
            }

            var invalidPaths = executablePaths
                .Concat(writablePaths)
                .Concat(readablePaths)
                .Where(path => !Path.IsPathRooted(path) || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                .Distinct()
                .ToList();

            if (invalidPaths.Count > 0)
            {
                MessageBox.Show(
                    "Invalid or non-absolute path entries found:\n\n" + string.Join("\n", invalidPaths),
                    "Validation Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }

            var missingPaths = executablePaths
                .Concat(writablePaths)
                .Concat(readablePaths)
                .Where(path => !Directory.Exists(path) && !File.Exists(path))
                .Distinct()
                .ToList();

            if (missingPaths.Count > 0)
            {
                var continueResult = MessageBox.Show(
                    "Some paths do not currently exist:\n\n" + string.Join("\n", missingPaths) +
                    "\n\nContinue anyway?",
                    "Path Warning",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (continueResult != MessageBoxResult.Yes)
                {
                    return false;
                }
            }

            _context.AllowedExecutablePaths = executablePaths;
            _context.AllowedWritablePaths = writablePaths;
            _context.AllowedReadablePaths = readablePaths;

            return true;
        }

        private static List<string> ParsePathList(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return new List<string>();
            }

            return text
                .Split(new[] { ';', '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries)
                .Select(path => path.Trim())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(System.StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
