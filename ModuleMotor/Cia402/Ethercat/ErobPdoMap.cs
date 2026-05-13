namespace ModuleMotor.Cia402.Ethercat
{
    /// <summary>
    /// Describes one eRob slave's byte layout inside the EtherCAT process image.
    /// This follows the VRH3.1 EtherCAT frame mapping for Slave 1 -> Slave 12.
    /// </summary>
    public sealed class ErobPdoMap
    {
        public const int NotMapped = -1;

        public int OutRegionOffset { get; init; } = 0;
        public int OutStride { get; init; } = 16;

        // RPDO: 0x607A, 0x60FF, 0x6071, 0x6072, 0x6040, 0x6060, 8-bit align.
        public int OutTargetPosition { get; init; } = 0;
        public int OutTargetVelocity { get; init; } = 4;
        public int OutTargetTorque { get; init; } = 8;
        public int OutMaxTorque { get; init; } = 10;
        public int OutControlword { get; init; } = 12;
        public int OutModesOfOperation { get; init; } = 14;
        public int OutBytes { get; init; } = 16;

        public int InRegionOffset { get; init; } = 0;
        public int InStride { get; init; } = 16;

        // TPDO: 0x603F, 0x6041, 0x6064, 0x606C, 0x6077, 0x6061, 8-bit align.
        public int InErrorCode { get; init; } = 0;
        public int InStatusword { get; init; } = 2;
        public int InPositionActualValue { get; init; } = 4;
        public int InVelocityActualValue { get; init; } = 8;
        public int InTorqueActualValue { get; init; } = 12;
        public int InModesOfOperationDisplay { get; init; } = 14;
        public int InBytes { get; init; } = 16;

        public static ErobPdoMap Default { get; } = new();
    }
}
