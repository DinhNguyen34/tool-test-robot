namespace ModuleMotor.Cia402.Abstractions
{
    public readonly record struct ProcessSnapshot(
        ushort Statusword,
        sbyte OperationModeDisplay,
        int PositionActualValue,
        int VelocityActualValue,
        short TorqueActualValue,
        ushort ErrorCode = 0);
}
