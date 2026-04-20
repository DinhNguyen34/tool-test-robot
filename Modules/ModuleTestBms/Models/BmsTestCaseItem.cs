using Prism.Mvvm;

namespace ModuleTestBms.Models
{
    public enum BmsTestStatus
    {
        NotRun,
        Running,
        HasRun,
        Pass,
        Fail
    }

    public class BmsTestCaseItem : BindableBase
    {
        private bool _isSelected = true;
        private BmsTestStatus _status = BmsTestStatus.NotRun;
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

        public BmsTestStatus Status
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
            BmsTestStatus.NotRun => "Not Run",
            BmsTestStatus.Running => "Running",
            BmsTestStatus.HasRun => "Has Run",
            BmsTestStatus.Pass => "PASS",
            BmsTestStatus.Fail => "FAIL",
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
