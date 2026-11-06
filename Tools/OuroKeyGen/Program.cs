using System;
using System.Security.Cryptography;
using System.Text;

namespace OuroKeyGen
{
    class Program
    {
        private static readonly byte[] SecretKey = Encoding.UTF8.GetBytes("OURO_REPLAY_ASIC_MASTER_SEC_2026_X99_#MINT");
        private const string Alphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";

        static void Main(string[] args)
        {
            Console.Title = "Ouro - Generador Oficial de Licencias";

            if (args.Length == 1 && (args[0] == "--self" || args[0] == "-s"))
            {
                string localHwid = GetLocalHwid();
                string lifetimeKey = GenerateKey(localHwid, "Lifetime");
                string monthlyKey = GenerateKey(localHwid, "Monthly");
                Console.WriteLine($"LOCAL_HWID: {localHwid}");
                Console.WriteLine($"LIFETIME_KEY: {lifetimeKey}");
                Console.WriteLine($"MONTHLY_KEY: {monthlyKey}");
                return;
            }

            if (args.Length >= 2)
            {
                string cliHwid = args[0].Trim().ToUpperInvariant();
                string cliPlan = (args[1] == "1" || args[1].Equals("monthly", StringComparison.OrdinalIgnoreCase)) ? "Monthly" : "Lifetime";
                string key = GenerateKey(cliHwid, cliPlan);
                Console.WriteLine($"HWID: {cliHwid}");
                Console.WriteLine($"PLAN: {cliPlan}");
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
                string licenseKey = GenerateKey(hwid, planType);

                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("==================================================");
                Console.WriteLine($"✦ LICENCIA GENERADA CON ÉXITO [{planType.ToUpper()}]");
                Console.WriteLine("==================================================");
                Console.ResetColor();
                Console.WriteLine($"Cliente HWID : {hwid}");
                Console.WriteLine($"Tipo         : {planType}");
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("CLAVE PARA EL CLIENTE (12 caracteres):");
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine(licenseKey);
                Console.ResetColor();
                Console.WriteLine("==================================================");
                Console.WriteLine("\nPresiona [Enter] para generar otra clave...");
                Console.ReadLine();
            }
        }

        public static string GenerateKey(string hwid, string planType)
        {
            string planFlag = (planType.Equals("Lifetime", StringComparison.OrdinalIgnoreCase)) ? "L" : "M";
            string monthCode;

            if (planFlag == "L")
            {
                monthCode = "99";
            }
            else
            {
                // Expires 1 month from now: count months since Jan 2026
                var epoch = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                var expiry = DateTime.UtcNow.AddDays(31);
                int months = ((expiry.Year - epoch.Year) * 12) + (expiry.Month - epoch.Month) + 1;
                monthCode = months.ToString("D2");
            }

            string signaturePart = ComputeSignature(hwid, planFlag, monthCode);
            string raw12 = $"{planFlag}{monthCode}{signaturePart}";
            return $"OURO-{raw12.Substring(0, 4)}-{raw12.Substring(4, 4)}-{raw12.Substring(8, 4)}";
        }

        private static string ComputeSignature(string hwid, string planFlag, string monthCode)
        {
            using var hmac = new HMACSHA256(SecretKey);
            string message = $"{hwid}|{planFlag}|{monthCode}";
            byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));

            var sb = new StringBuilder(9);
            for (int i = 0; i < 9; i++)
            {
                int index = (hash[i] ^ hash[i + 9] ^ hash[i + 18]) % Alphabet.Length;
                sb.Append(Alphabet[Math.Abs(index)]);
            }
            return sb.ToString();
        }

        public static string GetLocalHwid()
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
    }
}
