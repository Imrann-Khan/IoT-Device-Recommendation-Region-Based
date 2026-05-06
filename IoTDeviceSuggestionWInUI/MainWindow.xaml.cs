using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using IoTDeviceSuggestionWInUI.Services;
using IoTDeviceSuggestionWInUI.ViewModels;

namespace IoTDeviceSuggestionWInUI
{
    /// <summary>
    /// Main window for the IoT Device Recommendation application.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        public MainViewModel ViewModel { get; private set; }

        public MainWindow()
        {
            // Initialize ViewModel first with default rooms so bindings work
            ViewModel = new MainViewModel();
            
            InitializeComponent();
            
            // Set title
            Title = "IoT Device Recommender";

            // Initialize services asynchronously 
            _ = InitializeServicesAsync();
        }

        private Task InitializeServicesAsync()
        {
            try
            {
                Console.WriteLine("");
                Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
                Console.WriteLine("║                  ISOLATED PYTHON BACKEND INITIALIZATION                      ║");
                Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
                Console.WriteLine("");

                ViewModel.StatusMessage = "Loading isolated backend...";

                var appDataPath = GetAppDataPath();
                var dataPath = Path.Combine(appDataPath, "Data", "Dataset_Cleaned_Final_v2_Unique.json");
                var configPath = Path.Combine(appDataPath, "Data", "hybrid_config.json");
                var regionalUsagePath = Path.Combine(appDataPath, "Data", "combined_by_category.json");

                Console.WriteLine($"[INIT] AppDataPath: {appDataPath}");
                //Console.WriteLine($"[INIT] BackendRoot: {backendRoot}");
                Console.WriteLine($"[INIT] DataPath: {dataPath}");
                Console.WriteLine($"[INIT] ConfigPath: {configPath}");
                Console.WriteLine($"[INIT] RegionalUsagePath: {regionalUsagePath}");
                Console.WriteLine("");

                // Load repository and config (common)
                if (!File.Exists(dataPath))
                {
                    throw new FileNotFoundException($"Device data file not found: {dataPath}");
                }

                if (!File.Exists(configPath))
                {
                    throw new FileNotFoundException($"Config file not found: {configPath}");
                }

                Console.WriteLine("[INIT] Loading device repository and configuration...");
                var repository = new DeviceRepository(dataPath, configPath);
                var devices = repository.LoadDevices();
                var config = repository.LoadConfig();
                Console.WriteLine($"[INIT] ✓ Loaded {devices.Count} devices");
                Console.WriteLine($"[INIT] ✓ Config loaded (top_n={config.TopN}, retrieval_top_n={config.RetrievalTopN})");

                RegionalUsageService? regionalUsageService = null;
                if (File.Exists(regionalUsagePath))
                {
                    try
                    {
                        regionalUsageService = new RegionalUsageService(
                            regionalUsagePath,
                            config.DefaultRegion,
                            config.RegionalFallbackScore);
                        Console.WriteLine("[INIT] ✓ Regional usage service loaded");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[WARN] Failed to load regional usage service: {ex.Message}");
                    }
                }

                // Initialize Pure ONNX Bi-Encoder + Cross-Encoder (no LLM)
                Console.WriteLine("[INIT] Initializing Pure ONNX Bi-Encoder + Cross-Encoder pipeline...");

                var biEncoderPath = Path.Combine(GetAppDataPath(), "Models", "Onnx", "sentence Transformer", "sentence_transformer.onnx");
                var crossEncoderPath = Path.Combine(GetAppDataPath(), "Models", "Onnx", "CrossEncoder", "model_O4.onnx");

                if (!File.Exists(biEncoderPath))
                {
                    throw new FileNotFoundException($"Bi-encoder ONNX model not found: {biEncoderPath}");
                }
                if (!File.Exists(crossEncoderPath))
                {
                    throw new FileNotFoundException($"Cross-encoder ONNX model not found: {crossEncoderPath}");
                }

                IRecommendationService recommendationService = new OnnxBiCrossRecommendationService(
                    dataPath,
                    biEncoderPath,
                    crossEncoderPath,
                    configPath,
                    regionalUsagePath
                );
                Console.WriteLine("[INIT] ✓ Pure ONNX Bi-Encoder + Cross-Encoder service created");

                var weatherService = new WeatherService();
                Console.WriteLine("[INIT] ✓ Weather service created");
                Console.WriteLine("");

                DispatcherQueue.TryEnqueue(() =>
                {
                    ViewModel = new MainViewModel(recommendationService, repository, weatherService);
                    Bindings.Update();
                    ViewModel.StatusMessage = "Ready - Pure ONNX Bi-Encoder + Cross-Encoder";

                    Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
                    Console.WriteLine("║                    INITIALIZATION COMPLETE                                   ║");
                    Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
                    Console.WriteLine("");
                    Console.WriteLine("Application Mode: Pure ONNX Bi-Encoder + Cross-Encoder (LLM removed)");
                    Console.WriteLine("");
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Initialization failed: {ex.Message}");
                Console.WriteLine($"[ERROR] Stack trace: {ex.StackTrace}");
                DispatcherQueue.TryEnqueue(() =>
                {
                    ViewModel.StatusMessage = $"Error: {ex.Message}";
                });
            }

            return Task.CompletedTask;
        }

        private string GetAppDataPath()
        {
            // Get the application's base directory
            var appPath = AppContext.BaseDirectory;
            return appPath;
        }
    }
}