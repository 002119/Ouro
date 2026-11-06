using System;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Ouro.Services.Licensing
{
    /// <summary>
    /// Signed license envelope shared by the client validator and the offline issuer tool.
    /// The signature covers a deterministic, pipe-delimited payload so both sides can
    /// canonicalize it byte-for-byte without depending on JSON serializer formatting.
    /// </summary>
    public sealed class LicenseEnvelope
    {
        public int Version { get; set; } = LicenseFormat.CurrentVersion;
        public string Product { get; set; } = LicenseFormat.ProductName;
        public string Hwid { get; set; } = string.Empty;
        public string Plan { get; set; } = "Monthly";
        public long IssuedAt { get; set; }
        public long? ExpiresAt { get; set; }
        public string Nonce { get; set; } = string.Empty;
        public string Signature { get; set; } = string.Empty;
    }

    /// <summary>
    /// Canonical license serialization and parsing helpers.
    /// Do not change the payload layout without bumping <see cref="CurrentVersion"/>;
    /// older clients would otherwise reject newly issued licenses.
    /// </summary>
    public static class LicenseFormat
    {
        public const int CurrentVersion = 2;
        public const string ProductName = "Ouro";
        public const string TokenPrefix = "OURO2";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        /// <summary>
        /// Canonical byte sequence signed with RSA PKCS#1 SHA-256.
        /// Layout: Version|Product|HWID|Plan|IssuedAtUnix|ExpiresAtUnix|Nonce
        /// </summary>
        public static string BuildPayload(LicenseEnvelope envelope)
        {
            ArgumentNullException.ThrowIfNull(envelope);

            long expiresAtUnix = envelope.ExpiresAt ?? 0L;
            return string.Join(
                "|",
                envelope.Version.ToString(CultureInfo.InvariantCulture),
                envelope.Product ?? string.Empty,
                envelope.Hwid ?? string.Empty,
                envelope.Plan ?? string.Empty,
                envelope.IssuedAt.ToString(CultureInfo.InvariantCulture),
                expiresAtUnix.ToString(CultureInfo.InvariantCulture),
                envelope.Nonce ?? string.Empty);
        }

        public static byte[] BuildPayloadBytes(LicenseEnvelope envelope) =>
            Encoding.UTF8.GetBytes(BuildPayload(envelope));

        /// <summary>
        /// Parses either a JSON license envelope or a compact OURO2 token.
        /// </summary>
        public static bool TryParse(string? rawLicense, out LicenseEnvelope? envelope, out string error)
        {
            envelope = null;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(rawLicense))
            {
                error = "La licencia está vacía.";
                return false;
            }

            string text = rawLicense.Trim().TrimStart('\uFEFF').Trim();
            if (text.StartsWith(TokenPrefix + ".", StringComparison.OrdinalIgnoreCase))
            {
                return TryParseToken(text, out envelope, out error);
            }

            if (!text.StartsWith("{", StringComparison.Ordinal))
            {
                error = "Formato de licencia no reconocido. Pega el contenido JSON o el token OURO2.";
                return false;
            }

            try
            {
                envelope = JsonSerializer.Deserialize<LicenseEnvelope>(text, JsonOptions);
                if (envelope == null)
                {
                    error = "No se pudo leer la licencia.";
                    return false;
                }

                return true;
            }
            catch (JsonException)
            {
                error = "La licencia no es un JSON válido.";
                return false;
            }
        }

        /// <summary>
        /// Produces the canonical JSON envelope stored on disk. The signature field is required.
        /// </summary>
        public static string Serialize(LicenseEnvelope envelope)
        {
            ArgumentNullException.ThrowIfNull(envelope);
            return JsonSerializer.Serialize(envelope, JsonOptions);
        }

        /// <summary>
        /// Produces the compact token: OURO2.&lt;base64url payload JSON&gt;.&lt;base64url signature&gt;
        /// </summary>
        public static string ToToken(LicenseEnvelope envelope)
        {
            ArgumentNullException.ThrowIfNull(envelope);

            var unsigned = new LicenseEnvelope
            {
                Version = envelope.Version,
                Product = envelope.Product,
                Hwid = envelope.Hwid,
                Plan = envelope.Plan,
                IssuedAt = envelope.IssuedAt,
                ExpiresAt = envelope.ExpiresAt,
                Nonce = envelope.Nonce,
                Signature = string.Empty
            };

            string payloadJson = JsonSerializer.Serialize(unsigned, JsonOptions);
            string payloadPart = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));
            string signaturePart = Base64UrlEncode(Convert.FromBase64String(envelope.Signature));

            return $"{TokenPrefix}.{payloadPart}.{signaturePart}";
        }

        private static bool TryParseToken(string text, out LicenseEnvelope? envelope, out string error)
        {
            envelope = null;
            error = string.Empty;

            string[] parts = text.Split('.');
            if (parts.Length != 3)
            {
                error = "El token de licencia está incompleto.";
                return false;
            }

            try
            {
                byte[] payloadBytes = Base64UrlDecode(parts[1]);
                byte[] signatureBytes = Base64UrlDecode(parts[2]);

                var parsed = JsonSerializer.Deserialize<LicenseEnvelope>(
                    Encoding.UTF8.GetString(payloadBytes), JsonOptions);

                if (parsed == null)
                {
                    error = "El token no contiene una licencia válida.";
                    return false;
                }

                parsed.Signature = Convert.ToBase64String(signatureBytes);
                envelope = parsed;
                return true;
            }
            catch (Exception ex) when (ex is FormatException or JsonException)
            {
                error = "El token de licencia no se pudo decodificar.";
                return false;
            }
        }

        private static string Base64UrlEncode(byte[] data) =>
            Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private static byte[] Base64UrlDecode(string value)
        {
            string padded = value.Replace('-', '+').Replace('_', '/');
            switch (padded.Length % 4)
            {
                case 2: padded += "=="; break;
                case 3: padded += "="; break;
                case 1: throw new FormatException("Longitud Base64Url inválida.");
            }

            return Convert.FromBase64String(padded);
        }
    }
}
