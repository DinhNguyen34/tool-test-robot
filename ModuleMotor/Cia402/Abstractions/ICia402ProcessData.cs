namespace ModuleMotor.Cia402.Abstractions
{
    public interface ICia402ProcessData
    {
        ValueTask<ProcessSnapshot> ReadAsync(CancellationToken ct);
        ValueTask WriteAsync(ProcessCommand command, CancellationToken ct);
    }

    public interface ICia402ProcessDataCapabilities
    {
        bool HasTargetPosition { get; }
        bool HasTargetVelocity { get; }
        bool HasTargetTorque { get; }
        bool HasMaxTorque { get; }
        bool HasControlword { get; }
        bool HasOperationMode { get; }
        bool HasOperationModeDisplay { get; }
        bool HasVelocityActualValue { get; }
        bool HasTorqueActualValue { get; }
        bool HasErrorCode { get; }
    }
}
