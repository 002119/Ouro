using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Ouro.Models;

namespace Ouro.Services.Configuration
{
    /// <summary>
    /// Thread-safe manager for reading and persisting user preferences to %APPDATA%\Ouro\settings.json
    /// and synchronizing with appsettings.json.
    /// Preserves user selections (resolution, fps, bitrate, audio devices, hotkey) across updates and compilation builds.
    /// </summary>
    public sealed class SettingsManager : ISettingsManager
    {
        private readonly SemaphoreSlim _ioLock = new(1, 1);
        private readonly string _persistentPath;
        private readonly string _appSettingsPath;
        private OuroSettings _currentSettings;

        public OuroSettings Settings
        {
            get
            {
                lock (_ioLock)
                {
                    return _currentSettings;
                }
            }
            private set
            {
                lock (_ioLock)
                {
                    _currentSettings = value;
                }
            }
        }

        public SettingsManager(IOptions<OuroSettings> options, string? settingsPath = null)
        {
            string appDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ouro");
            Directory.CreateDirectory(appDataFolder);
            _persistentPath = Path.Combine(appDataFolder, "settings.json");

            string baseDirFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
            _appSettingsPath = File.Exists(baseDirFile)
                ? baseDirFile
                : Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");

            // Priority 1: Persistent user settings from %APPDATA%
            if (File.Exists(_persistentPath))
            {
                try
                {
                    string json = File.ReadAllText(_persistentPath);
                    var doc = JsonNode.Parse(json);
                    if (doc is JsonObject rootObj)
                    {
                        OuroSettings? loaded = null;
                        if (rootObj.ContainsKey(OuroSettings.SectionName) && rootObj[OuroSettings.SectionName] != null)
                        {
                            loaded = rootObj[OuroSettings.SectionName].Deserialize<OuroSettings>();
                        }
                        else
                        {
                            loaded = rootObj.Deserialize<OuroSettings>();
                        }

                        if (loaded != null)
                        {
                            _currentSettings = loaded;
                            return;
                        }
                    }
                }
                catch { }
            }

            // Priority 2: Injected options from appsettings.json
            _currentSettings = options?.Value ?? new OuroSettings();
        }

        public async Task SaveSettingsAsync(OuroSettings newSettings)
        {
            if (newSettings == null) throw new ArgumentNullException(nameof(newSettings));

            await _ioLock.WaitAsync().ConfigureAwait(false);
            try
            {
                var serializerOptions = new JsonSerializerOptions { WriteIndented = true };

                // 1. Save to %APPDATA%\Ouro\settings.json with "OuroSettings" section wrapper
                var userRootNode = new JsonObject();
                userRootNode[OuroSettings.SectionName] = JsonSerializer.SerializeToNode(newSettings);
                string userJson = userRootNode.ToJsonString(serializerOptions);
                await File.WriteAllTextAsync(_persistentPath, userJson).ConfigureAwait(false);

                // 2. Synchronize with appsettings.json in current execution directory
                await WriteToAppSettingsAsync(_appSettingsPath, newSettings, serializerOptions).ConfigureAwait(false);

                // 3. If in developer environment, also synchronize root project appsettings.json
                string devRootSettings = Path.Combine(@"C:\Proyectos juegos-apps\Ouro", "appsettings.json");
                if (File.Exists(devRootSettings) && !string.Equals(_appSettingsPath, devRootSettings, StringComparison.OrdinalIgnoreCase))
                {
                    await WriteToAppSettingsAsync(devRootSettings, newSettings, serializerOptions).ConfigureAwait(false);
                }

                // Update in-memory reference
                Settings = newSettings;
            }
            finally
            {
                _ioLock.Release();
            }
        }

        private static async Task WriteToAppSettingsAsync(string path, OuroSettings settings, JsonSerializerOptions serializerOptions)
        {
            try
            {
                string jsonContent = File.Exists(path)
                    ? await File.ReadAllTextAsync(path).ConfigureAwait(false)
                    : "{}";

                var rootNode = JsonNode.Parse(jsonContent) as JsonObject ?? new JsonObject();
                rootNode[OuroSettings.SectionName] = JsonSerializer.SerializeToNode(settings);
                string updatedJson = rootNode.ToJsonString(serializerOptions);
                await File.WriteAllTextAsync(path, updatedJson).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SettingsManager] Could not update {path}: {ex.Message}");
            }
        }
    }
}
