namespace ModuleMotor.Models
{
    // ── Feedback & fault result types ────────────────────────────────────────

    public record RsMotorFeedback(
        byte  MotorId,
        float Angle,       // rad
        float Speed,       // rad/s
        float Torque,      // Nm
        float Temperature  // °C
    );

    public record RsFaultStatus(
        byte     MotorId,
        uint     FaultBits,
        uint     WarningBits,
        string[] ActiveFaults,
        string[] ActiveWarnings
    );

    // ── Motor profile ─────────────────────────────────────────────────────────

    public record RsMotorProfile(
        string Name,
        float  VMin,  float VMax,
        float  KpMin, float KpMax,
        float  KdMin, float KdMax,
        float  TMin,  float TMax
    );



    public static class RsMotorProfileMap
    {
        private static readonly Dictionary<byte, RsMotorProfile> Map = new()
        {
            [0x01] = RsMotorControl.Motor4,
            [0x02] = RsMotorControl.Motor4,
            [0x03] = RsMotorControl.Motor3,
            [0x04] = RsMotorControl.Motor4,
            [0x05] = RsMotorControl.Motor6,
            [0x06] = RsMotorControl.Motor6,
            [0x07] = RsMotorControl.Motor4,
            [0x08] = RsMotorControl.Motor4,
            [0x09] = RsMotorControl.Motor3,
            [0x0A] = RsMotorControl.Motor4,
            [0x0B] = RsMotorControl.Motor6,
            [0x0C] = RsMotorControl.Motor6,
            [0x0D] = RsMotorControl.Motor3,
            [0x0E] = RsMotorControl.Motor2,
            [0x0F] = RsMotorControl.Motor2,
            [0x10] = RsMotorControl.Motor6,
            [0x11] = RsMotorControl.Motor6,
            [0x12] = RsMotorControl.Motor6,
            [0x13] = RsMotorControl.Motor6,
            [0x14] = RsMotorControl.Motor0,
            [0x15] = RsMotorControl.Motor0,
            [0x16] = RsMotorControl.Motor0,
            [0x17] = RsMotorControl.Motor6,
            [0x18] = RsMotorControl.Motor6,
            [0x19] = RsMotorControl.Motor6,
            [0x1A] = RsMotorControl.Motor6,
            [0x1B] = RsMotorControl.Motor0,
            [0x1C] = RsMotorControl.Motor0,
            [0x1D] = RsMotorControl.Motor0,
            [0x1E] = RsMotorControl.Motor5,
            [0x1F] = RsMotorControl.Motor5,
            [0x20] = RsMotorControl.Motor5
        };
        public static RsMotorProfile Resolve(byte deviceId)
            => Map.TryGetValue(deviceId, out var profile) ? profile : RsMotorControl.Motor4;
    }

    // ── Main class — direct C# port of rs_motor_control.c ────────────────────

    public static class RsMotorControl
    {
        // Position limits (same for all motors)
        private const float P_MIN = -12.57f;
        private const float P_MAX =  12.57f;

        // ── Motor profiles (mirrors rs_motor_control.h) ───────────────────────
        public static readonly RsMotorProfile Motor0 = new("RS Motor0",  -33,  33,    0,  500,  0,   5, -14,   14);
        public static readonly RsMotorProfile Motor2 = new("RS Motor2",  -44,  44,    0,  500,  0,   5, -17,   17);
        public static readonly RsMotorProfile Motor3 = new("RS Motor3",  -20,  20,    0, 5000,  0, 100, -60,   60);
        public static readonly RsMotorProfile Motor4 = new("RS Motor4",  -15,  15,    0, 5000,  0, 100,-120,  120);
        public static readonly RsMotorProfile Motor5 = new("RS Motor5",  -50,  50,    0,  500,  0,   5,  -5.5f,  5.5f);
        public static readonly RsMotorProfile Motor6 = new("RS Motor6",  -50,  50,    0, 5000,  0, 100, -36,   36);

        public static readonly IReadOnlyList<RsMotorProfile> AllProfiles =
            [Motor0, Motor2, Motor3, Motor4, Motor5, Motor6];

        // ── Fault / warning bit masks (mirrors rs_motor_control.h) ───────────
        private const uint PHASE_A_OVERCURRENT_FAULT      = 1u << 16;
        private const uint GRIDLOCK_OVERLOAD_FAULT         = 1u << 14;
        private const uint POSITION_INITIALIZATION_FAULT   = 1u << 9;
        private const uint HARDWARED_ID_FAULT              = 1u << 8;
        private const uint ENCODER_NOT_CALIBRATED_FAULT    = 1u << 7;
        private const uint PHASE_C_OVERCURRENT_FAULT       = 1u << 5;
        private const uint PHASE_B_OVERCURRENT_FAULT       = 1u << 4;
        private const uint OVERVOLTAGE_FAULT               = 1u << 3;
        private const uint UNDERVOLTAGE_FAULT              = 1u << 2;
        private const uint DRIVER_CHIP_FAULT               = 1u << 1;
        private const uint OVERTEMPERATURE_FAULT           = 1u << 0;

        private const uint PHASE_A_OVERCURRENT_WARNING     = 1u << 16;
        private const uint GRIDLOCK_OVERLOAD_WARNING        = 1u << 14;
        private const uint OVERVOLTAGE_WARNING              = 1u << 13;
        private const uint UNDERVOLTAGE_WARNING             = 1u << 12;
        private const uint PHASE_C_OVERCURRENT_WARNING      = 1u << 5;
        private const uint PHASE_B_OVERCURRENT_WARNING      = 1u << 4;
        private const uint OVERTEMPERATURE_WARNING          = 1u << 0;

        // ── Signal conversion (mirrors float_to_uint / uint_to_float) ─────────

        private static ushort FloatToUint(float x, float min, float max, int bits)
        {
            x = Math.Clamp(x, min, max);
            return (ushort)((x - min) / (max - min) * ((1 << bits) - 1));
        }

        private static float UintToFloat(ushort x, float min, float max, int bits)
            => min + (float)x / ((1 << bits) - 1) * (max - min);

        // ── CAN ID encode (mirrors encode_can_id) ─────────────────────────────
        //
        // Bit layout:
        //   [31:29]  reserved (3 bits)
        //   [28:24]  mode     (5 bits)
        //   [23:8]   data     (16 bits) — torque for mode 1
        //   [7:0]    motorId  (8 bits)

        private static uint EncodeCanId(byte mode, ushort data, byte motorId)
            => ((uint)(mode   & 0x1F) << 24)
             | ((uint)(data   & 0xFFFF) << 8)
             | motorId;

        /// <summary>Parse "0x01" or "1" config string into a device byte.</summary>
        public static byte ParseDeviceId(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return 0;
            value = value.Trim();
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return byte.TryParse(value[2..], System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out var h) ? h : (byte)0;
            return byte.TryParse(value, out var d) ? d : (byte)0;
        }

        // ── CAN ID decode (mirrors RS_decode_can_id) ──────────────────────────
        //
        // NOTE: in the feedback frame the motor echoes its own ID in bits[15:8]
        // (lower byte of the data field), NOT in bits[7:0].

        public static void DecodeCanId(uint rawId, out byte motorId, out byte commMode, out byte operation_mode)
        {
            commMode = (byte)((rawId >> 24) & 0x1F);   // bits [28:24]
            motorId  = (byte)((rawId >>  8) & 0xFF);   // bits [15:8]  ← feedback layout
            operation_mode = (byte)((rawId >> 22) & 0x03);     // mode status = 0 -> reset mode; 1: Cali mode; 2: Motor mode[Run]
        }

        // ── TX: utility commands ──────────────────────────────────────────────

        /// <summary>Mode 3 — enable motor. Returns (canId, payload).</summary>
        public static (string CanId, byte[] Payload) EnableMotor(byte motorId)
            => BuildCommand(mode: 3, data: 0, motorId, payload: new byte[8]);

        public static (string CanId, byte[] Payload) GetIdMotor(byte motorId)
            => BuildCommand(mode: 0, data: 0, motorId, payload: new byte[8]);

        /// <summary>Mode 4 — disable motor. Returns (canId, payload).</summary>
        public static (string CanId, byte[] Payload) DisableMotor(byte motorId)
            => BuildCommand(mode: 4, data: 0, motorId, payload: new byte[8]);

        /// <summary>Mode 6 — set zero position. Returns (canId, payload).</summary>
        public static (string CanId, byte[] Payload) SetZeroPosition(byte motorId)
        {
            var payload = new byte[8];
            payload[0] = 1;   // byte 0 = 1 per protocol
            return BuildCommand(mode: 6, data: 0, motorId, payload);
        }

        // ── TX: motor control (mode 1) ────────────────────────────────────────
        // Mirrors RS_Motor0Control … RS_Motor6Control.
        // Torque is encoded in the CAN ID data field; position/speed/Kp/Kd in payload.
        // All signals are big-endian 16-bit scaled integers.

        public static (string CanId, byte[] Payload) ControlMotor(
            byte motorId, RsMotorProfile profile,
            float torque, float position, float speed, float kp, float kd)
        {
            ushort torqueU = FloatToUint(torque,    profile.TMin,  profile.TMax,  16);
            ushort posU    = FloatToUint(position,  P_MIN,         P_MAX,         16);
            ushort speedU  = FloatToUint(speed,     profile.VMin,  profile.VMax,  16);
            ushort kpU     = FloatToUint(kp,        profile.KpMin, profile.KpMax, 16);
            ushort kdU     = FloatToUint(kd,        profile.KdMin, profile.KdMax, 16);

            var payload = new byte[8];
            payload[0] = (byte)(posU   >> 8);  payload[1] = (byte)(posU   & 0xFF);
            payload[2] = (byte)(speedU >> 8);  payload[3] = (byte)(speedU & 0xFF);
            payload[4] = (byte)(kpU    >> 8);  payload[5] = (byte)(kpU    & 0xFF);
            payload[6] = (byte)(kdU    >> 8);  payload[7] = (byte)(kdU    & 0xFF);

            return BuildCommand(mode: 1, data: torqueU, motorId, payload);
        }

        // ── RX: feedback decode (mode 2) ──────────────────────────────────────
        // Mirrors RS_Decode_Motor0_Feedback … RS_Decode_Motor6_Feedback.

        public static RsMotorFeedback DecodeFeedback(
            byte motorId, byte[] data, RsMotorProfile profile)
        {
            ushort rawAngle = (ushort)((data[0] << 8) | data[1]);
            ushort rawSpeed = (ushort)((data[2] << 8) | data[3]);
            ushort rawTorq  = (ushort)((data[4] << 8) | data[5]);
            ushort rawTemp  = (ushort)((data[6] << 8) | data[7]);


            return new RsMotorFeedback(
                MotorId:     motorId,
                Angle:       UintToFloat(rawAngle, P_MIN,         P_MAX,         16),
                Speed:       UintToFloat(rawSpeed, profile.VMin,  profile.VMax,  16),
                Torque:      UintToFloat(rawTorq,  profile.TMin,  profile.TMax,  16),
                Temperature: rawTemp * 0.1f
            );
        }

        // ── RX: fault decode (mode 21) ────────────────────────────────────────
        // Mirrors RS_decode_fault_feedback.
        // buf[0..3] = fault bits (little-endian), buf[4..7] = warning bits.

        public static RsFaultStatus DecodeFault(byte motorId, byte[] buf)
        {
            uint fault   = buf[0] | ((uint)buf[1] << 8) | ((uint)buf[2] << 16) | ((uint)buf[3] << 24);
            uint warning = buf[4] | ((uint)buf[5] << 8) | ((uint)buf[6] << 16) | ((uint)buf[7] << 24);

            var faults   = DecodeFlags(fault,   FaultNames);
            var warnings = DecodeFlags(warning, WarningNames);

            return new RsFaultStatus(motorId, fault, warning, faults, warnings);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static (string CanId, byte[] Payload) BuildCommand(
            byte mode, ushort data, byte motorId, byte[] payload)
        {
            uint raw = EncodeCanId(mode, data, motorId);
            return ($"0x{raw:X8}", payload);
        }

        private static readonly (uint Mask, string Name)[] FaultNames =
        [
            (PHASE_A_OVERCURRENT_FAULT,    "Phase-A Overcurrent"),
            (GRIDLOCK_OVERLOAD_FAULT,      "Gridlock Overload"),
            (POSITION_INITIALIZATION_FAULT,"Position Init"),
            (HARDWARED_ID_FAULT,           "Hardware ID"),
            (ENCODER_NOT_CALIBRATED_FAULT, "Encoder Not Calibrated"),
            (PHASE_C_OVERCURRENT_FAULT,    "Phase-C Overcurrent"),
            (PHASE_B_OVERCURRENT_FAULT,    "Phase-B Overcurrent"),
            (OVERVOLTAGE_FAULT,            "Overvoltage"),
            (UNDERVOLTAGE_FAULT,           "Undervoltage"),
            (DRIVER_CHIP_FAULT,            "Driver Chip"),
            (OVERTEMPERATURE_FAULT,        "Overtemperature"),
        ];

        private static readonly (uint Mask, string Name)[] WarningNames =
        [
            (PHASE_A_OVERCURRENT_WARNING,  "Phase-A Overcurrent"),
            (GRIDLOCK_OVERLOAD_WARNING,    "Gridlock Overload"),
            (OVERVOLTAGE_WARNING,          "Overvoltage"),
            (UNDERVOLTAGE_WARNING,         "Undervoltage"),
            (PHASE_C_OVERCURRENT_WARNING,  "Phase-C Overcurrent"),
            (PHASE_B_OVERCURRENT_WARNING,  "Phase-B Overcurrent"),
            (OVERTEMPERATURE_WARNING,      "Overtemperature"),
        ];

        private static string[] DecodeFlags(uint bits, (uint Mask, string Name)[] table)
            => table.Where(e => (bits & e.Mask) != 0).Select(e => e.Name).ToArray();
    }
}
