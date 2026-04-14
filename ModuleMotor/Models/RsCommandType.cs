namespace ModuleMotor.Models
{
    public enum RsCommandType
    {
        None,
        GetId,
        QueryCanId,
        Enable,
        Disable,
        SetZero,
        Control,
        PositionControl,
        SpeedControl,
        CurrentControl,
        TorqueControl,
        BrakeRelease,
        BrakeEngage,
        Query,
        ResetMotorId,
    }
}
