using System.Globalization;

namespace ModuleMotor.Models
{
    /// <summary>
    /// Builds and decodes the Robstride 29-bit extended CAN ID.
    ///
    /// Bit layout:
    ///   [28:24]  mode     (5 bits)  — communication type
    ///   [23:8]   data     (16 bits) — e.g. torque encoded as uint16 for mode 1
    ///   [7:0]    motorId  (8 bits)  — device address
    /// </summary>
    public static class RobstrideCanId
    {
        private const uint Mask29 = 0x1FFF_FFFF;

        /// <summary>Assemble the 29-bit CAN ID from its three fields.</summary>
        public static uint Build(byte mode, ushort data, byte motorId)
            => (((uint)mode & 0x1F) << 24)
             | (((uint)data  & 0xFFFF) << 8)
             | motorId;

        /// <summary>Same as Build but returns a hex string ready for SendMessage.</summary>
        public static string BuildHex(byte mode, ushort data, byte motorId)
            => $"0x{Build(mode, data, motorId):X8}";

        /// <summary>Extract the three fields from a raw 29-bit value.</summary>
        public static void Decode(uint rawId, out byte mode, out ushort data, out byte motorId)
        {
            rawId   &= Mask29;
            mode     = (byte)((rawId >> 24) & 0x1F);
            data     = (ushort)((rawId >> 8) & 0xFFFF);
            motorId  = (byte)(rawId & 0xFF);
        }

        /// <summary>
        /// Parse a motor device ID from a config string such as "0x01" or "1".
        /// Returns 0 if the string cannot be parsed.
        /// </summary>
        public static byte ParseDeviceId(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return 0;
            value = value.Trim();
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return byte.TryParse(value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var h) ? h : (byte)0;
            return byte.TryParse(value, out var d) ? d : (byte)0;
        }
    }
}
