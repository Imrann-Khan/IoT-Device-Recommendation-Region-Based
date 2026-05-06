using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace IoTDeviceSuggestionWInUI
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private Window? _window;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool AllocConsole();

        [DllImport("kernel32.dll")]
        static extern bool FreeConsole();

        /// <summary>
        /// Initializes the singleton application object.
        /// </summary>
        public App()
        {
            // Allocate a console window for debug output
            AllocConsole();
            Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║                    IoT DEVICE RECOMMENDER - DEBUG CONSOLE                    ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            Console.WriteLine("");
            Console.WriteLine("[APP] Application starting...");
            Console.WriteLine($"[APP] Working Directory: {Environment.CurrentDirectory}");
            Console.WriteLine($"[APP] Base Directory: {AppContext.BaseDirectory}");
            Console.WriteLine("");
            
            try
            {
                InitializeComponent();
                Console.WriteLine("[APP] InitializeComponent() completed");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[APP ERROR] InitializeComponent failed: {ex.Message}");
                Console.WriteLine($"[APP ERROR] Stack: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            Console.WriteLine("[APP] OnLaunched() called");
            
            try
            {
                _window = new MainWindow();
                Console.WriteLine("[APP] MainWindow created");
                
                _window.Activate();
                Console.WriteLine("[APP] MainWindow activated");
                Console.WriteLine("[APP] Application started successfully!");
                Console.WriteLine("");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[APP ERROR] OnLaunched failed: {ex.Message}");
                Console.WriteLine($"[APP ERROR] Stack: {ex.StackTrace}");
                
                // Show a message box with the error
                var dialog = new Windows.UI.Popups.MessageDialog(
                    $"Error starting application: {ex.Message}\n\nStack: {ex.StackTrace}",
                    "Startup Error"
                );
                dialog.ShowAsync().AsTask().Wait();
            }
        }
    }
}