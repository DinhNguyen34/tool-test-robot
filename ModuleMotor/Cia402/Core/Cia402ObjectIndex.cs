namespace ModuleMotor.Cia402.Core
{
    public static class Cia402ObjectIndex
    {
        public const byte ValueSubIndex = 0x00;

        public const ushort ErrorCode = 0x603F;
        public const ushort Controlword = 0x6040;
        public const ushort Statusword = 0x6041;
        public const ushort ModesOfOperation = 0x6060;
        public const ushort ModesOfOperationDisplay = 0x6061;
        public const ushort PositionActualValue = 0x6064;
        public const ushort VelocityActualValue = 0x606C;
     
        public const ushort TargetTorque = 0x6071;
        public const ushort MaxTorque = 0x6072;
        public const ushort TargetPosition = 0x607A;
        public const ushort MaxProfileVelocity = 0x607F;
        public const ushort ProfileAcceleration = 0x6083;
        public const ushort ProfileDeceleration = 0x6084;
        public const ushort QuickStopDeceleration = 0x6085;
        public const ushort TargetVelocity = 0x60FF;
        public const ushort TorqueActualValue = 0x6077;
    }
}
