using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Ouro.Services.Licensing
{
    public sealed class LicenseManager
    {
        // Proprietary cryptographic secret seed (used for HMAC keyed hashing)
        private static readonly byte[] SecretKey = Encoding.UTF8.GetBytes("OURO_REPLAY_ASIC_MASTER_SEC_2026_X99_#MINT");

        // Clean Base32 alphabet (excludes confusing characters: 0, O, 1, I, L)
        private const string Alphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";

        private static readonly string LicenseDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ouro");
        private static readonly string LicenseFilePath = Path.Combine(LicenseDirectory, "license.lic");

        public LicenseModel? CurrentLicense { get; private set; }

        public bool IsLicensed(out LicenseModel? validLicense)
        {
            var result = LoadSavedLicense();
            if (result.IsValid && result.License != null)
            {
                CurrentLicense = result.License;
                validLicense = CurrentLicense;
                return true;
            }

            validLicense = null;
            return false;
        }

        public (bool IsValid, string Message, LicenseModel? License) ValidateAndActivateKey(string rawKey)
        {
            if (string.IsNullOrWhiteSpace(rawKey))
            {
                return (false, "Por favor introduce tu clave de activación.", null);
            }

            // Normalize format: remove dashes, spaces, uppercase
            string clean = rawKey.Trim().ToUpperInvariant().Replace("-", "").Replace(" ", "");
            if (clean.StartsWith("OURO"))
            {
                clean = clean.Substring(4);
            }

            // Expected exact 12-character license body
            if (clean.Length != 12)
            {
                return (false, "La clave debe tener 12 caracteres (ej: OURO-XXXX-XXXX-XXXX).", null);
            }

            try
            {
                string planFlag = clean.Substring(0, 1);     // 'L' for Lifetime, 'M' for Monthly
                string monthCode = clean.Substring(1, 2);    // Encoded month expiry for Monthly, or '99' for Lifetime
                string signaturePart = clean.Substring(3);   // 9-character cryptographic signature

                string currentHwid = HwidService.GetCurrentHwid();
                bool isLifetime = (planFlag == "L");

                DateTime? expiresAt = null;
                if (!isLifetime)
                {
                    if (!int.TryParse(monthCode, out int monthsFromEpoch))
                    {
                        return (false, "Código temporal de licencia inválido.", null);
                    }
                    // Epoch baseline: Jan 2026
                    var epoch = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                    expiresAt = epoch.AddMonths(monthsFromEpoch);

                    if (DateTime.UtcNow > expiresAt.Value)
                    {
                        return (false, $"Esta licencia mensual expiró el {expiresAt.Value:dd/MM/yyyy}.", null);
                    }
                }

                // Verify cryptographic HMAC signature
                string expectedSig = ComputeSignature(currentHwid, planFlag, monthCode);
                if (!string.Equals(signaturePart, expectedSig, StringComparison.OrdinalIgnoreCase))
                {
                    return (false, "Clave incorrecta o no válida para este equipo (HWID Mismatch).", null);
                }

                var model = new LicenseModel
                {
                    Hwid = currentHwid,
                    Plan = isLifetime ? "Lifetime" : "Monthly",
                    CreatedAt = DateTime.UtcNow,
                    ExpiresAt = expiresAt,
                    RawKey = FormatKey(clean)
                };

                SaveLicenseFile(model.RawKey);
                CurrentLicense = model;

                return (true, isLifetime
                    ? "✦ LICENCIA DE POR VIDA ACTIVADA CON ÉXITO ✦"
                    : $"✦ LICENCIA PREMIUM ACTIVA HASTA {model.ExpiresAt:dd/MM/yyyy} ✦", model);
            }
            catch (Exception ex)
            {
                return (false, $"Error al procesar clave: {ex.Message}", null);
            }
        }

        public (bool IsValid, LicenseModel? License) LoadSavedLicense()
        {
            if (!File.Exists(LicenseFilePath))
            {
                return (false, null);
            }

            try
            {
                string rawKey = File.ReadAllText(LicenseFilePath).Trim();
                var result = ValidateAndActivateKey(rawKey);
                return (result.IsValid, result.License);
            }
            catch
            {
                return (false, null);
            }
        }

        private static void SaveLicenseFile(string rawKey)
        {
            try
            {
                Directory.CreateDirectory(LicenseDirectory);
                File.WriteAllText(LicenseFilePath, rawKey);
            }
            catch { }
        }

        public static string ComputeSignature(string hwid, string planFlag, string monthCode)
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

        public static string FormatKey(string raw12)
        {
            return $"OURO-{raw12.Substring(0, 4)}-{raw12.Substring(4, 4)}-{raw12.Substring(8, 4)}";
        }
    }
}
