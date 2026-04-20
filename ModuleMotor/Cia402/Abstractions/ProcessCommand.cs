namespace ModuleMotor.Cia402.Abstractions
{
    public readonly record struct ProcessCommand(
        ushort? Controlword = null,
        sbyte? OperationMode = null,
        int? TargetPosition = null,
        int? TargetVelocity = null,
        short? TargetTorque = null);
}
