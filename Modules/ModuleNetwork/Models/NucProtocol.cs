using System;
using System.Net.Sockets;

namespace ModuleNetwork.Models
{
    // ── Enums ──────────────────────────────────────────────────────────────────
    public enum RobotMode { Leg = 0, Arm = 1 }

    // ── Data structures (mirrors nuc_server.py dataclasses) ───────────────────
    public class MotorState
    {
        public float Q             { get; set; }
        public float Dq            { get; set; }
        public float TauEst        { get; set; }
        public float Temperature   { get; set; }
        public int   ErrorCode     { get; set; }
        public int   OperationMode { get; set; }
    }

    public class ImuState
    {
        public float[] Quaternion    { get; set; } = new float[4];
        public float[] Gyroscope     { get; set; } = new float[3];
        public float[] Accelerometer { get; set; } = new float[3];
        public bool    IsValid       { get; set; }
    }

    public class MotorCmd
    {
        public float Q   { get; set; }
        public float Dq  { get; set; }
        public float Tau { get; set; }
        public float Kp  { get; set; }
        public float Kd  { get; set; }
    }

    public class LowState
    {
        public List<MotorState> Motors    { get; set; } = new();
        public ImuState         Imu       { get; set; } = new();
        public bool             CrcOk     { get; set; }
        public double           Timestamp { get; set; }
    }

    public class LowCmd
    {
        public List<MotorCmd> Motors    { get; set; } = new();
        public double         Timestamp { get; set; }
    }

    // ── Protocol: CRC32, packet sizes, parse/build ────────────────────────────
    /// <summary>
    /// Mirrors the packet format and CRC logic in nuc_server.py.
    /// Each motor state  = 4f + 2I = 24 bytes (q, dq, tau_est, temp, err, mode).
    /// Each motor cmd    = 5f + 3I = 32 bytes (q, dq, tau, kp, kd, 0, 0, 0).
    /// State packet      = N*24 + 40 (IMU) + 8 (timestamp+CRC).
    /// Command packet    = N*32 + 8  (reserved+CRC).
    /// CRC32: poly=0x04C11DB7, init=0xFFFFFFFF, finalXor=0xFFFFFFFF.
    /// </summary>
    public static class NucProtocol
    {
        public static int StateSize(int nMotors) => nMotors * 24 + 40 + 8;
        public static int CmdSize  (int nMotors) => nMotors * 32 + 8;

        // CRC32 — identical algorithm to nuc_server.py crc32()
        public static uint Crc32(ReadOnlySpan<byte> data)
        {
            uint crc = 0xFFFFFFFF;
            foreach (byte b in data)
            {
                crc ^= (uint)b << 24;
                for (int i = 0; i < 8; i++)
                    crc = (crc & 0x80000000u) != 0
                        ? ((crc << 1) ^ 0x04C11DB7u)
                        :  (crc << 1);
            }
            return crc ^ 0xFFFFFFFF;
        }

        /// <summary>Parse raw UDP bytes into LowState. Returns null on size/CRC mismatch.</summary>
        public static LowState? ParseLowState(byte[] raw, int nMotors)
        {
            int stateSize = StateSize(nMotors);
            if (raw.Length != stateSize)
                return null;

            uint crcRecv = BitConverter.ToUInt32(raw, stateSize - 4);
            uint crcCalc = Crc32(raw.AsSpan(0, stateSize - 4));
            bool crcOk   = crcRecv == crcCalc;

            var state = new LowState
            {
                CrcOk     = crcOk,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0
            };

            // Motors: each 24 bytes — <4f2I> little-endian
            for (int i = 0; i < nMotors; i++)
            {
                int off = i * 24;
                state.Motors.Add(new MotorState
                {
                    Q             = BitConverter.ToSingle(raw, off),
                    Dq            = BitConverter.ToSingle(raw, off + 4),
                    TauEst        = BitConverter.ToSingle(raw, off + 8),
                    Temperature   = BitConverter.ToSingle(raw, off + 12),
                    ErrorCode     = (int)BitConverter.ToUInt32(raw, off + 16),
                    OperationMode = (int)BitConverter.ToUInt32(raw, off + 20),
                });
            }

            // IMU: offset = N*24, layout: 4f quat + 3f gyro + 3f accel
            int imuOff = nMotors * 24;
            state.Imu = new ImuState
            {
                IsValid = crcOk,
                Quaternion = new[]
                {
                    BitConverter.ToSingle(raw, imuOff),
                    BitConverter.ToSingle(raw, imuOff + 4),
                    BitConverter.ToSingle(raw, imuOff + 8),
                    BitConverter.ToSingle(raw, imuOff + 12),
                },
                Gyroscope = new[]
                {
                    BitConverter.ToSingle(raw, imuOff + 16),
                    BitConverter.ToSingle(raw, imuOff + 20),
                    BitConverter.ToSingle(raw, imuOff + 24),
                },
                Accelerometer = new[]
                {
                    BitConverter.ToSingle(raw, imuOff + 28),
                    BitConverter.ToSingle(raw, imuOff + 32),
                    BitConverter.ToSingle(raw, imuOff + 36),
                },
            };

            return state;
        }

        /// <summary>Build raw UDP bytes from LowCmd with CRC appended.</summary>
        public static byte[] BuildLowCmd(LowCmd cmd, int nMotors)
        {
            int size = CmdSize(nMotors);
            var buf  = new byte[size];

            // Motors: each 32 bytes — <5f3I> little-endian (3 uint padding = 0)
            for (int i = 0; i < Math.Min(cmd.Motors.Count, nMotors); i++)
            {
                int    off = i * 32;
                var    m   = cmd.Motors[i];
                Buffer.BlockCopy(BitConverter.GetBytes(m.Q),   0, buf, off,      4);
                Buffer.BlockCopy(BitConverter.GetBytes(m.Dq),  0, buf, off + 4,  4);
                Buffer.BlockCopy(BitConverter.GetBytes(m.Tau), 0, buf, off + 8,  4);
                Buffer.BlockCopy(BitConverter.GetBytes(m.Kp),  0, buf, off + 12, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(m.Kd),  0, buf, off + 16, 4);
                // bytes [off+20 .. off+31] = 0 (padding, already zeroed)
            }

            // reserved field before CRC = 0 (already zeroed)
            uint crc = Crc32(buf.AsSpan(0, size - 4));
            Buffer.BlockCopy(BitConverter.GetBytes(crc), 0, buf, size - 4, 4);

            return buf;
        }
    }
}
