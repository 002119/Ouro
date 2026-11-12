using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Ouro.Services.Social;
using Ouro.Utils;

internal static class DiscordRegression
{
    // Deliberately fictitious token. All requests use an in-memory HTTP handler.
    private const string Webhook = "https://discord.com/api/webhooks/123456789/audit_fake_token";
    private static int _checks;
    private static int _failed;
    private static void Check(string name, bool ok)
    {
        _checks++;
        if (!ok) _failed++;
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {name}");
    }

    internal static async Task<int> RunAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ouro_discord_audit_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            foreach (string value in new[] { Webhook, "  " + Webhook + "  ", Webhook.Replace("/api/", "/api/v10/"),
                Webhook.Replace("discord.com", "discordapp.com"), Webhook.Replace("discord.com", "canary.discord.com"),
                Webhook.Replace("discord.com", "ptb.discord.com"), Webhook + "?wait=true&thread_id=123456" })
                Check("Accept supported Discord webhook shape", DiscordWebhookService.TryGetWebhookUri(value, out _));

            using var transport = new FakeTransport(HttpStatusCode.NoContent);
            using var http = new HttpClient(transport);
            var service = new DiscordWebhookService(http);
            string small = Path.Combine(directory, "clip.mp4");
            await File.WriteAllBytesAsync(small, new byte[2048]);
            foreach (string value in new[] { "", "http://127.0.0.1:1234/", Webhook.Replace("https:", "http:"),
                Webhook.Replace("discord.com", "discord.com.attacker.invalid"), Webhook.Replace("discord.com", "attacker.invalid"),
                Webhook.Replace("discord.com", "discord.com@attacker.invalid"), Webhook.Replace("discord.com", "user@discord.com"),
                Webhook.Replace("discord.com", "discord.com:8443"), "https://discord.com/api/webhooks/123", Webhook + "/messages/1", Webhook + "#fragment" })
            {
                Check("Reject unsafe/incomplete destination in all operations",
                    !(await service.TestWebhookAsync(value)).Success && !(await service.PostManualAsync(value)).Success &&
                    !(await service.ShareClipAsync(value, small, "Game", "1080p")).Success);
            }
            Check("Invalid destinations never reach HTTP transport", transport.RequestCount == 0);
            Check("Webhook connection check still works", (await service.TestWebhookAsync(Webhook)).Success);
            Check("Manual posting still works", (await service.PostManualAsync(Webhook)).Success);

            int compressed = 0;
            var direct = new DiscordWebhookService(http, (_, _) => { compressed++; return Task.FromResult<string?>(null); });
            Check("Small clip shares in one request without compression", (await direct.ShareClipAsync(Webhook, small, "Game", "1080p")).Success && compressed == 0 && transport.FileRequests == 1);

            foreach (var option in new[] { (Limit: 18, Size: 20_000_000L, Target: 18), (Limit: 48, Size: 49_000_000L, Target: 45) })
            {
                string source = Path.Combine(directory, $"original_{option.Limit}.mp4");
                using (var stream = File.Create(source)) stream.SetLength(option.Size);
                string? output = null;
                int target = 0;
                var optimized = new DiscordWebhookService(http, async (_, mb) =>
                {
                    target = mb;
                    output = Path.Combine(directory, $"compressed_{option.Limit}.mp4");
                    await File.WriteAllBytesAsync(output, new byte[1024]);
                    return output;
                });
                Check($"Selected {option.Limit} MB limit preserves automatic optimization", (await optimized.ShareClipAsync(Webhook, source, "Game", "1080p", option.Limit)).Success && target == option.Target);
                Check("Original clip retained; successful upload temporary removed", File.Exists(source) && new FileInfo(source).Length == option.Size && output != null && !File.Exists(output));
            }

            using var retryTransport = new FakeTransport(HttpStatusCode.RequestEntityTooLarge, HttpStatusCode.RequestEntityTooLarge, HttpStatusCode.NoContent);
            using var retryHttp = new HttpClient(retryTransport);
            var targets = new List<int>();
            var outputs = new List<string>();
            var retry = new DiscordWebhookService(retryHttp, async (_, mb) =>
            {
                targets.Add(mb);
                string path = Path.Combine(directory, $"retry_{targets.Count}.mp4");
                outputs.Add(path);
                await File.WriteAllBytesAsync(path, new byte[1024]);
                return path;
            });
            Check("413 retries preserve 45 -> 18 -> 9 MB flow", (await retry.ShareClipAsync(Webhook, Path.Combine(directory, "original_48.mp4"), "Game", "1080p", 48)).Success && targets.SequenceEqual(new[] { 45, 18, 9 }) && retryTransport.FileRequests == 3);
            Check("Retry temporaries cleaned", outputs.All(p => !File.Exists(p)));

            using var failureTransport = new FakeTransport(HttpStatusCode.BadRequest);
            using var failureHttp = new HttpClient(failureTransport);
            string failureTemp = Path.Combine(directory, "failed_upload.mp4");
            var failure = new DiscordWebhookService(failureHttp, async (_, _) => { await File.WriteAllBytesAsync(failureTemp, new byte[1024]); return failureTemp; });
            Check("Failed upload reports failure and cleans temporary", !(await failure.ShareClipAsync(Webhook, Path.Combine(directory, "original_48.mp4"), "Game", "1080p", 48)).Success && !File.Exists(failureTemp));

            string? partial = null;
            int attempts = 0;
            var encoderArguments = new List<string>();
            string? failedCompression = await DiscordWebhookService.CompressForDiscordCoreAsync(small, 18, async (_, arguments, output, _) =>
            {
                partial = output;
                attempts++;
                encoderArguments.Add(arguments);
                await File.WriteAllBytesAsync(output, new byte[2048]);
                return false;
            }, _ => Task.FromResult(60.0));
            Check("Both encoder failures clean partially written video", failedCompression == null && attempts == 2 && partial != null && !File.Exists(partial));
            Check("Hardware first and CPU fallback settings preserved", encoderArguments[0].Contains("-c:v h264_nvenc") && encoderArguments[0].Contains("-c:a copy") && encoderArguments[1].Contains("-c:v libx264") && encoderArguments[1].Contains("-c:a aac") && encoderArguments.All(a => a.Contains("scale='min(1280,iw)':-2") && a.Contains("-nostdin")));

            var normalProbe = SelfProcess("--probe-number");
            Check("Probe output read asynchronously", await DiscordWebhookService.ReadProbeOutputAsync(normalProbe, TimeSpan.FromSeconds(5)) == "12.5");
            string marker = Path.Combine(directory, "probe.pid");
            var sleepingProbe = SelfProcess("--probe-sleep", marker);
            var watch = Stopwatch.StartNew();
            string? timedOut = await DiscordWebhookService.ReadProbeOutputAsync(sleepingProbe, TimeSpan.FromSeconds(2));
            Check("Hanging probe has a bounded wait", timedOut == null && watch.Elapsed < TimeSpan.FromSeconds(6));
            bool stopped = false;
            if (File.Exists(marker))
            {
                int pid = int.Parse(await File.ReadAllTextAsync(marker));
                try { using var child = Process.GetProcessById(pid); stopped = child.HasExited; }
                catch (ArgumentException) { stopped = true; }
            }
            Check("Timed out probe process is terminated", stopped);
        }
        finally
        {
            // Only this probe's GUID-named directory is removed.
            Directory.Delete(directory, recursive: true);
        }
        Console.WriteLine($"DISCORD_CHECKS={_checks}; FAILED={_failed}; NETWORK_REQUESTS=0");
        return _failed == 0 ? 0 : 1;
    }

    private static ProcessStartInfo SelfProcess(params string[] arguments)
    {
        var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        return info;
    }

    internal static async Task<int> RunSmokeAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ouro_compression_smoke_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string? compressed = null;
        try
        {
            string source = Path.Combine(directory, "synthetic.mp4");
            var generate = new ProcessStartInfo(FFmpegBootstrapper.GetFFmpegExecutablePath()!)
            {
                UseShellExecute = false, CreateNoWindow = true,
                Arguments = $"-nostdin -loglevel error -y -f lavfi -i testsrc2=size=320x180:rate=30 -f lavfi -i sine=frequency=880:sample_rate=48000 -t 2 -c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a aac \"{source}\""
            };
            using (var process = Process.Start(generate)!)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                try { await process.WaitForExitAsync(cts.Token); }
                catch { process.Kill(entireProcessTree: true); throw; }
                if (process.ExitCode != 0) throw new InvalidOperationException("Synthetic clip generation failed");
            }
            byte[] originalHash = SHA256.HashData(await File.ReadAllBytesAsync(source));
            var method = typeof(DiscordWebhookService).GetMethod("CompressForDiscordAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
            compressed = await (Task<string?>)method.Invoke(null, new object[] { source, 18 })!;
            Check("Real FFmpeg compression produces a video", compressed != null && File.Exists(compressed) && new FileInfo(compressed).Length > 1000);
            Check("Real compression preserves original file", originalHash.SequenceEqual(SHA256.HashData(await File.ReadAllBytesAsync(source))));
            if (compressed != null)
            {
                var probe = new ProcessStartInfo(FFmpegBootstrapper.GetFFprobeExecutablePath()!)
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
                    Arguments = $"-v error -show_entries stream=codec_name,codec_type:format=duration -of json \"{compressed}\""
                };
                string? output = await DiscordWebhookService.ReadProbeOutputAsync(probe, TimeSpan.FromSeconds(5));
                using var metadata = JsonDocument.Parse(output ?? "{}");
                var streams = metadata.RootElement.GetProperty("streams").EnumerateArray().ToArray();
                Check("Compressed video retains H264 and AAC streams", streams.Any(s => s.GetProperty("codec_name").GetString() == "h264") && streams.Any(s => s.GetProperty("codec_name").GetString() == "aac"));
                double duration = double.Parse(metadata.RootElement.GetProperty("format").GetProperty("duration").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
                Check("Synthetic clip duration remains about two seconds", Math.Abs(duration - 2.0) < 0.2);
            }
        }
        finally
        {
            if (compressed != null) File.Delete(compressed);
            Directory.Delete(directory, recursive: true);
        }
        Console.WriteLine($"COMPRESSION_SMOKE_CHECKS={_checks}; FAILED={_failed}; NETWORK_REQUESTS=0");
        return _failed == 0 ? 0 : 1;
    }

    private sealed class FakeTransport(params HttpStatusCode[] statuses) : HttpMessageHandler
    {
        internal int RequestCount { get; private set; }
        internal int FileRequests { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is MultipartFormDataContent multipart)
            {
                if (!multipart.Any(p => p.Headers.ContentDisposition?.Name?.Trim('"') == "files[0]")) throw new InvalidOperationException("Missing clip attachment");
                // Consume the small synthetic attachment to exercise live file handles.
                foreach (var part in multipart) await part.ReadAsByteArrayAsync(cancellationToken);
                FileRequests++;
            }
            var status = statuses[Math.Min(RequestCount, statuses.Length - 1)];
            RequestCount++;
            return new HttpResponseMessage(status);
        }
    }
}
