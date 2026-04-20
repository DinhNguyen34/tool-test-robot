namespace ModuleMotor.Cia402.Ethercat
{
    /// <summary>
    /// Describes the byte layout of the eRob EtherCAT process data image.
    /// All offsets are relative to the start of the output or input region.
    ///
    /// Defaults correspond to the eRob EtherCAT factory PDO mapping (manual v1.9).
    /// Verify against your actual eRob firmware using:
    ///   RPDO objects: 0x1600 sub-indices
    ///   TPDO objects: 0x1A00 sub-indices
    /// </summary>
    public sealed class ErobPdoMap
    {
        // ── Output (RPDO: master → eRob) ──────────────────────────────────────

        /// <summary>Byte offset in the IO map where the output region starts.</summary>
        public int OutRegionOffset { get; init; } = 0;

        // Object 0x6040: Controlword (UINT16, 2 bytes)
        public int OutControlword { get; init; } = 0;
        // Object 0x6060: Modes of Operation (INT8, 1 byte)
        public int OutModesOfOperation { get; init; } = 2;
        // 1 byte alignment padding at offset 3
        // Object 0x607A: Target Position (INT32, 4 bytes)
        public int OutTargetPosition { get; init; } = 4;
        // Object 0x60FF: Target Velocity (INT32, 4 bytes)
        public int OutTargetVelocity { get; init; } = 8;
        // Object 0x6071: Target Torque (INT16, 2 bytes)
        public int OutTargetTorque { get; init; } = 12;

        /// <summary>Total output region size in bytes.</summary>
        public int OutBytes { get; init; } = 14;

        // ── Input (TPDO: eRob → master) ───────────────────────────────────────

        /// <summary>Byte offset in the IO map where the input region starts.</summary>
        public int InRegionOffset { get; init; } = 14;

        // Object 0x6041: Statusword (UINT16, 2 bytes)
        public int InStatusword { get; init; } = 0;
        // Object 0x6061: Modes of Operation Display (INT8, 1 byte)
        public int InModesOfOperationDisplay { get; init; } = 2;
        // 1 byte alignment padding at offset 3
        // Object 0x6064: Position Actual Value (INT32, 4 bytes)
        public int InPositionActualValue { get; init; } = 4;
        // Object 0x606C: Velocity Actual Value (INT32, 4 bytes)
        public int InVelocityActualValue { get; init; } = 8;
        // Object 0x6077: Torque Actual Value (INT16, 2 bytes)
        public int InTorqueActualValue { get; init; } = 12;
        // Object 0x603F: Error Code (UINT16, 2 bytes)
        public int InErrorCode { get; init; } = 14;

        /// <summary>Total input region size in bytes.</summary>
        public int InBytes { get; init; } = 16;

        /// <summary>Factory-default PDO map for eRob v1.9.</summary>
        public static ErobPdoMap Default { get; } = new ErobPdoMap();
    }
}
