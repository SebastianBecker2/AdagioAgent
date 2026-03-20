using System;
using System.Windows;

namespace AdagioMachineAgent.BootstrapperApplication
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            
            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                MessageBox.Show($"Unhandled exception: {args.ExceptionObject}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Environment.Exit(1);
            };
        }
    }
}
