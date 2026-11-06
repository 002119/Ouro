using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ouro.Services.Licensing
{
    public sealed class LicenseManager
    {
        // Public RSA key used strictly for signature verification (cannot forge signatures)
        private const string PublicKeyXml = "<RSAKeyValue><Modulus>yqWF89uRqvRA7zwc8f8LYBm8OqS3hVHj58AWALkGw+pXyj/hZIIl/pmfXawjyGv2a5Rfekrh6CkGRkVwWXegv1RHLWXFwxTwg/hV78wXCkbpE47ySJvGutc6/0hq7IdJuTkxrSGsmBZPX91wps44Nj4ycTfod/n20FRS0GH9F1cni+APaPjQYPb/9jGqSbl/0o8SOw+V8gMdviOhH0YscKEtscjhzlERBjE2t1hnQVRLwoIx79/Hh0LBq56+F0TsLZZDZkcjbfFiTjH3YDc+IhcBTmmow0uFXTMeV/n83QRotyULksvv3ZbA6Wb6dYcCnSeZLwVPFm8MlKw5iX+5UQ==</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";

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
                return (false, "Por favor introduce una clave de activación.", null);
            }

            rawKey = rawKey.Trim();
            if (!rawKey.StartsWith("OURO-", StringComparison.OrdinalIgnoreCase))
            {
                return (false, "Formato de clave no reconocido. Debe comenzar con OURO-", null);
            }

            try
            {
                string b64Inner = rawKey.Substring(5);
                byte[] innerBytes = Convert.FromBase64String(b64Inner);
                string innerString = Encoding.UTF8.GetString(innerBytes);

                string[] parts = innerString.Split('.');
                if (parts.Length != 2)
                {
                    return (false, "La estructura criptográfica de la clave es inválida.", null);
                }

                byte[] payloadBytes = Convert.FromBase64String(parts[0]);
                byte[] signatureBytes = Convert.FromBase64String(parts[1]);

                // Verify RSA-2048 SHA-256 signature
                using var rsa = RSA.Create();
                rsa.FromXmlString(PublicKeyXml);

                bool isSignatureValid = rsa.VerifyData(
                    payloadBytes,
                    signatureBytes,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1);

                if (!isSignatureValid)
                {
                    return (false, "Firma digital no válida. La clave ha sido adulterada o falsificada.", null);
                }

                string payloadJson = Encoding.UTF8.GetString(payloadBytes);
                using var doc = JsonDocument.Parse(payloadJson);
                var root = doc.RootElement;

                string licHwid = root.GetProperty("hwid").GetString() ?? string.Empty;
                string licPlan = root.GetProperty("plan").GetString() ?? "Monthly";
                string createdStr = root.GetProperty("created").GetString() ?? string.Empty;
                string? expiresStr = root.TryGetProperty("expires", out var expProp) ? expProp.GetString() : null;
                string nonce = root.TryGetProperty("nonce", out var nonceProp) ? nonceProp.GetString() ?? "" : "";

                // Check HWID Match
                string currentHwid = HwidService.GetCurrentHwid();
                if (!string.Equals(licHwid, currentHwid, StringComparison.OrdinalIgnoreCase))
                {
                    return (false, $"Esta licencia pertenece a otro equipo.\nHWID Requerido: {licHwid}\nTu HWID: {currentHwid}", null);
                }

                DateTime createdAt = DateTime.TryParse(createdStr, out var cDate) ? cDate : DateTime.UtcNow;
                DateTime? expiresAt = null;
                if (!string.IsNullOrEmpty(expiresStr) && DateTime.TryParse(expiresStr, out var eDate))
                {
                    expiresAt = eDate;
                }

                var model = new LicenseModel
                {
                    Hwid = licHwid,
                    Plan = licPlan,
                    CreatedAt = createdAt,
                    ExpiresAt = expiresAt,
                    Nonce = nonce,
                    RawKey = rawKey
                };

                // Check Expiration
                if (model.IsExpired)
                {
                    return (false, $"Esta licencia expiró el {model.ExpiresAt:dd/MM/yyyy HH:mm} UTC.", null);
                }

                // Save locally
                SaveLicenseFile(rawKey);
                CurrentLicense = model;

                return (true, model.IsLifetime
                    ? "✦ LICENCIA DE POR VIDA ACTIVADA CON ÉXITO ✦"
                    : $"✦ LICENCIA PREMIUM ACTIVA HASTA {model.ExpiresAt:dd/MM/yyyy} ✦", model);
            }
            catch (Exception ex)
            {
                return (false, $"Error al verificar la clave: {ex.Message}", null);
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving license: {ex.Message}");
            }
        }
    }
}
