using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Ouro.Services.Licensing;

namespace OuroKeyGen
{
    /// <summary>
    /// Offline license issuer. Signs v2 license envelopes with the RSA-2048 private key.
    /// The private key never ships with the client and must never be committed to git.
    /// </summary>
    internal static class Program
    {
        private const string PrivateKeyEnvironmentVariable = "OURO_LICENSE_PRIVATE_KEY";
        private const string PrivateKeyRelativePath = "LicensingKeys/OuroPrivateKey.xml";

        private static int Main(string[] args)
        {
            Console.Title = "Ouro - Generador Oficial de Licencias (RSA-2048)";

            try
            {
                if (HasFlag(args, "--help", "-h", "/?"))
                {
                    PrintUsage();
                    return 0;
                }

                string? verifyPath = GetOptionValue(args, "--verify");
                if (!string.IsNullOrWhiteSpace(verifyPath))
                {
                    return VerifyLicenseFile(verifyPath);
                }

                using RSA privateKey = LoadPrivateKey(GetOptionValue(args, "--key"));
                string? outputPath = GetOptionValue(args, "--out");
                List<string> positional = GetPositionalArgs(args);

                if (positional.Count > 0 &&
                    (positional[0].Equals("--self", StringComparison.OrdinalIgnoreCase) ||
                     positional[0].Equals("-s", StringComparison.OrdinalIgnoreCase)))
                {
                    return GenerateSelfLicenses(privateKey);
                }

                if (positional.Count >= 2)
                {
                    string hwid = NormalizeHwid(positional[0]);
                    var (plan, days) = ParsePlan(positional[1], positional.Count >= 3 ? positional[2] : null);
                    var envelope = IssueLicense(privateKey, hwid, plan, days);

                    string path = string.IsNullOrWhiteSpace(outputPath)
                        ? GetDefaultOutputPath(hwid, plan)
                        : outputPath;
                    SaveLicense(envelope, path);
                    PrintLicense(envelope, path);
                    return 0;
                }

                PrintHeader();
                RunInteractive(privateKey, outputPath);
                return 0;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine();
                Console.WriteLine($"ERROR: {ex.Message}");
                Console.ResetColor();
                Console.WriteLine();
                PrintUsage();
                return 1;
            }
        }

        private static void RunInteractive(RSA privateKey, string? outputPath)
        {
            while (true)
            {
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write("HWID del cliente (ej: OURO-ABCD-1234-EF56-7890): ");
                Console.ResetColor();
                string? hwid = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(hwid))
                {
                    Console.WriteLine("HWID inválido. Inténtalo de nuevo.");
                    continue;
                }

                Console.WriteLine();
                Console.WriteLine("Selecciona el tipo de licencia:");
                Console.WriteLine("  [1] Mensual (31 días por defecto)");
                Console.WriteLine("  [2] De por vida (Lifetime)");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write("Opción (1 o 2): ");
                Console.ResetColor();

                string? option = Console.ReadLine();
                var (plan, days) = ParsePlan(option, null);

                if (plan == "Monthly")
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.Write("Días de validez (Enter = 31): ");
                    Console.ResetColor();
                    string? daysInput = Console.ReadLine();
                    if (!string.IsNullOrWhiteSpace(daysInput) &&
                        int.TryParse(daysInput.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedDays) &&
                        parsedDays > 0)
                    {
                        days = parsedDays;
                    }
                }

                string normalizedHwid = NormalizeHwid(hwid);
                var envelope = IssueLicense(privateKey, normalizedHwid, plan, days);

                string path = string.IsNullOrWhiteSpace(outputPath)
                    ? GetDefaultOutputPath(normalizedHwid, plan)
                    : outputPath;
                SaveLicense(envelope, path);
                PrintLicense(envelope, path);

                Console.WriteLine();
                Console.WriteLine("Presiona [Enter] para generar otra licencia (Ctrl+C para salir)...");
                Console.ReadLine();
            }
        }

        private static int GenerateSelfLicenses(RSA privateKey)
        {
            string hwid = GetLocalHwid();
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("Generando licencias de prueba para este equipo...");
            Console.ResetColor();
            Console.WriteLine($"HWID local: {hwid}");

            var lifetime = IssueLicense(privateKey, hwid, "Lifetime", null);
            string lifetimePath = GetDefaultOutputPath(hwid, "Lifetime");
            SaveLicense(lifetime, lifetimePath);
            PrintLicense(lifetime, lifetimePath);

            var monthly = IssueLicense(privateKey, hwid, "Monthly", 31);
            string monthlyPath = GetDefaultOutputPath(hwid, "Monthly");
            SaveLicense(monthly, monthlyPath);
            PrintLicense(monthly, monthlyPath);

            return 0;
        }

        private static int VerifyLicenseFile(string path)
        {
            if (!File.Exists(path))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"No existe el archivo: {path}");
                Console.ResetColor();
                return 1;
            }

            string raw = File.ReadAllText(path, Encoding.UTF8);
            if (!LicenseFormat.TryParse(raw, out var envelope, out string parseError) || envelope == null)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Licencia inválida: {parseError}");
                Console.ResetColor();
                return 1;
            }

            bool signatureValid = VerifySignature(envelope);
            Console.WriteLine();
            Console.ForegroundColor = signatureValid ? ConsoleColor.Green : ConsoleColor.Red;
            Console.WriteLine(signatureValid
                ? "✔ FIRMA RSA-2048 VÁLIDA"
                : "✘ FIRMA RSA-2048 INVÁLIDA");
            Console.ResetColor();

            Console.WriteLine($"Versión    : {envelope.Version}");
            Console.WriteLine($"Producto   : {envelope.Product}");
            Console.WriteLine($"HWID       : {envelope.Hwid}");
            Console.WriteLine($"Plan       : {envelope.Plan}");
            Console.WriteLine($"Emitida    : {FormatUnix(envelope.IssuedAt)}");
            Console.WriteLine($"Vence      : {(envelope.ExpiresAt is > 0 ? FormatUnix(envelope.ExpiresAt.Value) : "Nunca (Lifetime)")}");
            Console.WriteLine($"Nonce      : {envelope.Nonce}");
            Console.WriteLine($"Este equipo: {GetLocalHwid()}");

            return signatureValid &&
                   string.Equals(envelope.Product, LicenseFormat.ProductName, StringComparison.OrdinalIgnoreCase) &&
                   envelope.Version == LicenseFormat.CurrentVersion
                ? 0
                : 1;
        }

        private static LicenseEnvelope IssueLicense(RSA privateKey, string hwid, string plan, int? days)
        {
            DateTimeOffset issuedAt = DateTimeOffset.UtcNow;
            DateTimeOffset? expiresAt = plan == "Lifetime"
                ? null
                : issuedAt.AddDays(days is > 0 ? days.Value : 31);

            var envelope = new LicenseEnvelope
            {
                Version = LicenseFormat.CurrentVersion,
                Product = LicenseFormat.ProductName,
                Hwid = NormalizeHwid(hwid),
                Plan = plan,
                IssuedAt = issuedAt.ToUnixTimeSeconds(),
                ExpiresAt = expiresAt?.ToUnixTimeSeconds(),
                Nonce = Guid.NewGuid().ToString("N")
            };

            byte[] signature = privateKey.SignData(
                LicenseFormat.BuildPayloadBytes(envelope),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            envelope.Signature = Convert.ToBase64String(signature);
            return envelope;
        }

        private static bool VerifySignature(LicenseEnvelope envelope)
        {
            if (string.IsNullOrWhiteSpace(envelope.Signature))
            {
                return false;
            }

            try
            {
                using var rsa = RSA.Create();
                rsa.FromXmlString(OuroLicensePublicKey.Xml);

                return rsa.VerifyData(
                    LicenseFormat.BuildPayloadBytes(envelope),
                    Convert.FromBase64String(envelope.Signature),
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1);
            }
            catch
            {
                return false;
            }
        }

        private static RSA LoadPrivateKey(string? explicitPath)
        {
            string? path = explicitPath;

            if (string.IsNullOrWhiteSpace(path))
            {
                path = Environment.GetEnvironmentVariable(PrivateKeyEnvironmentVariable);
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                path = FindPrivateKey();
            }

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                throw new FileNotFoundException(
                    "No se encontró la clave privada RSA. Colócala en LicensingKeys/OuroPrivateKey.xml, " +
                    "define OURO_LICENSE_PRIVATE_KEY o usa --key <ruta>.");
            }

            var rsa = RSA.Create();
            try
            {
                rsa.FromXmlString(File.ReadAllText(path, Encoding.UTF8).Trim());
                if (rsa.KeySize < 2048)
                {
                    throw new CryptographicException($"La clave privada es demasiado corta ({rsa.KeySize} bits). Se requieren al menos 2048.");
                }

                EnsureKeyMatchesClient(rsa);

                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"Clave privada: {Path.GetFullPath(path)} ({rsa.KeySize} bits)");
                Console.ResetColor();
                return rsa;
            }
            catch
            {
                rsa.Dispose();
                throw;
            }
        }

        private static void EnsureKeyMatchesClient(RSA privateKey)
        {
            using var clientKey = RSA.Create();
            clientKey.FromXmlString(OuroLicensePublicKey.Xml);

            RSAParameters generatedPublic = privateKey.ExportParameters(false);
            RSAParameters clientPublic = clientKey.ExportParameters(false);

            bool matches = generatedPublic.Modulus != null &&
                           clientPublic.Modulus != null &&
                           generatedPublic.Exponent != null &&
                           clientPublic.Exponent != null &&
                           generatedPublic.Modulus.SequenceEqual(clientPublic.Modulus) &&
                           generatedPublic.Exponent.SequenceEqual(clientPublic.Exponent);

            if (!matches)
            {
                throw new CryptographicException(
                    "La clave privada no coincide con la clave pública embebida en el cliente. " +
                    "Las licencias generadas serían rechazadas; restaura el par de claves correcto.");
            }
        }

        private static string? FindPrivateKey()
        {
            var startingPoints = new List<string>();
            AddIfNotEmpty(startingPoints, Environment.CurrentDirectory);
            AddIfNotEmpty(startingPoints, AppContext.BaseDirectory);

            var candidates = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string start in startingPoints)
            {
                var directory = new DirectoryInfo(start);
                for (int depth = 0; directory != null && depth < 8; depth++, directory = directory.Parent)
                {
                    string candidate = Path.Combine(directory.FullName, PrivateKeyRelativePath);
                    if (File.Exists(candidate) && seen.Add(candidate))
                    {
                        candidates.Add(candidate);
                    }
                }
            }

            // Prefer the copy at the repository root over a legacy copy under Tools/.
            return candidates
                .OrderByDescending(IsRepositoryKey)
                .ThenBy(path => path.Length)
                .FirstOrDefault();
        }

        private static bool IsRepositoryKey(string keyPath)
        {
            string? licensingDirectory = Path.GetDirectoryName(Path.GetFullPath(keyPath));
            string? repositoryRoot = licensingDirectory == null ? null : Path.GetDirectoryName(licensingDirectory);
            return repositoryRoot != null && File.Exists(Path.Combine(repositoryRoot, "Ouro.csproj"));
        }

        private static void AddIfNotEmpty(List<string> list, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                list.Add(value);
            }
        }

        private static (string Plan, int? Days) ParsePlan(string? value, string? daysValue)
        {
            string option = (value ?? "1").Trim().ToLowerInvariant();

            int? days = null;
            if (!string.IsNullOrWhiteSpace(daysValue) &&
                int.TryParse(daysValue.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedDays))
            {
                if (parsedDays <= 0)
                {
                    throw new ArgumentException("Los días de validez deben ser mayores que cero.");
                }

                days = parsedDays;
            }

            return option switch
            {
                "1" or "m" or "month" or "monthly" or "mensual" => ("Monthly", days ?? 31),
                "2" or "l" or "life" or "lifetime" or "vida" => ("Lifetime", null),
                _ => throw new ArgumentException($"Tipo de licencia no reconocido: '{value}'. Usa 1/Monthly o 2/Lifetime.")
            };
        }

        private static string NormalizeHwid(string hwid) =>
            hwid.Trim().ToUpperInvariant();

        private static string GetDefaultOutputPath(string hwid, string plan)
        {
            string safeHwid = new string(Array.FindAll(hwid.ToCharArray(), char.IsLetterOrDigit));
            if (safeHwid.Length > 16)
            {
                safeHwid = safeHwid[..16];
            }

            string fileName = $"Ouro_{plan}_{safeHwid}_{DateTime.Now:yyyyMMdd}.ourolic";
            return Path.Combine(Environment.CurrentDirectory, fileName);
        }

        private static void SaveLicense(LicenseEnvelope envelope, string path)
        {
            string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, LicenseFormat.Serialize(envelope), new UTF8Encoding(false));
        }

        private static void PrintLicense(LicenseEnvelope envelope, string path)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("==================================================");
            Console.WriteLine($"✦ LICENCIA GENERADA [{envelope.Plan.ToUpperInvariant()}]");
            Console.WriteLine("==================================================");
            Console.ResetColor();
            Console.WriteLine($"Cliente HWID : {envelope.Hwid}");
            Console.WriteLine($"Tipo         : {envelope.Plan}");
            Console.WriteLine($"Emitida      : {FormatUnix(envelope.IssuedAt)}");
            Console.WriteLine($"Vence        : {(envelope.ExpiresAt is > 0 ? FormatUnix(envelope.ExpiresAt.Value) : "Nunca (Lifetime)")}");
            Console.WriteLine($"Archivo      : {Path.GetFullPath(path)}");
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("TOKEN COMPACTO (también puedes entregar el archivo .ourolic):");
            Console.ResetColor();
            Console.WriteLine(LicenseFormat.ToToken(envelope));
        }

        private static void PrintHeader()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("==================================================");
            Console.WriteLine("   OURO REPLAY // EMISOR DE LICENCIAS RSA-2048    ");
            Console.WriteLine("==================================================");
            Console.ResetColor();
        }

        private static void PrintUsage()
        {
            Console.WriteLine();
            Console.WriteLine("Uso:");
            Console.WriteLine("  OuroKeyGen.exe                              Modo interactivo");
            Console.WriteLine("  OuroKeyGen.exe <HWID> [1|2] [días]          Emite una licencia");
            Console.WriteLine("  OuroKeyGen.exe --self                       Licencias de prueba para este equipo");
            Console.WriteLine("  OuroKeyGen.exe --verify <archivo.ourolic>   Verifica una licencia");
            Console.WriteLine();
            Console.WriteLine("Opciones:");
            Console.WriteLine("  --key <ruta>    Clave privada RSA (por defecto LicensingKeys/OuroPrivateKey.xml)");
            Console.WriteLine("  --out <ruta>    Archivo .ourolic de salida");
            Console.WriteLine();
            Console.WriteLine("Planes: 1 = Mensual, 2 = Lifetime");
            Console.WriteLine();
            Console.WriteLine($"Variable de entorno alternativa: {PrivateKeyEnvironmentVariable}");
        }

        private static bool HasFlag(string[] args, params string[] flags)
        {
            foreach (string arg in args)
            {
                foreach (string flag in flags)
                {
                    if (arg.Equals(flag, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static string? GetOptionValue(string[] args, string option)
        {
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg.Equals(option, StringComparison.OrdinalIgnoreCase))
                {
                    return i + 1 < args.Length ? args[i + 1] : null;
                }

                if (arg.StartsWith(option + "=", StringComparison.OrdinalIgnoreCase))
                {
                    return arg[(option.Length + 1)..];
                }
            }

            return null;
        }

        private static List<string> GetPositionalArgs(string[] args)
        {
            var positional = new List<string>();
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg is "--key" or "--out" or "--verify")
                {
                    i++; // Skip the option value.
                    continue;
                }

                if (arg.StartsWith("--key=", StringComparison.OrdinalIgnoreCase) ||
                    arg.StartsWith("--out=", StringComparison.OrdinalIgnoreCase) ||
                    arg.StartsWith("--verify=", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                positional.Add(arg);
            }

            return positional;
        }

        private static string FormatUnix(long unixSeconds) =>
            DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

        /// <summary>
        /// Mirrors Ouro.Services.Licensing.HwidService so self-generated test licenses
        /// bind to the same identifier shown by the client application.
        /// </summary>
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
    }
}
