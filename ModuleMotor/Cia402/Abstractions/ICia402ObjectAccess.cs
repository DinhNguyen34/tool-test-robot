namespace ModuleMotor.Cia402.Abstractions
{
    public interface ICia402ObjectAccess
    {
        Task<ObjectValue> ReadAsync(ushort index, byte subIndex, CancellationToken ct);
        Task WriteAsync(ushort index, byte subIndex, ObjectValue value, CancellationToken ct);
    }
}
