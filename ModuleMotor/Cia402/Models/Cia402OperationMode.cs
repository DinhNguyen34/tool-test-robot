namespace ModuleMotor.Cia402.Models
{
    public enum Cia402OperationMode : sbyte
    {
        ProfilePosition = 0x01,
        ProfileVelocity = 0x03,
        ProfileTorque = 0x04,
        CyclicSynchronousPosition = 0x08,
        CyclicSynchronousVelocity = 0x09,
        CyclicSynchronousTorque = 0x0A,
    }
}
