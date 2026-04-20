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
    /// </summary>
    public sealed record MoveAbsolutePositionDriveCommand(
        int TargetPosition,
        Cia402PositionCommandMode CommandMode = Cia402PositionCommandMode.ProfilePosition,
        bool ImmediateChange = false) : DriveCommand;

    public sealed record SetVelocityDriveCommand(int TargetVelocity) : DriveCommand;

    public sealed record SetTorqueDriveCommand(short TargetTorque) : DriveCommand;
}
