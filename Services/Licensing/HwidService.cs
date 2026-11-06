using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace Ouro.Services.Licensing
{
    /// <summary>
    /// Generates a stable, tamper-resistant Hardware Identifier (HWID)
    /// based on immutable machine and processor characteristics.
    /// </summary>
    public static class HwidService
    {
        private static string? _cachedHwid;

        public static string GetCurrentHwid()
        {
            if (!string.IsNullOrEmpty(_cachedHwid))
            {
                return _cachedHwid;
            }

            var sb = new StringBuilder();

            // 1. Windows Machine GUID (Unique per OS installation / hardware)
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
                string? machineGuid = key?.GetValue("MachineGuid")?.ToString();
                sb.Append(machineGuid ?? "GUID_FALLBACK");
            }
            catch
            {
                sb.Append("GUID_UNAVAILABLE");
            }
            sb.Append('|');

            // 2. Motherboard / BIOS Identifiers
            try
            {
                using var biosKey = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
                string? baseboardSerial = biosKey?.GetValue("BaseBoardSerialNumber")?.ToString();
                string? systemSerial = biosKey?.GetValue("SystemSerialNumber")?.ToString();
                string? biosVersion = biosKey?.GetValue("BIOSVersion")?.ToString();

                sb.Append(baseboardSerial ?? "MB_NONE");
                sb.Append(':');
                sb.Append(systemSerial ?? "SYS_NONE");
                sb.Append(':');
                sb.Append(biosVersion ?? "BIOS_NONE");
            }
            catch
            {
                sb.Append("BIOS_UNAVAILABLE");
            }
            sb.Append('|');

            // 3. Central Processor Identifier
            try
            {
                using var cpuKey = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                string? cpuName = cpuKey?.GetValue("ProcessorNameString")?.ToString();
                string? cpuIdentifier = cpuKey?.GetValue("Identifier")?.ToString();

                sb.Append(cpuName ?? "CPU_NONE");
                sb.Append(':');
                sb.Append(cpuIdentifier ?? "CPU_ID_NONE");
            }
            catch
            {
                sb.Append("CPU_UNAVAILABLE");
            }

            // 4. Compute SHA-256 and format as OURO-XXXX-XXXX-XXXX-XXXX
            byte[] rawBytes = Encoding.UTF8.GetBytes(sb.ToString());
            byte[] hash = SHA256.HashData(rawBytes);
            string hex = Convert.ToHexString(hash);

            _cachedHwid = $"OURO-{hex[..4]}-{hex[4..8]}-{hex[8..12]}-{hex[12..16]}";
            return _cachedHwid;
        }
    }
}
