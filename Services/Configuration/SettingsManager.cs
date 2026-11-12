using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
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
    /// Each user's Discord Webhook URL is bound to their Windows user profile via DPAPI so it never leaks across machines.
    /// </summary>
    public sealed class SettingsManager : ISettingsManager
    {
        private const string DpapiPrefix = "dpapi:";
        private const uint CRYPTPROTECT_UI_FORBIDDEN = 0x1;

        [StructLayout(LayoutKind.Sequential)]
        private struct DATA_BLOB
        {
            public int cbData;
            public IntPtr pbData;
        }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptProtectData(
            ref DATA_BLOB pDataIn,
            string? szDataDescr,
            IntPtr pOptionalEntropy,
            IntPtr pvReserved,
            IntPtr pPromptStruct,
            uint dwFlags,
            out DATA_BLOB pDataOut);

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptUnprotectData(
            ref DATA_BLOB pDataIn,
            IntPtr ppszDataDescr,
            IntPtr pOptionalEntropy,
            IntPtr pvReserved,
            IntPtr pPromptStruct,
            uint dwFlags,
            out DATA_BLOB pDataOut);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LocalFree(IntPtr hMem);

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
                            loaded.DiscordWebhookUrl = UnprotectWebhook(loaded.DiscordWebhookUrl);
                            _currentSettings = loaded;
                            return;
                        }
                    }
                }
                catch { }
            }

            // Priority 2: Injected options from appsettings.json (always empty webhook template)
            _currentSettings = options?.Value ?? new OuroSettings();
            _currentSettings.DiscordWebhookUrl = UnprotectWebhook(_currentSettings.DiscordWebhookUrl);
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
                var node = JsonSerializer.SerializeToNode(newSettings) as JsonObject;
                if (node != null)
                {
                    node[nameof(OuroSettings.DiscordWebhookUrl)] = ProtectWebhook(newSettings.DiscordWebhookUrl);
                }
                userRootNode[OuroSettings.SectionName] = node;
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

        private static string ProtectWebhook(string? plainUrl)
        {
            if (string.IsNullOrWhiteSpace(plainUrl)) return string.Empty;
            if (plainUrl.StartsWith(DpapiPrefix, StringComparison.Ordinal)) return plainUrl;

            byte[] bytes = Encoding.UTF8.GetBytes(plainUrl.Trim());
            GCHandle handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                var dataIn = new DATA_BLOB { cbData = bytes.Length, pbData = handle.AddrOfPinnedObject() };
                if (CryptProtectData(ref dataIn, "OuroDiscordWebhook", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, out DATA_BLOB dataOut))
                {
                    try
                    {
                        byte[] encrypted = new byte[dataOut.cbData];
                        Marshal.Copy(dataOut.pbData, encrypted, 0, dataOut.cbData);
                        return DpapiPrefix + Convert.ToBase64String(encrypted);
                    }
                    finally
                    {
                        if (dataOut.pbData != IntPtr.Zero) LocalFree(dataOut.pbData);
                    }
                }
            }
            catch { }
            finally
            {
                handle.Free();
            }

            return plainUrl;
        }

        private static string UnprotectWebhook(string? storedValue)
        {
            if (string.IsNullOrWhiteSpace(storedValue)) return string.Empty;
            if (!storedValue.StartsWith(DpapiPrefix, StringComparison.Ordinal)) return storedValue.Trim();

            try
            {
                byte[] encrypted = Convert.FromBase64String(storedValue[DpapiPrefix.Length..]);
                GCHandle handle = GCHandle.Alloc(encrypted, GCHandleType.Pinned);
                try
                {
                    var dataIn = new DATA_BLOB { cbData = encrypted.Length, pbData = handle.AddrOfPinnedObject() };
                    if (CryptUnprotectData(ref dataIn, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, out DATA_BLOB dataOut))
                    {
                        try
                        {
                            byte[] plain = new byte[dataOut.cbData];
                            Marshal.Copy(dataOut.pbData, plain, 0, dataOut.cbData);
                            return Encoding.UTF8.GetString(plain);
                        }
                        finally
                        {
                            if (dataOut.pbData != IntPtr.Zero) LocalFree(dataOut.pbData);
                        }
                    }
                }
                finally
                {
                    handle.Free();
                }
            }
            catch { }

            // If copied from another user/machine where DPAPI cannot decrypt, reset cleanly so another user's webhook never leaks
            return string.Empty;
        }
    }
}
