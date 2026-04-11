using System.Buffers.Binary;
using System.Globalization;

namespace ModuleMotor.Models
{
    public enum EncosReplyKind
    {
        Unknown,
        ServiceReply,
        QueryCanIdReply,
        Feedback,
        ConfigAck,
        QueryReply,
        BrakeStatus
    }

    public record EncosMotorFeedback(
        ushort MotorId,
        byte MessageType,
        byte ErrorCode,
        float PositionRad,
        float SpeedRadPerSec,
        float EffortValue,
        string EffortLabel,
        float TemperatureC,
        float MosTemperatureC);

    public record EncosDecodedReply(
        EncosReplyKind Kind,
        ushort Identifier,
        byte ErrorCode,
        string Summary,
        EncosMotorFeedback? Feedback = null);

    public static class EncosMotorControl
    {
        public const ushort BroadcastServiceId = 0x7FF;

        public const float HybridPositionMinRad = -12.5f;
        public const float HybridPositionMaxRad = 12.5f;
        public const float HybridSpeedMinRadPerSec = -18f;
        public const float HybridSpeedMaxRadPerSec = 18f;
        public const float HybridTorqueMinNm = -30f;
        public const float HybridTorqueMaxNm = 30f;
        public const float HybridKpMin = 0f;
        public const float HybridKpMax = 500f;
        public const float HybridKdMin = 0f;
        public const float HybridKdMax = 5f;

        private const byte HybridMode = 0x00;
        private const byte ServoPositionMode = 0x01;
        private const byte ServoSpeedMode = 0x02;
        private const byte CurrentTorqueBrakeMode = 0x03;
        private const byte ConfigMode = 0x06;
        private const byte QueryMode = 0x07;

        public static ushort ParseDeviceId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return 0;

            value = value.Trim();
            ushort hex;
            bool ok = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? ushort.TryParse(value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out hex)
                : ushort.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out hex);

            if (!ok)
                return 0;

            return (ushort)(hex & 0x7FF);
        }

        public static CanFrameSpec QueryCanId()
            => BuildStandard(BroadcastServiceId, 0xFF, 0xFF, 0x00, 0x82);

        public static CanFrameSpec SetZeroPosition(ushort motorId)
            => BuildStandard(
                BroadcastServiceId,
                (byte)(motorId >> 8),
                (byte)(motorId & 0xFF),
                0x00,
                0x03);

        public static CanFrameSpec SetMotorId(ushort currentMotorId, ushort newMotorId)
            => BuildStandard(
                BroadcastServiceId,
                (byte)(currentMotorId >> 8),
                (byte)(currentMotorId & 0xFF),
                0x00,
                0x04,
                (byte)(newMotorId >> 8),
                (byte)(newMotorId & 0xFF));

        public static CanFrameSpec ResetMotorId()
            => BuildStandard(BroadcastServiceId, 0x7F, 0x7F, 0x00, 0x05, 0x7F, 0x7F);

        public static CanFrameSpec BuildHybridControl(
            ushort motorId,
            float positionRad,
            float speedRadPerSec,
            float torqueNm,
            float kp,
            float kd)
        {
            ulong packed = ((ulong)(HybridMode & 0x07) << 61)
                         | ((ulong)FloatToUint(kp, HybridKpMin, HybridKpMax, 12) << 49)
                         | ((ulong)FloatToUint(kd, HybridKdMin, HybridKdMax, 9) << 40)
                         | ((ulong)FloatToUint(positionRad, HybridPositionMinRad, HybridPositionMaxRad, 16) << 24)
                         | ((ulong)FloatToUint(speedRadPerSec, HybridSpeedMinRadPerSec, HybridSpeedMaxRadPerSec, 12) << 12)
                         | FloatToUint(torqueNm, HybridTorqueMinNm, HybridTorqueMaxNm, 12);

            byte[] payload = new byte[8];
            BinaryPrimitives.WriteUInt64BigEndian(payload, packed);
            return BuildStandard(motorId, payload);
        }

        public static CanFrameSpec BuildServoPositionControl(
            ushort motorId,
            float positionDegrees,
            float speedRpm,
            float currentLimitA,
            byte returnStatus = 1)
        {
            ulong packed = ((ulong)(ServoPositionMode & 0x07) << 61)
                         | ((ulong)BitConverter.SingleToUInt32Bits(positionDegrees) << 29)
                         | ((ulong)Math.Clamp((int)MathF.Round(speedRpm * 10f), 0, 0x7FFF) << 14)
                         | ((ulong)Math.Clamp((int)MathF.Round(currentLimitA * 10f), 0, 0x0FFF) << 2)
                         | (uint)(returnStatus & 0x03);

            byte[] payload = new byte[8];
            BinaryPrimitives.WriteUInt64BigEndian(payload, packed);
            return BuildStandard(motorId, payload);
        }

        public static CanFrameSpec BuildServoSpeedControl(
            ushort motorId,
            float speedRpm,
            float currentLimitA,
            byte returnStatus = 1)
        {
            byte[] payload = new byte[7];
            payload[0] = BuildHeader(ServoSpeedMode, 0, returnStatus);
            BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(1, 4), BitConverter.SingleToUInt32Bits(speedRpm));
            ushort currentLimit = (ushort)Math.Clamp((int)MathF.Round(currentLimitA * 10f), 0, ushort.MaxValue);
            BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(5, 2), currentLimit);
            return BuildStandard(motorId, payload);
        }

        public static CanFrameSpec BuildCurrentControl(ushort motorId, float currentA, byte returnStatus = 1)
            => BuildMode3Command(motorId, reserveStatus: 0, ScaledToInt16(currentA, 100f), returnStatus);

        public static CanFrameSpec BuildTorqueControl(ushort motorId, float torqueNm, byte returnStatus = 1)
            => BuildMode3Command(motorId, reserveStatus: 1, ScaledToInt16(torqueNm, 100f), returnStatus);

        public static CanFrameSpec BuildElectromagneticBrake(ushort motorId, bool release, byte returnStatus = 1)
            => BuildMode3Command(motorId, reserveStatus: 5, (short)(release ? 1 : 0), returnStatus);

        public static CanFrameSpec BuildSingleValueConfig(
            ushort motorId,
            byte configCode,
            ushort value,
            byte returnStatus = 1)
        {
            byte[] payload = new byte[4];
            payload[0] = BuildHeader(ConfigMode, 0, returnStatus);
            payload[1] = configCode;
            BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(2, 2), value);
            return BuildStandard(motorId, payload);
        }

        public static CanFrameSpec BuildSignedRangeConfig(
            ushort motorId,
            byte configCode,
            short minValue,
            short maxValue,
            byte returnStatus = 1)
        {
            byte[] payload = new byte[6];
            payload[0] = BuildHeader(ConfigMode, 0, returnStatus);
            payload[1] = configCode;
            BinaryPrimitives.WriteInt16BigEndian(payload.AsSpan(2, 2), minValue);
            BinaryPrimitives.WriteInt16BigEndian(payload.AsSpan(4, 2), maxValue);
            return BuildStandard(motorId, payload);
        }

        public static CanFrameSpec BuildUnsignedPairConfig(
            ushort motorId,
            byte configCode,
            ushort firstValue,
            ushort secondValue,
            byte returnStatus = 1)
        {
            byte[] payload = new byte[6];
            payload[0] = BuildHeader(ConfigMode, 0, returnStatus);
            payload[1] = configCode;
            BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(2, 2), firstValue);
            BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(4, 2), secondValue);
            return BuildStandard(motorId, payload);
        }

        public static CanFrameSpec BuildQuery(ushort motorId, byte queryCode)
            => BuildStandard(motorId, 0xE1, queryCode);

        public static bool TryDecodeReply(uint rawId, byte[] payload, out EncosDecodedReply reply)
        {
            ushort identifier = (ushort)(rawId & 0x7FF);
            reply = new EncosDecodedReply(EncosReplyKind.Unknown, identifier, 0, "Unknown ENCOS frame.");

            if (payload.Length == 0)
                return false;

            if (identifier == BroadcastServiceId)
                return TryDecodeServiceReply(payload, out reply);

            byte messageType = (byte)(payload[0] >> 5);
            return messageType switch
            {
                0x01 => TryDecodeType1(identifier, payload, out reply),
                0x02 => TryDecodeType2(identifier, payload, out reply),
                0x03 => TryDecodeType3(identifier, payload, out reply),
                0x04 => TryDecodeConfigAck(identifier, payload, out reply),
                0x05 => TryDecodeQueryReply(identifier, payload, out reply),
                0x06 => TryDecodeBrakeStatus(identifier, payload, out reply),
                _ => false
            };
        }

        private static bool TryDecodeServiceReply(byte[] payload, out EncosDecodedReply reply)
        {
            reply = new EncosDecodedReply(EncosReplyKind.ServiceReply, BroadcastServiceId, 0, "ENCOS service reply.");

            if (payload.Length >= 5 && payload[0] == 0xFF && payload[1] == 0xFF && payload[2] == 0x01)
            {
                ushort motorId = (ushort)((payload[3] << 8) | payload[4]);
                reply = new EncosDecodedReply(
                    EncosReplyKind.QueryCanIdReply,
                    BroadcastServiceId,
                    0,
                    $"ENCOS query CAN ID reply: motor ID 0x{motorId:X3}.");
                return true;
            }

            if (payload.Length >= 4)
            {
                ushort motorId = (ushort)((payload[0] << 8) | payload[1]);
                bool success = payload[2] == 0x01;
                byte commandCode = payload[3];
                string description = commandCode switch
                {
                    0x03 => "set zero position",
                    0x04 => "set CAN ID",
                    0x05 => "reset CAN ID",
                    _ => $"service command 0x{commandCode:X2}"
                };

                reply = new EncosDecodedReply(
                    EncosReplyKind.ServiceReply,
                    BroadcastServiceId,
                    0,
                    $"ENCOS {description}: {(success ? "success" : "failed")} (motor 0x{motorId:X3}).");
                return true;
            }

            return false;
        }

        private static bool TryDecodeType1(ushort motorId, byte[] payload, out EncosDecodedReply reply)
        {
            reply = new EncosDecodedReply(EncosReplyKind.Unknown, motorId, 0, string.Empty);
            if (payload.Length < 8)
                return false;

            ulong packed = BinaryPrimitives.ReadUInt64BigEndian(payload);
            byte error = (byte)((packed >> 58) & 0x07);
            ushort posRaw = (ushort)((packed >> 40) & 0xFFFF);
            ushort speedRaw = (ushort)((packed >> 28) & 0x0FFF);
            ushort currentRaw = (ushort)((packed >> 16) & 0x0FFF);
            byte motorTempRaw = (byte)((packed >> 8) & 0xFF);
            byte mosTempRaw = (byte)(packed & 0xFF);

            var feedback = new EncosMotorFeedback(
                MotorId: motorId,
                MessageType: 1,
                ErrorCode: error,
                PositionRad: UintToFloat(posRaw, HybridPositionMinRad, HybridPositionMaxRad, 16),
                SpeedRadPerSec: UintToFloat(speedRaw, HybridSpeedMinRadPerSec, HybridSpeedMaxRadPerSec, 12),
                EffortValue: currentRaw,
                EffortLabel: "Current(raw12)",
                TemperatureC: DecodeTemperature(motorTempRaw),
                MosTemperatureC: DecodeTemperature(mosTempRaw));

            reply = new EncosDecodedReply(
                EncosReplyKind.Feedback,
                motorId,
                error,
                $"ENCOS Type1 feedback: q={feedback.PositionRad:F4} rad, dq={feedback.SpeedRadPerSec:F4} rad/s, currentRaw={feedback.EffortValue:F0}, temp={feedback.TemperatureC:F1}C, error={DescribeError(error)}.",
                feedback);
            return true;
        }

        private static bool TryDecodeType2(ushort motorId, byte[] payload, out EncosDecodedReply reply)
        {
            reply = new EncosDecodedReply(EncosReplyKind.Unknown, motorId, 0, string.Empty);
            if (payload.Length < 8)
                return false;

            byte error = (byte)(payload[0] & 0x1F);
            float positionDeg = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(1, 4)));
            short currentRaw = BinaryPrimitives.ReadInt16BigEndian(payload.AsSpan(5, 2));
            byte motorTempRaw = payload[7];

            var feedback = new EncosMotorFeedback(
                MotorId: motorId,
                MessageType: 2,
                ErrorCode: error,
                PositionRad: DegreesToRadians(positionDeg),
                SpeedRadPerSec: 0f,
                EffortValue: currentRaw / 100f,
                EffortLabel: "Current(A)",
                TemperatureC: DecodeTemperature(motorTempRaw),
                MosTemperatureC: 0f);

            reply = new EncosDecodedReply(
                EncosReplyKind.Feedback,
                motorId,
                error,
                $"ENCOS Type2 feedback: pos={positionDeg:F3} deg, current={feedback.EffortValue:F2} A, temp={feedback.TemperatureC:F1}C, error={DescribeError(error)}.",
                feedback);
            return true;
        }

        private static bool TryDecodeType3(ushort motorId, byte[] payload, out EncosDecodedReply reply)
        {
            reply = new EncosDecodedReply(EncosReplyKind.Unknown, motorId, 0, string.Empty);
            if (payload.Length < 8)
                return false;

            byte error = (byte)(payload[0] & 0x1F);
            float speedRpm = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(1, 4)));
            short currentRaw = BinaryPrimitives.ReadInt16BigEndian(payload.AsSpan(5, 2));
            byte motorTempRaw = payload[7];

            var feedback = new EncosMotorFeedback(
                MotorId: motorId,
                MessageType: 3,
                ErrorCode: error,
                PositionRad: 0f,
                SpeedRadPerSec: RpmToRadPerSec(speedRpm),
                EffortValue: currentRaw / 100f,
                EffortLabel: "Current(A)",
                TemperatureC: DecodeTemperature(motorTempRaw),
                MosTemperatureC: 0f);

            reply = new EncosDecodedReply(
                EncosReplyKind.Feedback,
                motorId,
                error,
                $"ENCOS Type3 feedback: speed={speedRpm:F3} rpm, current={feedback.EffortValue:F2} A, temp={feedback.TemperatureC:F1}C, error={DescribeError(error)}.",
                feedback);
            return true;
        }

        private static bool TryDecodeConfigAck(ushort motorId, byte[] payload, out EncosDecodedReply reply)
        {
            reply = new EncosDecodedReply(EncosReplyKind.Unknown, motorId, 0, string.Empty);
            if (payload.Length < 3)
                return false;

            byte error = (byte)(payload[0] & 0x1F);
            byte configCode = payload[1];
            byte status = payload[2];
            reply = new EncosDecodedReply(
                EncosReplyKind.ConfigAck,
                motorId,
                error,
                $"ENCOS config ack: code=0x{configCode:X2}, status={(status == 1 ? "success" : "failed")}, error={DescribeError(error)}.");
            return true;
        }

        private static bool TryDecodeQueryReply(ushort motorId, byte[] payload, out EncosDecodedReply reply)
        {
            reply = new EncosDecodedReply(EncosReplyKind.Unknown, motorId, 0, string.Empty);
            if (payload.Length < 2)
                return false;

            byte error = (byte)(payload[0] & 0x1F);
            byte queryCode = payload[1];
            string queryName = queryCode switch
            {
                0x01 => "current position",
                0x02 => "current speed",
                0x03 => "current phase current",
                0x04 => "current power",
                0x05 => "acceleration",
                0x17 => "hybrid KP range",
                0x18 => "hybrid KD range",
                0x19 => "hybrid POS range",
                0x1A => "hybrid SPD range",
                0x1B => "hybrid TOR range",
                0x1C => "hybrid CUR range",
                0x1F => "CAN timeout",
                0x20 => "current loop PI",
                0x21 => "speed loop PI",
                0x22 => "position loop PD",
                0x25 => "brake status",
                _ => $"query 0x{queryCode:X2}"
            };

            reply = new EncosDecodedReply(
                EncosReplyKind.QueryReply,
                motorId,
                error,
                $"ENCOS query reply: {queryName}, data=[{string.Join(" ", payload.Skip(2).Select(b => b.ToString("X2")))}], error={DescribeError(error)}.");
            return true;
        }

        private static bool TryDecodeBrakeStatus(ushort motorId, byte[] payload, out EncosDecodedReply reply)
        {
            reply = new EncosDecodedReply(EncosReplyKind.Unknown, motorId, 0, string.Empty);
            if (payload.Length < 2)
                return false;

            byte error = (byte)(payload[0] & 0x1F);
            byte brakeStatus = payload[1];
            reply = new EncosDecodedReply(
                EncosReplyKind.BrakeStatus,
                motorId,
                error,
                $"ENCOS brake status: {(brakeStatus == 1 ? "released" : "engaged")}, error={DescribeError(error)}.");
            return true;
        }

        private static CanFrameSpec BuildMode3Command(
            ushort motorId,
            byte reserveStatus,
            short value,
            byte returnStatus)
        {
            byte[] payload = new byte[3];
            payload[0] = BuildHeader(CurrentTorqueBrakeMode, reserveStatus, returnStatus);
            BinaryPrimitives.WriteInt16BigEndian(payload.AsSpan(1, 2), value);
            return BuildStandard(motorId, payload);
        }

        private static byte BuildHeader(byte motorMode, byte reserveStatus, byte returnStatus)
            => (byte)(((motorMode & 0x07) << 5) | ((reserveStatus & 0x07) << 2) | (returnStatus & 0x03));

        private static CanFrameSpec BuildStandard(ushort canId, params byte[] payload)
            => new($"0x{(canId & 0x7FF):X3}", payload, false);

        private static ushort FloatToUint(float value, float min, float max, int bits)
        {
            value = Math.Clamp(value, min, max);
            return (ushort)((value - min) / (max - min) * ((1 << bits) - 1));
        }

        private static float UintToFloat(uint value, float min, float max, int bits)
            => min + (value / (float)((1 << bits) - 1) * (max - min));

        private static short ScaledToInt16(float value, float ratio)
            => (short)Math.Clamp((int)MathF.Round(value * ratio), short.MinValue, short.MaxValue);

        private static float DecodeTemperature(byte rawValue)
            => (rawValue - 50f) / 2f;

        private static float DegreesToRadians(float degrees)
            => degrees * (MathF.PI / 180f);

        private static float RpmToRadPerSec(float rpm)
            => rpm * (2f * MathF.PI / 60f);

        public static string DescribeError(byte errorCode)
            => errorCode switch
            {
                0 => "No error",
                1 => "Motor over-heating",
                2 => "Motor over-current",
                3 => "Motor voltage too high",
                4 => "Motor voltage too low",
                5 => "Motor encoder error",
                6 => "Motor brake voltage too high",
                7 => "DRV driver fault",
                _ => $"Unknown error 0x{errorCode:X2}"
            };
    }
}
