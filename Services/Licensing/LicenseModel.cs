using System;

namespace Ouro.Services.Licensing
{
    /// <summary>
    /// Validated license metadata held in memory after a successful signature verification.
    /// </summary>
    public sealed class LicenseModel
    {
        public int Version { get; set; } = 2;
        public string Hwid { get; set; } = string.Empty;
        public string Plan { get; set; } = "Annual"; // "Annual" ("Monthly" legacy wire supported)
        public DateTime IssuedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public string Nonce { get; set; } = string.Empty;
        public string RawLicense { get; set; } = string.Empty;

        public bool IsExpired => !ExpiresAt.HasValue || DateTime.UtcNow > ExpiresAt.Value;

        public int DaysRemaining
        {
            get
            {
                if (!ExpiresAt.HasValue) return 0;
                var delta = ExpiresAt.Value - DateTime.UtcNow;
                return delta <= TimeSpan.Zero ? 0 : (int)Math.Ceiling(delta.TotalDays);
            }
        }
    }
}
