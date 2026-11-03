using System.Threading.Tasks;
using Ouro.Models;

namespace Ouro.Services.Configuration
{
    /// <summary>
    /// Contract for managing and persistently updating application preferences in appsettings.json.
    /// </summary>
    public interface ISettingsManager
    {
        /// <summary>
        /// Current in-memory settings snapshot.
        /// </summary>
        OuroSettings Settings { get; }

        /// <summary>
        /// Persistently writes new settings to appsettings.json in a thread-safe manner.
        /// </summary>
        Task SaveSettingsAsync(OuroSettings newSettings);
    }
}
