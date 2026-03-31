using System.Windows;
using System.Windows.Controls;

namespace AdagioMachineAgent.BootstrapperApplication
{
    /// <summary>
    /// Base class for wizard screens.
    /// </summary>
    public abstract class WizardScreen : UserControl
    {
        /// <summary>
        /// Called by the host window when the screen becomes active.
        /// </summary>
        public virtual void OnBeforeShown()
        {
        }

        /// <summary>
        /// Validates the screen's input and returns true if valid.
        /// </summary>
        public abstract bool Validate();
    }

    /// <summary>
    /// Welcome screen introducing the installer.
    /// </summary>
    public class WelcomeScreen : WizardScreen
    {
        public WelcomeScreen()
        {
            var grid = new Grid { Background = System.Windows.Media.Brushes.White, Margin = new Thickness(40) };
            
            var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            
            var title = new TextBlock
            {
                Text = "Adagio Machine Agent Installer",
                FontSize = 28,
                FontWeight = FontWeights.Bold,
                Foreground = System.Windows.Media.Brushes.Black,
                Margin = new Thickness(0, 0, 0, 20)
            };
            
            var description = new TextBlock
            {
                Text = "This wizard will guide you through the installation and configuration of the Adagio Machine Agent.\n\n" +
                       "You'll configure:\n" +
                       "• Security settings (certificate and API key modes)\n" +
                       "• Network configuration (service URLs and allowed hosts)\n" +
                       "• Path security settings (executable and path allowlists)\n\n" +
                       "Click 'Start' to begin.",
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Foreground = System.Windows.Media.Brushes.Gray
            };
            
            content.Children.Add(title);
            content.Children.Add(description);
            grid.Children.Add(content);
            this.Content = grid;
        }

        public override bool Validate() => true;
    }
}
