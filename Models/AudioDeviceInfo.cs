using System;

namespace Ouro.Models
{
    public class AudioDeviceInfo
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";

        public override bool Equals(object? obj)
        {
            if (obj is AudioDeviceInfo other)
            {
                return string.Equals(Id, other.Id, StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }

        public override int GetHashCode()
        {
            return StringComparer.OrdinalIgnoreCase.GetHashCode(Id ?? "");
        }

        public override string ToString() => Name;
    }
}
