using Prism.Mvvm;

namespace ModuleTestLed.Models
{
    public enum LedTestStatus
    {
        NotRun,
        Running,
        HasRun,
        Pass,
        Fail
    }

    public class LedTestCaseItem : BindableBase
    {
        private bool _isSelected = true;
        private LedTestStatus _status = LedTestStatus.NotRun;
        private string _duration = "-";
        private string _remark = "";

        public int No { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public LedTestStatus Status
        {
            get => _status;
            set
            {
                SetProperty(ref _status, value);
                RaisePropertyChanged(nameof(StatusText));
            }
        }

        public string StatusText => Status switch
        {
            LedTestStatus.NotRun => "Not Run",
            LedTestStatus.Running => "Running",
            LedTestStatus.HasRun => "Has Run",
            LedTestStatus.Pass => "PASS",
            LedTestStatus.Fail => "FAIL",
            _ => "-"
        };

        public string Duration
        {
            get => _duration;
            set => SetProperty(ref _duration, value);
        }

        public string Remark
        {
            get => _remark;
            set => SetProperty(ref _remark, value);
        }
    }
}
