using Prism.Mvvm;

namespace ModuleMotor.Models
{
    public class MotorChannelModel : BindableBase
    {
        public int Id { get; }
        public string Label => $"M{Id:D2}";

        // ── Command inputs ─────────────────────────────────────────────────────
        private bool _enabled = true;
        public bool Enabled { get => _enabled; set => SetProperty(ref _enabled, value); }

        private double _qCmd;
        public double QCmd { get => _qCmd; set => SetProperty(ref _qCmd, value); }

        private double _dqCmd;
        public double DqCmd { get => _dqCmd; set => SetProperty(ref _dqCmd, value); }

        private double _tauCmd;
        public double TauCmd { get => _tauCmd; set => SetProperty(ref _tauCmd, value); }

        private double _kp = 20.0;
        public double Kp { get => _kp; set => SetProperty(ref _kp, value); }

        private double _kd = 1.0;
        public double Kd { get => _kd; set => SetProperty(ref _kd, value); }

        // ── State feedback ─────────────────────────────────────────────────────
        private double _q;
        public double Q { get => _q; set => SetProperty(ref _q, value); }

        private double _dq;
        public double Dq { get => _dq; set => SetProperty(ref _dq, value); }

        private double _tau;
        public double Tau { get => _tau; set => SetProperty(ref _tau, value); }

        private double _temperature;
        public double Temperature { get => _temperature; set => SetProperty(ref _temperature, value); }

        private int _errorCode;
        public int ErrorCode
        {
            get => _errorCode;
            set
            {
                SetProperty(ref _errorCode, value);
                RaisePropertyChanged(nameof(ErrorText));
                RaisePropertyChanged(nameof(HasError));
            }
        }

        public string ErrorText => ErrorCode == 0 ? "OK" : $"0x{ErrorCode:X4}";
        public bool HasError => ErrorCode != 0;

        public MotorChannelModel(int id) { Id = id; }
    }
}
