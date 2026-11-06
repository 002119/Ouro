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
    /// Thread-safe manager for reading and persisting user preferences to %APPDATA%\Ouro\settings.json.
    /// The bundled appsettings.json is treated as a read-only default template: user data and
    /// credentials must never be written back into the installation directory.
    /// </summary>
    public sealed class SettingsManager : ISettingsManager
    {
        private readonly SemaphoreSlim _ioLock = new(1, 1);
        private readonly object _settingsLock = new();
        private readonly string _persistentPath;
        private OuroSettings _currentSettings;

        public OuroSettings Settings
        {
            get
            {
                lock (_settingsLock)
                {
                    return _currentSettings;
                }
            }
            private set
            {
                lock (_settingsLock)
                {
                    _currentSettings = value;
                }
            }
        }

        public SettingsManager(IOptions<OuroSettings> options)
        {
            string appDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ouro");
            Directory.CreateDirectory(appDataFolder);
            _persistentPath = Path.Combine(appDataFolder, "settings.json");

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

                // Persist only in the per-user profile. Never mutate the installed appsettings.json:
                // it may be read-only, shared by all users, or accidentally committed to source control.
                var userRootNode = new JsonObject();
                userRootNode[OuroSettings.SectionName] = JsonSerializer.SerializeToNode(newSettings);
                string userJson = userRootNode.ToJsonString(serializerOptions);
                await File.WriteAllTextAsync(_persistentPath, userJson).ConfigureAwait(false);

                // Update in-memory reference
                Settings = newSettings;
            }
            finally
            {
                _ioLock.Release();
            }
        }
    }
}
