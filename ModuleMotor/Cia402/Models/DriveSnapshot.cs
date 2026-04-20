namespace ModuleMotor.Cia402.Models
{
    public sealed record DriveSnapshot(
        double Position,
        double Velocity,
        double Torque,
        double Temperature,
        int ErrorCode,
        string StatusText);
}
