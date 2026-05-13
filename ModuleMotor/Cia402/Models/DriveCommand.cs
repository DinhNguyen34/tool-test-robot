namespace ModuleMotor.Cia402.Models
{
    public abstract record DriveCommand;

    public sealed record FaultResetDriveCommand() : DriveCommand;

    public sealed record ShutdownDriveCommand() : DriveCommand;

    public sealed record SwitchOnDriveCommand() : DriveCommand;

    public sealed record EnableOperationDriveCommand() : DriveCommand;

    public sealed record DisableOperationDriveCommand() : DriveCommand;

    public sealed record QuickStopDriveCommand() : DriveCommand;

    public sealed record SetModeDriveCommand(Cia402OperationMode Mode) : DriveCommand;

    public sealed record SyncActualPositionToTargetDriveCommand() : DriveCommand;

    /// <summary>
    /// Sends a position target using the selected CiA 402 position-command semantics.
    /// Profile Position requires the New Set Point handshake, while CSP writes only
    /// the cyclic target value.
    /// For Profile Position, completion means the drive acknowledged the set-point
    /// handshake; it does not guarantee the motor has already reached the target.
    /// ProfileVelocity (0x6081), ProfileAcceleration (0x6083) and ProfileDeceleration (0x6084)
    /// override the drive's stored profile parameters for this Profile Position move when set;
    /// they are ignored in CSP mode.
    /// </summary>
    public sealed record MoveAbsolutePositionDriveCommand(
        int TargetPosition,
        Cia402PositionCommandMode CommandMode = Cia402PositionCommandMode.ProfilePosition,
        bool ImmediateChange = false,
        TimeSpan AckTimeout = default,
        int? ProfileVelocity = null,
        int? ProfileAcceleration = null,
        int? ProfileDeceleration = null) : DriveCommand;

    public sealed record SetVelocityDriveCommand(int TargetVelocity) : DriveCommand;

    public sealed record SetTorqueDriveCommand(short TargetTorque) : DriveCommand;

    public sealed record WriteCyclicProcessDataDriveCommand(
        int? TargetPosition = null,
        int? TargetVelocity = null,
        short? TargetTorque = null) : DriveCommand;
}
