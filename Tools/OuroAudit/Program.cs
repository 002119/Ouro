using System.Net;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using FFmpeg.AutoGen;
using NAudio.Wave;
using Ouro.Services.Audio;
using Ouro.Services.Capture;
using Ouro.Services.Export;
using Ouro.Services.Licensing;
using Ouro.Services.Social;
using Ouro.Utils;

// Read-only probes against production classes. No production key is read and no
// license/settings are activated or persisted. Network traffic stays on loopback.
internal static class Program
{
    private static int _failed;
    private static void Check(string name, bool ok)
    {
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {name}");
        if (!ok) _failed++;
    }

    public static unsafe int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--discord") return DiscordRegression.RunAsync().GetAwaiter().GetResult();
        if (args.Length > 0 && args[0] == "--discord-smoke") return DiscordRegression.RunSmokeAsync().GetAwaiter().GetResult();
        if (args.Length > 0 && args[0] == "--probe-number") { Console.Write("12.5"); return 0; }
        if (args.Length > 1 && args[0] == "--probe-sleep")
        {
            File.WriteAllText(args[1], Environment.ProcessId.ToString());
            System.Threading.Thread.Sleep(30_000);
            return 0;
        }
        var manager = new LicenseManager();
        foreach (string value in new[] { "", "legacy123456", "{", "{}", "OURO2.a.a", "OURO2.%%%.$$$", "null" })
            Check("Reject malformed/unsigned license " + (value.Length == 0 ? "empty" : value), !manager.InspectLicense(value).IsValid);

        using var rsa = RSA.Create(2048);
        var envelope = new LicenseEnvelope { Hwid = "OURO-AUDIT", Plan = "Lifetime", IssuedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), Nonce = "audit" };
        envelope.Signature = Convert.ToBase64String(rsa.SignData(LicenseFormat.BuildPayloadBytes(envelope), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        Check("Reject license signed by unrelated RSA key", !manager.InspectLicense(LicenseFormat.Serialize(envelope)).IsValid);
        Check("Token round trip", LicenseFormat.TryParse(LicenseFormat.ToToken(envelope), out var parsed, out _) && parsed!.Signature == envelope.Signature && LicenseFormat.BuildPayload(parsed) == LicenseFormat.BuildPayload(envelope));
        envelope.Plan = "Monthly";
        Check("Changing plan invalidates signature", !rsa.VerifyData(LicenseFormat.BuildPayloadBytes(envelope), Convert.FromBase64String(envelope.Signature), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        Check("Annual LicenseModel exposes remaining days cleanly", new LicenseModel { Plan = "Annual", ExpiresAt = DateTime.UtcNow.AddDays(364.2) }.DaysRemaining == 365 && new LicenseModel { Plan = "Monthly", ExpiresAt = DateTime.UtcNow.AddDays(10.1) }.DaysRemaining == 11);

        var audio = new AudioRamBuffer(new WaveFormat(8000, 16, 1), 1);
        var bytes = Enumerable.Range(0, 24000).Select(i => (byte)(i % 251)).ToArray();
        audio.Write(bytes, 0, 12000);
        audio.Write(bytes, 12000, 12000);
        Check("PCM wrap preserves latest bytes in order", audio.ExtractSnapshot().SequenceEqual(bytes.Skip(8000)));
        audio.Clear();
        Check("PCM clear empties snapshot", audio.ExtractSnapshot().Length == 0);
        audio.Write(bytes, 0, bytes.Length);
        Check("PCM oversized chunk retains tail", audio.ExtractSnapshot().SequenceEqual(bytes.Skip(8000)));
        Check("Game name is safe single directory", GameProcessResolver.ResolveGameName("unknown", "..").Trim('.') != "");
        Check("Known game name avoids invalid filename characters", GameProcessResolver.ResolveGameName("csgo").IndexOfAny(Path.GetInvalidFileNameChars()) < 0);

        // A local server proves whether a purported Discord URL can point elsewhere.
        var reserve = new TcpListener(IPAddress.Loopback, 0);
        reserve.Start();
        int port = ((IPEndPoint)reserve.LocalEndpoint).Port;
        reserve.Stop();
        using var listener = new HttpListener();
        string url = $"http://127.0.0.1:{port}/";
        listener.Prefixes.Add(url);
        listener.Start();
        var request = listener.GetContextAsync();
        var upload = new DiscordWebhookService().TestWebhookAsync(url);
        bool received = request.Wait(TimeSpan.FromSeconds(5));
        if (received)
        {
            request.Result.Response.StatusCode = 204;
            request.Result.Response.Close();
        }
        Check("Reject HTTP endpoint outside Discord before POST", !received);
        if (received) upload.GetAwaiter().GetResult();

        // Synthetic native packet exercises actual snapshot ownership on failure.
        // The deliberately unwritable destination must not retain caller-owned refs.
        if (FFmpegBootstrapper.Initialize())
        {
            AVPacket* packet = ffmpeg.av_packet_alloc();
            try
            {
                ffmpeg.av_new_packet(packet, 1024);
                packet->pts = packet->dts = 0;
                packet->flags = ffmpeg.AV_PKT_FLAG_KEY;
                using var buffer = new RamReplayBuffer(60);
                buffer.EnqueuePacket(packet);
                var snapshot = buffer.ExtractSnapshot();
                int before = ffmpeg.av_buffer_get_ref_count(packet->buf);
                try
                {
                    new Mp4Muxer().ExportClipAsync("\0invalid", snapshot, [], [], AudioFormatInfo.DefaultStereo48kHz, null, buffer.TimeBase).GetAwaiter().GetResult();
                }
                catch (Exception ex) when (ex is ArgumentException or IOException) { }
                int after = ffmpeg.av_buffer_get_ref_count(packet->buf);
                Check("Export failure frees snapshot refs (observed " + before + " -> " + after + ")", after == before - snapshot.Count);
                // Probe cleanup: production currently leaves these owned snapshots live.
                foreach (IntPtr pointer in snapshot) { var p = (AVPacket*)pointer; ffmpeg.av_packet_free(&p); }
            }
            finally { ffmpeg.av_packet_free(&packet); }
        }
        else Console.WriteLine("SKIP native packet ownership: FFmpeg unavailable");
        Console.WriteLine($"FAILED_REQUIREMENTS={_failed}");
        return _failed == 0 ? 0 : 1;
    }
}
