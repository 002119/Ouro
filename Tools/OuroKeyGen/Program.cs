using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OuroKeyGen
{
    class Program
    {
        private static readonly string KeysDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "LicensingKeys");
        private static readonly string PrivateKeyFile = Path.Combine(KeysDir, "OuroPrivateKey.xml");
        private static readonly string PublicKeyFile = Path.Combine(KeysDir, "OuroPublicKey.xml");

        static void Main(string[] args)
        {
            Console.Title = "Ouro - Generador Oficial de Licencias";
            EnsureKeysExist();

            if (args.Length == 1 && (args[0] == "--self" || args[0] == "-s"))
            {
                string localHwid = GetLocalHwid();
                string lifetimeKey = GenerateLicenseKey(localHwid, "Lifetime", DateTime.UtcNow, null);
                Console.WriteLine($"LOCAL_HWID: {localHwid}");
                Console.WriteLine($"LIFETIME_KEY: {lifetimeKey}");
                return;
            }

            if (args.Length >= 2)
            {
                string cliHwid = args[0].Trim().ToUpperInvariant();
                string cliPlan = (args[1] == "1" || args[1].Equals("monthly", StringComparison.OrdinalIgnoreCase)) ? "Monthly" : "Lifetime";
                DateTime cDate = DateTime.UtcNow;
                DateTime? eDate = (cliPlan == "Monthly") ? cDate.AddDays(30) : null;
                string key = GenerateLicenseKey(cliHwid, cliPlan, cDate, eDate);
                Console.WriteLine($"HWID: {cliHwid}");
                Console.WriteLine($"PLAN: {cliPlan}");
                Console.WriteLine($"EXPIRES: {(eDate.HasValue ? eDate.Value.ToString("yyyy-MM-dd HH:mm UTC") : "Lifetime")}");
                Console.WriteLine($"KEY: {key}");
                return;
            }

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("==================================================");
            Console.WriteLine("       OURO REPLAY // GENERADOR DE LICENCIAS      ");
            Console.WriteLine("==================================================");
            Console.ResetColor();

            while (true)
            {
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write("Introduce el HWID del cliente (ej: OURO-XXXX-XXXX...): ");
                Console.ResetColor();
                string? hwid = Console.ReadLine()?.Trim().ToUpperInvariant();

                if (string.IsNullOrWhiteSpace(hwid))
                {
                    Console.WriteLine("HWID inválido.");
                    continue;
                }

                Console.WriteLine("\nSelecciona el Tipo de Licencia:");
                Console.WriteLine(" [1] Licencia 1 Mes (30 Días)");
                Console.WriteLine(" [2] Licencia de Por Vida (Lifetime)");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write("Opción (1 o 2): ");
                Console.ResetColor();
                string? opt = Console.ReadLine()?.Trim();

                string planType = (opt == "1") ? "Monthly" : "Lifetime";
                DateTime createdAt = DateTime.UtcNow;
                DateTime? expiresAt = (planType == "Monthly") ? createdAt.AddDays(30) : null;

                string licenseKey = GenerateLicenseKey(hwid, planType, createdAt, expiresAt);

                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("==================================================");
                Console.WriteLine($"✦ LICENCIA GENERADA CON ÉXITO [{planType.ToUpper()}]");
                Console.WriteLine("==================================================");
                Console.ResetColor();
                Console.WriteLine($"Cliente HWID : {hwid}");
                Console.WriteLine($"Tipo         : {planType}");
                Console.WriteLine($"Expira       : {(expiresAt.HasValue ? expiresAt.Value.ToString("yyyy-MM-dd HH:mm UTC") : "NUNCA (Lifetime)")}");
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("CLAVE PARA ENTREGAR AL CLIENTE (Copia completa):");
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine(licenseKey);
                Console.ResetColor();
                Console.WriteLine("==================================================");
                Console.WriteLine("\nPresiona [Enter] para generar otra, o cierra la ventana.");
                Console.ReadLine();
            }
        }

        private static string GetLocalHwid()
        {
            var sb = new StringBuilder();
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
                string? machineGuid = key?.GetValue("MachineGuid")?.ToString();
                sb.Append(machineGuid ?? "GUID_FALLBACK");
            }
            catch { sb.Append("GUID_UNAVAILABLE"); }
            sb.Append('|');

            try
            {
                using var biosKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
                string? baseboardSerial = biosKey?.GetValue("BaseBoardSerialNumber")?.ToString();
                string? systemSerial = biosKey?.GetValue("SystemSerialNumber")?.ToString();
                string? biosVersion = biosKey?.GetValue("BIOSVersion")?.ToString();
                sb.Append(baseboardSerial ?? "MB_NONE");
                sb.Append(':');
                sb.Append(systemSerial ?? "SYS_NONE");
                sb.Append(':');
                sb.Append(biosVersion ?? "BIOS_NONE");
            }
            catch { sb.Append("BIOS_UNAVAILABLE"); }
            sb.Append('|');

            try
            {
                using var cpuKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                string? cpuName = cpuKey?.GetValue("ProcessorNameString")?.ToString();
                string? cpuIdentifier = cpuKey?.GetValue("Identifier")?.ToString();
                sb.Append(cpuName ?? "CPU_NONE");
                sb.Append(':');
                sb.Append(cpuIdentifier ?? "CPU_ID_NONE");
            }
            catch { sb.Append("CPU_UNAVAILABLE"); }

            byte[] rawBytes = Encoding.UTF8.GetBytes(sb.ToString());
            byte[] hash = SHA256.HashData(rawBytes);
            string hex = Convert.ToHexString(hash);
            return $"OURO-{hex[..4]}-{hex[4..8]}-{hex[8..12]}-{hex[12..16]}";
        }

        private static void EnsureKeysExist()
        {
            Directory.CreateDirectory(KeysDir);
            if (!File.Exists(PrivateKeyFile) || !File.Exists(PublicKeyFile))
            {
                using var rsa = RSA.Create(2048);
                string privateXml = rsa.ToXmlString(true);
                string publicXml = rsa.ToXmlString(false);
                File.WriteAllText(PrivateKeyFile, privateXml);
                File.WriteAllText(PublicKeyFile, publicXml);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("[INFO] Nuevas llaves RSA-2048 generadas en LicensingKeys/");
                Console.ResetColor();
            }
        }

        public static string GenerateLicenseKey(string hwid, string planType, DateTime createdAt, DateTime? expiresAt)
        {
            string privateXml = File.ReadAllText(PrivateKeyFile);
            using var rsa = RSA.Create();
            rsa.FromXmlString(privateXml);

            var payloadObj = new
            {
                hwid = hwid,
                plan = planType,
                created = createdAt.ToString("o"),
                expires = expiresAt?.ToString("o"),
                nonce = Guid.NewGuid().ToString("N")[..8]
            };

            string payloadJson = JsonSerializer.Serialize(payloadObj);
            byte[] payloadBytes = Encoding.UTF8.GetBytes(payloadJson);
            byte[] signatureBytes = rsa.SignData(payloadBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            string payloadB64 = Convert.ToBase64String(payloadBytes);
            string signatureB64 = Convert.ToBase64String(signatureBytes);

            string rawLicense = $"{payloadB64}.{signatureB64}";
            return "OURO-" + Convert.ToBase64String(Encoding.UTF8.GetBytes(rawLicense));
        }
    }
}
