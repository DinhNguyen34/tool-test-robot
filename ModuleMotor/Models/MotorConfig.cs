namespace ModuleMotor.Models
{
    public class MotorConfig
    {
        public MotorProtocolKind Protocol { get; set; } = MotorProtocolKind.Robstride;
        public string RobotIp    { get; set; } = "192.168.123.10";
        public int    Port       { get; set; } = 8080;
        public int    RefreshHz  { get; set; } = 30;
        public string LogFile    { get; set; } = "motor_log.csv";
        public double DefaultKp  { get; set; } = 20.0;
        public double DefaultKd  { get; set; } = 1.0;
        public string MotorId    { get; set; } = "0x01";
        public string SerialPort { get; set; } = string.Empty;
        public int    BaudRate   { get; set; } = 115200;
        public int    CanBitrateKbps { get; set; } = 1000;
        public bool   UseCanFd   { get; set; }
    }
}
