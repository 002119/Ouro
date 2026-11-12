using System;
using System.Threading.Tasks;

namespace Ouro.Services.Social
{
    public interface IDiscordService
    {
        Task<(bool Success, string Message)> TestWebhookAsync(string webhookUrl);
        Task<(bool Success, string Message)> PostManualAsync(string webhookUrl);
        Task<(bool Success, string Message)> ShareClipAsync(string webhookUrl, string clipFilePath, string gameName, string specs, int maxUploadMb = 48);
    }
}
