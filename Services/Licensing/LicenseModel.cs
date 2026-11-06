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
        public string Plan { get; set; } = "Monthly"; // "Monthly" or "Lifetime"
        public DateTime IssuedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public string Nonce { get; set; } = string.Empty;
        public string RawLicense { get; set; } = string.Empty;

        public bool IsLifetime => string.Equals(Plan, "Lifetime", StringComparison.OrdinalIgnoreCase);

        public bool IsExpired => !IsLifetime && ExpiresAt.HasValue && DateTime.UtcNow > ExpiresAt.Value;

        public int DaysRemaining
        {
            get
            {
                if (IsLifetime) return 99999;
                if (!ExpiresAt.HasValue) return 0;
                var delta = ExpiresAt.Value - DateTime.UtcNow;
                return Math.Max(0, (int)delta.TotalDays);
            }
        }
    }
}
