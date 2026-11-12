using System.Threading.Tasks;
using Ouro.Models;

namespace Ouro.Services.Configuration
{
    /// <summary>
    /// Contract for managing application preferences. User values are persisted in
    /// %APPDATA%\Ouro\settings.json; the bundled appsettings.json remains a read-only default.
    /// </summary>
    public interface ISettingsManager
    {
        /// <summary>
        /// Current in-memory settings snapshot.
        /// </summary>
        OuroSettings Settings { get; }

        /// <summary>
        /// Persistently writes new settings to the per-user profile in a thread-safe manner.
        /// </summary>
        Task SaveSettingsAsync(OuroSettings newSettings);
    }
}
