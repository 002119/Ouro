using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Ouro.Services.Licensing
{
    /// <summary>
    /// Validates offline licenses signed with the project's RSA-2048 private key.
    /// The client only embeds the public key, so a leaked binary cannot issue licenses.
    /// </summary>
    public sealed class LicenseManager
    {
        private static readonly string LicenseDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ouro");

        private static readonly string LicenseFilePath = Path.Combine(LicenseDirectory, "license.ourolic");

        /// <summary>
        /// Last license successfully validated during this process lifetime.
        /// </summary>
        public LicenseModel? CurrentLicense { get; private set; }

        /// <summary>
        /// Loads the persisted license and returns true only if its signature, product,
        /// hardware binding and expiry are all valid.
        /// </summary>
        public bool IsLicensed(out LicenseModel? validLicense)
        {
            var result = LoadSavedLicense();
            if (result.IsValid && result.License != null)
            {
                CurrentLicense = result.License;
                validLicense = result.License;
                return true;
            }

            validLicense = null;
            return false;
        }

        /// <summary>
        /// Verifies a license without activating or persisting it. Useful for diagnostics
        /// and for the activation UI to report precise errors before writing to disk.
        /// </summary>
        public (bool IsValid, string Message, LicenseModel? License) InspectLicense(string rawLicense) =>
            ValidateLicense(rawLicense);

        /// <summary>
        /// Verifies a license pasted by the user (JSON envelope or OURO2 token) and,
        /// on success, stores it in %APPDATA%\Ouro\license.ourolic.
        /// </summary>
        public (bool IsValid, string Message, LicenseModel? License) ValidateAndActivateLicense(string rawLicense)
        {
            var result = ValidateLicense(rawLicense);
            if (!result.IsValid || result.License == null)
            {
                return result;
            }

            try
            {
                PersistLicense(result.License.RawLicense);
            }
            catch (Exception ex)
            {
                return (false, $"No se pudo guardar la licencia en el equipo: {ex.Message}", null);
            }

            CurrentLicense = result.License;
            return result;
        }

        /// <summary>
        /// Re-validates the license stored in the user profile without writing to disk.
        /// </summary>
        public (bool IsValid, LicenseModel? License) LoadSavedLicense()
        {
            if (!File.Exists(LicenseFilePath))
            {
                return (false, null);
            }

            try
            {
                string rawLicense = File.ReadAllText(LicenseFilePath, Encoding.UTF8);
                var result = ValidateLicense(rawLicense);
                return (result.IsValid, result.License);
            }
            catch
            {
                return (false, null);
            }
        }

        private static (bool IsValid, string Message, LicenseModel? License) ValidateLicense(string rawLicense)
        {
            if (!LicenseFormat.TryParse(rawLicense, out var envelope, out string parseError) || envelope == null)
            {
                return (false, parseError, null);
            }

            if (!VerifySignature(envelope))
            {
                return (false, "La firma digital de la licencia no es válida.", null);
            }

            if (envelope.Version != LicenseFormat.CurrentVersion)
            {
                return (false, $"Versión de licencia no soportada ({envelope.Version}).", null);
            }

            if (!string.Equals(envelope.Product, LicenseFormat.ProductName, StringComparison.OrdinalIgnoreCase))
            {
                return (false, "Esta licencia no pertenece a Ouro.", null);
            }

            string currentHwid = HwidService.GetCurrentHwid();
            if (!string.Equals(NormalizeHwid(envelope.Hwid), NormalizeHwid(currentHwid), StringComparison.Ordinal))
            {
                return (false, "La licencia está vinculada a otro equipo (HWID distinto).", null);
            }

            if (string.Equals(envelope.Plan, "Lifetime", StringComparison.OrdinalIgnoreCase))
            {
                return (false, "Las licencias Lifetime están deshabilitadas por ahora. Usa una licencia anual de 1 año.", null);
            }

            bool isAnnual = string.Equals(envelope.Plan, "Annual", StringComparison.OrdinalIgnoreCase)
                || string.Equals(envelope.Plan, "Anual", StringComparison.OrdinalIgnoreCase)
                || string.Equals(envelope.Plan, "Monthly", StringComparison.OrdinalIgnoreCase);
            if (!isAnnual)
            {
                return (false, "Tipo de licencia desconocido.", null);
            }

            if (envelope.IssuedAt <= 0)
            {
                return (false, "La licencia no tiene una fecha de emisión válida.", null);
            }

            DateTime issuedAt;
            try
            {
                issuedAt = DateTimeOffset.FromUnixTimeSeconds(envelope.IssuedAt).UtcDateTime;
            }
            catch
            {
                return (false, "La fecha de emisión de la licencia está fuera de rango.", null);
            }

            // Allow a small clock skew so a freshly issued license is not rejected
            // on machines that are a few hours behind.
            if (issuedAt > DateTime.UtcNow.AddDays(1))
            {
                return (false, "La licencia tiene una fecha de emisión futura.", null);
            }

            if (!envelope.ExpiresAt.HasValue || envelope.ExpiresAt.Value <= 0)
            {
                return (false, "La licencia anual no tiene fecha de vencimiento.", null);
            }

            DateTime expiresAt;
            try
            {
                expiresAt = DateTimeOffset.FromUnixTimeSeconds(envelope.ExpiresAt.Value).UtcDateTime;
            }
            catch
            {
                return (false, "La fecha de vencimiento de la licencia está fuera de rango.", null);
            }

            if (expiresAt <= issuedAt)
            {
                return (false, "La fecha de vencimiento es anterior a la emisión.", null);
            }

            if (DateTime.UtcNow > expiresAt)
            {
                return (false, $"Esta licencia expiró el {expiresAt.ToLocalTime():dd/MM/yyyy}.", null);
            }

            var model = new LicenseModel
            {
                Version = envelope.Version,
                Hwid = currentHwid,
                Plan = "Annual",
                IssuedAt = issuedAt,
                ExpiresAt = expiresAt,
                Nonce = envelope.Nonce ?? string.Empty,
                RawLicense = LicenseFormat.Serialize(envelope)
            };

            string successMessage = $"✦ LICENCIA ANUAL ACTIVA HASTA {expiresAt:dd/MM/yyyy} ✦";

            return (true, successMessage, model);
        }

        private static bool VerifySignature(LicenseEnvelope envelope)
        {
            if (string.IsNullOrWhiteSpace(envelope.Signature))
            {
                return false;
            }

            try
            {
                byte[] signature = Convert.FromBase64String(envelope.Signature);

                using var rsa = RSA.Create();
                rsa.FromXmlString(OuroLicensePublicKey.Xml);

                return rsa.VerifyData(
                    LicenseFormat.BuildPayloadBytes(envelope),
                    signature,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1);
            }
            catch
            {
                return false;
            }
        }

        private static void PersistLicense(string rawLicense)
        {
            Directory.CreateDirectory(LicenseDirectory);
            File.WriteAllText(LicenseFilePath, rawLicense, new UTF8Encoding(false));
        }

        private static string NormalizeHwid(string? hwid) =>
            (hwid ?? string.Empty).Trim().ToUpperInvariant();
    }
}
