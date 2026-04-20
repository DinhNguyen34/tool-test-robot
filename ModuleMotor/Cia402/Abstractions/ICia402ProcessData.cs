namespace ModuleMotor.Cia402.Abstractions
{
    public interface ICia402ProcessData
    {
        ValueTask<ProcessSnapshot> ReadAsync(CancellationToken ct);
        ValueTask WriteAsync(ProcessCommand command, CancellationToken ct);
    }
}
