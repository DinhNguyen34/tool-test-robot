namespace ModuleMotor.Models
{
    public readonly record struct CanFrameSpec(
        string CanId,
        byte[] Payload,
        bool IsExtendedId,
        bool IsCanFd = false);
}
