using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Ouro.Utils;

namespace Ouro.Services.Social
{
    public sealed class DiscordWebhookService : IDiscordService
    {
        private static readonly HttpClient SharedHttpClient = new(new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromMinutes(3)
        };

        private const int OuroMintColor = 65443; // #00FFA3 in integer
        private const string InvalidWebhookMessage = "URL del webhook no válida. Usa https://discord.com/api/webhooks/ID/TOKEN.";
        private readonly HttpClient _httpClient;
        private readonly Func<string, int, Task<string?>> _compress;

        public DiscordWebhookService() : this(SharedHttpClient) { }

        // Keep transport and compression replaceable for offline regression tests.
        internal DiscordWebhookService(HttpClient httpClient, Func<string, int, Task<string?>>? compress = null)
        {
            _httpClient = httpClient;
            _compress = compress ?? CompressForDiscordAsync;
        }

        internal static bool TryGetWebhookUri(string? value, [NotNullWhen(true)] out Uri? uri)
        {
            uri = null;
            if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var candidate) ||
                candidate.Scheme != Uri.UriSchemeHttps || !candidate.IsDefaultPort ||
                candidate.UserInfo.Length != 0 || candidate.Fragment.Length != 0)
                return false;

            // Exact hosts prevent lookalike domains. Keep legacy/canary/PTB URLs working.
            bool discordHost = candidate.IdnHost.ToLowerInvariant() is
                "discord.com" or "discordapp.com" or "canary.discord.com" or
                "ptb.discord.com" or "canary.discordapp.com" or "ptb.discordapp.com";
            if (!discordHost || !Regex.IsMatch(candidate.AbsolutePath,
                @"\A/api(?:/v[0-9]+)?/webhooks/[0-9]+/[A-Za-z0-9_-]+/?\z",
                RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
                return false;

            uri = candidate;
            return true;
        }

        public async Task<(bool Success, string Message)> TestWebhookAsync(string webhookUrl)
        {
            if (!TryGetWebhookUri(webhookUrl, out var uri))
            {
                return (false, InvalidWebhookMessage);
            }

            try
            {
                var payload = new
                {
                    content = "⚡ **Conexión Exitosa con Ouro Replay**",
                    embeds = new[]
                    {
                        new
                        {
                            title = "🟢 Ouro Webhook Activo",
                            description = "¡Tu webhook de Discord ha sido configurado y verificado correctamente! Ahora puedes compartir tus mejores jugadas directamente desde Ouro.",
                            color = OuroMintColor,
                            footer = new
                            {
                                text = "Ouro • Zero-Lag Clip Replay"
                            },
                            timestamp = DateTime.UtcNow.ToString("o")
                        }
                    }
                };

                string json = JsonSerializer.Serialize(payload);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");

                using var response = await _httpClient.PostAsync(uri, content).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    return (true, "¡Conexión con Discord verificada con éxito! Revisa tu canal de Discord.");
                }

                return (false, $"Discord respondió con error {(int)response.StatusCode}: {response.ReasonPhrase}");
            }
            catch (Exception ex)
            {
                return (false, $"Error al conectar con Discord: {ex.Message}");
            }
        }

        public async Task<(bool Success, string Message)> PostManualAsync(string webhookUrl)
        {
            if (!TryGetWebhookUri(webhookUrl, out var uri))
            {
                return (false, InvalidWebhookMessage);
            }

            try
            {
                var payload = new
                {
                    content = "📌 **MANUAL OFICIAL • CÓMO USAR OURO REPLAY ENGINE**\n*¡Fija este mensaje en tu canal con clic derecho -> Fijar mensaje!*",
                    embeds = new[]
                    {
                        new
                        {
                            title = "🐉 Guía de Inicio Rápido: Ouro Replay Engine",
                            description = "Bienvenido a **Ouro**, el motor de repetición retrospectiva de ultra bajo impacto diseñado para capturar tus mejores jugadas con **0% de lag y 0 MB/s de desgaste en disco**.",
                            color = OuroMintColor,
                            fields = new[]
                            {
                                new
                                {
                                    name = "⚡ 1. ¿Cómo sacar un Clip?",
                                    value = "Durante tu partida, simplemente presiona **`F12`** (o tu atajo configurado). Ouro extraerá al instante los últimos segundos directamente desde la memoria RAM sin tirones ni caídas de FPS.",
                                    inline = false
                                },
                                new
                                {
                                    name = "🎧 2. Audio Dual-Track Limpio",
                                    value = "Tus clips capturan tanto el sonido del juego como tu micrófono de forma balanceada para que tus mejores jugadas y reacciones se escuchen impecables.",
                                    inline = false
                                },
                                new
                                {
                                    name = "📂 3. Galería Organizada por Juego",
                                    value = "Haz clic en **`VER CLIPS`** en la aplicación. Ouro clasifica automáticamente cada clip en su carpeta correspondiente (*VALORANT*, *CS2*, *FORTNITE*, etc.).",
                                    inline = false
                                },
                                new
                                {
                                    name = "🚀 4. Compartir Directo en Discord",
                                    value = "Selecciona cualquier clip y presiona **`COMPARTIR EN DISCORD`**.\nOuro ajustará y optimizará el video automáticamente para que se reproduzca directo en el chat sin fallos ni límites de peso.",
                                    inline = false
                                },
                                new
                                {
                                    name = "⚙️ 5. Calidad Recomendada",
                                    value = "• **Resolución**: 720p o 1080p a 60 FPS\n• **Bitrate**: 8 a 15 Mbps\n• **Límite Discord**: 18 MB (Servidor Estándar) o 48 MB (Servidor Boost)",
                                    inline = false
                                }
                            },
                            footer = new
                            {
                                text = "Ouro Replay Engine • Zero-Lag Clip Replay"
                            },
                            timestamp = DateTime.UtcNow.ToString("o")
                        }
                    }
                };

                string json = JsonSerializer.Serialize(payload);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var response = await _httpClient.PostAsync(uri, content).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    return (true, "¡Manual 'Cómo usar Ouro' publicado en Discord! Ya puedes fijarlo 📌");
                }

                return (false, $"Discord respondió con error {(int)response.StatusCode}: {response.ReasonPhrase}");
            }
            catch (Exception ex)
            {
                return (false, $"Error al publicar manual: {ex.Message}");
            }
        }

        public async Task<(bool Success, string Message)> ShareClipAsync(string webhookUrl, string clipFilePath, string gameName, string specs, int maxUploadMb = 48)
        {
            if (!TryGetWebhookUri(webhookUrl, out var uri))
            {
                return (false, InvalidWebhookMessage);
            }

            if (!File.Exists(clipFilePath))
            {
                return (false, "El archivo del clip no fue encontrado en el disco.");
            }

            var fi = new FileInfo(clipFilePath);
            double originalSizeMb = fi.Length / (1024.0 * 1024.0);

            // Discord safe thresholds:
            // Free servers have a strict 20,000,000 byte limit (~19 MB).
            // Boost Level 1 servers have 50,000,000 byte limit (~47.6 MB).
            long safeCeilingBytes = (maxUploadMb <= 24)
                ? 19_000_000L // 18.1 MB safe threshold for free servers
                : 48_000_000L; // 45.7 MB safe threshold for boost servers

            string uploadFilePath = clipFilePath;
            string? tempCompressedFile = null;

            try
            {
                // If video is larger than the safe ceiling, compress on-the-fly to a temporary file
                if (fi.Length > safeCeilingBytes)
                {
                    int targetMb = (maxUploadMb <= 24) ? 18 : 45;
                    tempCompressedFile = await _compress(clipFilePath, targetMb).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(tempCompressedFile) && File.Exists(tempCompressedFile))
                    {
                        uploadFilePath = tempCompressedFile;
                    }
                }

                // Attempt upload
                var (success, errorStatus) = await TryUploadFileAsync(uri, uploadFilePath, gameName, specs, originalSizeMb).ConfigureAwait(false);

                // If Discord rejected with 413 (Payload Too Large):
                // Cascade retry 1: drop to 18 MB
                if (!success && errorStatus == 413)
                {
                    if (tempCompressedFile != null && File.Exists(tempCompressedFile))
                    {
                        try { File.Delete(tempCompressedFile); } catch { }
                        tempCompressedFile = null;
                    }

                    tempCompressedFile = await _compress(clipFilePath, 18).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(tempCompressedFile) && File.Exists(tempCompressedFile))
                    {
                        uploadFilePath = tempCompressedFile;
                        var retry1 = await TryUploadFileAsync(uri, uploadFilePath, gameName, specs, originalSizeMb).ConfigureAwait(false);
                        success = retry1.Success;
                        errorStatus = retry1.StatusCode;
                    }

                    // Cascade retry 2: if 18 MB still got rejected (e.g. 10 MB strict mobile limit channel), drop to 9.5 MB
                    if (!success && errorStatus == 413)
                    {
                        if (tempCompressedFile != null && File.Exists(tempCompressedFile))
                        {
                            try { File.Delete(tempCompressedFile); } catch { }
                            tempCompressedFile = null;
                        }

                        tempCompressedFile = await _compress(clipFilePath, 9).ConfigureAwait(false);
                        if (!string.IsNullOrEmpty(tempCompressedFile) && File.Exists(tempCompressedFile))
                        {
                            uploadFilePath = tempCompressedFile;
                            var retry2 = await TryUploadFileAsync(uri, uploadFilePath, gameName, specs, originalSizeMb).ConfigureAwait(false);
                            success = retry2.Success;
                            errorStatus = retry2.StatusCode;
                        }
                    }
                }

                if (success)
                {
                    bool wasCompressed = tempCompressedFile != null;
                    return (true, wasCompressed
                        ? $"¡Clip de {gameName} optimizado y compartido con éxito en Discord!"
                        : $"¡Clip de {gameName} compartido con éxito en Discord!");
                }

                return (false, $"Discord respondió con error {errorStatus}");
            }
            catch (Exception ex)
            {
                return (false, $"Fallo al compartir en Discord: {ex.Message}");
            }
            finally
            {
                if (tempCompressedFile != null && File.Exists(tempCompressedFile))
                {
                    try { File.Delete(tempCompressedFile); } catch { }
                }
            }
        }

        private async Task<(bool Success, int StatusCode)> TryUploadFileAsync(Uri webhookUri, string filePath, string gameName, string specs, double originalSizeMb)
        {
            var fi = new FileInfo(filePath);
            double uploadSizeMb = fi.Length / (1024.0 * 1024.0);

            using var multipart = new MultipartFormDataContent();

            var payload = new
            {
                content = $"🔥 **¡Nuevo Clip de {gameName}!**",
                embeds = new[]
                {
                    new
                    {
                        title = $"🎮 {gameName} • Replay Guardado",
                        description = uploadSizeMb < originalSizeMb - 0.5
                            ? $"Clip extraído desde **Ouro Replay Engine** ({uploadSizeMb:F1} MB, optimizado para Discord)"
                            : $"Clip extraído desde **Ouro Replay Engine** ({uploadSizeMb:F1} MB)",
                        color = OuroMintColor,
                        fields = new[]
                        {
                            new { name = "Juego", value = gameName, @inline = true },
                            new { name = "Calidad", value = specs, @inline = true },
                            new { name = "Tamaño", value = $"{uploadSizeMb:F1} MB", @inline = true }
                        },
                        footer = new
                        {
                            text = "Ouro • Zero-Lag Clip Replay"
                        },
                        timestamp = DateTime.UtcNow.ToString("o")
                    }
                }
            };

            string jsonString = JsonSerializer.Serialize(payload);
            multipart.Add(new StringContent(jsonString, Encoding.UTF8, "application/json"), "payload_json");

            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var fileContent = new StreamContent(fileStream);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
            multipart.Add(fileContent, "files[0]", Path.GetFileName(filePath));

            using var response = await _httpClient.PostAsync(webhookUri, multipart).ConfigureAwait(false);
            return (response.IsSuccessStatusCode, (int)response.StatusCode);
        }

        private static Task<string?> CompressForDiscordAsync(string sourceFile, int targetMb) =>
            CompressForDiscordCoreAsync(sourceFile, targetMb, RunFFmpegProcessAsync, GetVideoDurationSecAsync);

        internal static async Task<string?> CompressForDiscordCoreAsync(string sourceFile, int targetMb,
            Func<string, string, string, int, Task<bool>> runEncoder, Func<string, Task<double>> probe)
        {
            string? tempFile = null;
            bool completed = false;
            try
            {
                // Target slightly below limit (e.g. targetMb - 1.5MB) to guarantee Discord's strict multipart ceiling
                double targetBytes = (Math.Max(9, targetMb) - 1.2) * 1024.0 * 1024.0;

                // Probe video duration
                double durationSec = await probe(sourceFile).ConfigureAwait(false);
                if (durationSec <= 0.5) durationSec = 20.0;

                // Calculate required video bitrate in kbps
                double totalKbps = (targetBytes * 8.0) / (durationSec * 1024.0);
                int audioKbps = 128;
                int videoKbps = (int)Math.Max(600, totalKbps - audioKbps);

                tempFile = Path.Combine(Path.GetTempPath(), $"ouro_discord_{Guid.NewGuid():N}.mp4");
                string? ffmpegPath = FFmpegBootstrapper.GetFFmpegExecutablePath();
                if (string.IsNullOrEmpty(ffmpegPath) || !File.Exists(ffmpegPath))
                {
                    ffmpegPath = "ffmpeg";
                }

                // Scale down to 720p max if targeting tight bitrate (< 4000 kbps) for 3x faster encoding & sharper image
                string scaleFilter = videoKbps < 4000 ? "-vf \"scale='min(1280,iw)':-2\" " : "";

                // 1. Ultra-fast Hardware NVENC transcode (no stdout/stderr pipe redirection to prevent OS 4KB pipe deadlock)
                string nvencArgs = $"-nostdin -loglevel error -y -hwaccel auto -i \"{sourceFile}\" {scaleFilter}-c:v h264_nvenc -b:v {videoKbps}k -maxrate {videoKbps + 200}k -bufsize {videoKbps * 2}k -preset p1 -c:a copy -movflags +faststart \"{tempFile}\"";
                if (await runEncoder(ffmpegPath, nvencArgs, tempFile, 45).ConfigureAwait(false))
                {
                    completed = true;
                    return tempFile;
                }

                // 2. Fallback to multi-threaded CPU libx264 ultrafast if NVENC is unavailable or busy
                string cpuArgs = $"-nostdin -loglevel error -y -i \"{sourceFile}\" {scaleFilter}-c:v libx264 -preset ultrafast -threads 0 -b:v {videoKbps}k -maxrate {videoKbps + 200}k -bufsize {videoKbps * 2}k -c:a aac -b:a {audioKbps}k -movflags +faststart \"{tempFile}\"";
                if (await runEncoder(ffmpegPath, cpuArgs, tempFile, 60).ConfigureAwait(false))
                {
                    completed = true;
                    return tempFile;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DiscordWebhookService] Compression error: {ex.Message}");
            }
            finally
            {
                if (!completed && tempFile != null)
                {
                    try { File.Delete(tempFile); } catch { }
                }
            }

            return null;
        }

        private static async Task<bool> RunFFmpegProcessAsync(string ffmpegPath, string arguments, string outputFile, int timeoutSeconds)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = arguments,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false,
                    RedirectStandardInput = false,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc == null) return false;

                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                try
                {
                    await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    await StopProcessAsync(proc).ConfigureAwait(false);
                    return false;
                }

                return proc.ExitCode == 0 && File.Exists(outputFile) && new FileInfo(outputFile).Length > 1000;
            }
            catch
            {
                return false;
            }
        }

        private static async Task<double> GetVideoDurationSecAsync(string filePath)
        {
            try
            {
                string? probePath = FFmpegBootstrapper.GetFFprobeExecutablePath();
                if (string.IsNullOrEmpty(probePath) || !File.Exists(probePath))
                {
                    probePath = "ffprobe";
                }

                var psi = new ProcessStartInfo
                {
                    FileName = probePath,
                    Arguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{filePath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = false,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                string? output = await ReadProbeOutputAsync(psi, TimeSpan.FromSeconds(3)).ConfigureAwait(false);
                if (output != null &&
                    double.TryParse(output.Trim(), System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out double sec) &&
                    double.IsFinite(sec) && sec > 0)
                {
                    return sec;
                }
            }
            catch { }
            return 20.0;
        }

        internal static async Task<string?> ReadProbeOutputAsync(ProcessStartInfo startInfo, TimeSpan timeout)
        {
            using var proc = Process.Start(startInfo);
            if (proc == null) return null;
            using var cts = new System.Threading.CancellationTokenSource(timeout);
            try
            {
                Task<string> output = proc.StandardOutput.ReadToEndAsync(cts.Token);
                await Task.WhenAll(output, proc.WaitForExitAsync(cts.Token)).ConfigureAwait(false);
                return proc.ExitCode == 0 ? await output.ConfigureAwait(false) : null;
            }
            catch (OperationCanceledException)
            {
                await StopProcessAsync(proc).ConfigureAwait(false);
                return null;
            }
            catch
            {
                await StopProcessAsync(proc).ConfigureAwait(false);
                throw;
            }
        }

        private static async Task StopProcessAsync(Process process)
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(2));
                await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            }
            catch { /* Best effort teardown; do not block the sharing flow indefinitely. */ }
        }
    }
}
