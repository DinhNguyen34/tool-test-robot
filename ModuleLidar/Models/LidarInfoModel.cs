using Prism.Mvvm;

namespace ModuleLidar.Models
{
    public class LidarInfoModel : BindableBase
    {
        private string _sn = "Unknown";
        public string SN
        {
            get => _sn;
            set => SetProperty(ref _sn, value);
        }

        public uint Handle { get; set; }

        private string _ip = "0.0.0.0";
        public string IP
        {
            get => _ip;
            set => SetProperty(ref _ip, value);
        }

        private string _firmwareVersion = "0.0.0.0";
        public string FirmwareVersion
        {
            get => _firmwareVersion;
            set => SetProperty(ref _firmwareVersion, value);
        }

        private double _temperature = 0;
        public double Temperature
        {
            get => _temperature;
            set => SetProperty(ref _temperature, value);
        }

        private string _workMode = "IDLE";
        public string WorkMode
        {
            get => _workMode;
            set => SetProperty(ref _workMode, value);
        }

        private int _pointPPS = 0;
        public int PointPPS
        {
            get => _pointPPS;
            set => SetProperty(ref _pointPPS, value);
        }

        private int _imuFrequency = 0;
        public int IMUFrequency
        {
            get => _imuFrequency;
            set => SetProperty(ref _imuFrequency, value);
        }

        private int _packetLoss = 0;
        public int PacketLoss
        {
            get => _packetLoss;
            set => SetProperty(ref _packetLoss, value);
        }

        private double _roll = 0;
        public double Roll { get => _roll; set => SetProperty(ref _roll, value); }

        private double _pitch = 0;
        public double Pitch { get => _pitch; set => SetProperty(ref _pitch, value); }

        private double _yaw = 0;
        public double Yaw { get => _yaw; set => SetProperty(ref _yaw, value); }

        private double _accX = 0;
        public double AccX { get => _accX; set => SetProperty(ref _accX, value); }

        private double _accY = 0;
        public double AccY { get => _accY; set => SetProperty(ref _accY, value); }

        private double _accZ = 0;
        public double AccZ { get => _accZ; set => SetProperty(ref _accZ, value); }

        private bool _isRecording = false;
        public bool IsRecording
        {
            get => _isRecording;
            set => SetProperty(ref _isRecording, value);
        }
    }
}
