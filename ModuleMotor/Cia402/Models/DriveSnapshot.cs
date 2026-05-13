namespace ModuleMotor.Cia402.Models
{
    public sealed record DriveSnapshot(
        ushort Statusword,
        double Position,
        double Velocity,
        double Torque,
        double Temperature,
        ushort ErrorCode,
        string StatusText);
}
