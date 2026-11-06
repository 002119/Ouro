using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Ouro.Models;
using Ouro.Services.Audio;
using Ouro.Services.Capture;
using Ouro.Services.Configuration;
using Ouro.Services.Export;
using Ouro.Services.Input;
using Ouro.Services.Orchestration;
using Ouro.Utils;
using Ouro.ViewModels;
using Ouro.Views;

namespace Ouro
{
    /// <summary>
    /// Application entry point configuring Dependency Injection (DI) and lifecycle management.
    /// Manages boot orchestration, configuration options, global hook subscriptions, and graceful unmanaged teardown.
    /// </summary>
    public partial class App : Application
    {
        public static IServiceProvider ServiceProvider { get; private set; } = null!;
        public static IConfiguration Configuration { get; private set; } = null!;

        public App()
        {
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                var ex = args.ExceptionObject as Exception;
                MessageBox.Show($"Error fatal en Ouro:\n\n{ex?.Message}\n\n{ex?.StackTrace}", "Ouro - Error Fatal", MessageBoxButton.OK, MessageBoxImage.Error);
            };

            DispatcherUnhandledException += (s, args) =>
            {
                MessageBox.Show($"Error de interfaz en Ouro:\n\n{args.Exception?.Message}\n\n{args.Exception?.StackTrace}", "Ouro - Error", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };

            // 0. Discover native FFmpeg libraries
            FFmpegBootstrapper.Initialize();

            // 1. Build configuration from appsettings.json and persistent %APPDATA% settings
            string appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ouro");
            Directory.CreateDirectory(appDataDir);
            string userSettingsFile = Path.Combine(appDataDir, "settings.json");

            var builder = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

            if (File.Exists(userSettingsFile))
            {
                builder.AddJsonFile(userSettingsFile, optional: true, reloadOnChange: true);
            }

            Configuration = builder.Build();

            // 2. Setup dependency injection container
            var services = new ServiceCollection();
            ConfigureServices(services, Configuration);
            ServiceProvider = services.BuildServiceProvider();
        }

        private static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            // Configuration & Options Pattern
            services.AddSingleton<IConfiguration>(configuration);
            services.Configure<OuroSettings>(configuration.GetSection(OuroSettings.SectionName));
            services.AddSingleton<ISettingsManager, SettingsManager>();

            // Core Replay Buffers & Capture Pipeline (Singletons)
            services.AddSingleton<IRamReplayBuffer>(sp =>
                new RamReplayBuffer(sp.GetRequiredService<IOptions<OuroSettings>>()));

            services.AddSingleton<CaptureEngine>();

            services.AddSingleton<IVideoEncoder>(sp =>
            {
                var engine = sp.GetRequiredService<CaptureEngine>();
                var replayBuffer = sp.GetRequiredService<IRamReplayBuffer>();
                return new NvencEncoder(engine.Device!, engine.Context!, replayBuffer);
            });

            // Dual-track Audio Service (WASAPI Loopback + Mic)
            services.AddSingleton<IAudioService, WasapiAudioService>();

            // Global System Hotkey Service
            services.AddSingleton<IInputService, HotkeyService>();

            // MP4 Disk Persistence Multiplexer
            services.AddSingleton<IMp4Muxer, Mp4Muxer>();

            // Central Brain / Orchestrator
            services.AddSingleton<IClipOrchestrator>(sp =>
                new ClipOrchestrator(
                    sp.GetRequiredService<IInputService>(),
                    sp.GetRequiredService<IRamReplayBuffer>(),
                    sp.GetRequiredService<IAudioService>(),
                    sp.GetRequiredService<IMp4Muxer>(),
                    sp.GetRequiredService<IOptions<OuroSettings>>(),
                    sp.GetRequiredService<CaptureEngine>(),
                    sp.GetRequiredService<IVideoEncoder>()));

            // Social & Discord Webhook Integration
            services.AddSingleton<Ouro.Services.Social.IDiscordService, Ouro.Services.Social.DiscordWebhookService>();

            // Presentation Layer (ViewModels & Views)
            services.AddSingleton<MainViewModel>(sp =>
                new MainViewModel(
                    sp.GetRequiredService<IClipOrchestrator>(),
                    sp.GetRequiredService<ISettingsManager>(),
                    sp.GetRequiredService<IRamReplayBuffer>(),
                    sp.GetRequiredService<CaptureEngine>(),
                    sp.GetRequiredService<IInputService>(),
                    sp.GetRequiredService<IAudioService>(),
                    sp.GetRequiredService<Ouro.Services.Social.IDiscordService>()));

            // Licensing & HWID Verification Engine
            services.AddSingleton<Ouro.Services.Licensing.LicenseManager>();

            services.AddSingleton<MainWindow>();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 0. Cryptographic HWID License Verification
            var licenseManager = ServiceProvider.GetRequiredService<Ouro.Services.Licensing.LicenseManager>();
            if (!licenseManager.IsLicensed(out _))
            {
                var activationWindow = new Ouro.Views.ActivationWindow(licenseManager);
                bool? unlocked = activationWindow.ShowDialog();
                if (unlocked != true)
                {
                    Shutdown();
                    return;
                }
            }

            var settings = ServiceProvider.GetRequiredService<IOptions<OuroSettings>>().Value;

            // 1. Configure user-defined hotkey (defaults to F12)
            try
            {
                var inputService = ServiceProvider.GetRequiredService<IInputService>();
                string hotkey = !string.IsNullOrWhiteSpace(settings.SaveHotkey) ? settings.SaveHotkey : "F12";
                inputService.RegisterSaveHotkey(hotkey);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to register configured hotkey ({settings.SaveHotkey}): {ex.Message}");
            }

            bool startMinimized = e.Args != null && Array.Exists(e.Args, a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase) || a.Equals("--tray", StringComparison.OrdinalIgnoreCase));

            // 2. Resolve MainWindow, bind MainViewModel, and present UI
            var mainWindow = ServiceProvider.GetRequiredService<MainWindow>();
            mainWindow.DataContext = ServiceProvider.GetRequiredService<MainViewModel>();
            if (!startMinimized)
            {
                mainWindow.Show();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // 1. Unhook orchestrator and input triggers
            try
            {
                var orchestrator = ServiceProvider.GetService<IClipOrchestrator>();
                orchestrator?.Stop();
                orchestrator?.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error stopping orchestrator: {ex.Message}");
            }

            // 2. Stop audio capture endpoints to avoid device locks
            try
            {
                var audioService = ServiceProvider.GetService<IAudioService>();
                audioService?.Stop();
                audioService?.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error stopping audio service: {ex.Message}");
            }

            // 3. Dispose unmanaged COM objects, NVENC contexts, and ServiceProvider
            if (ServiceProvider is IDisposable disposableProvider)
            {
                disposableProvider.Dispose();
            }

            base.OnExit(e);
        }
    }
}
