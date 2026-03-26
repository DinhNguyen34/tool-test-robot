using System;
using System.Collections.Generic;
using System.Security.Permissions;
using System.Text;

namespace ModuleMotor.Models
{
    public class BuiltTestStep : BindableBase
    {
        private int _stepNo;
        public int StepNo 
        {
            get => _stepNo;
            set => SetProperty(ref _stepNo, value);
        }
        private int _testCaseNumber;
        public int TestCaseNumber 
        { 
            get => _testCaseNumber;
            set => SetProperty(ref _testCaseNumber, value); 
        }
        private string _label = string.Empty;
        public string Label 
        {
            get => _label;
            set => SetProperty(ref _label, value);
        }
        private bool _isEnabled = true;
        public bool IsEnabled 
        { 
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value); 
        }
        private CanSendMode _sendMode = CanSendMode.SendOnce;
        public CanSendMode SendMode
        {
            get => _sendMode;
            set => SetProperty(ref _sendMode, value);
        }
        private int _intervalMs = 2;
        public int IntervalMs 
        { 
            get => _intervalMs;
            set => SetProperty(ref _intervalMs, value); 
        }
        private int _repearCount = 1;
        public int RepeatCount 
        {
            get => _repearCount;
            set => SetProperty(ref _repearCount, value);
        }
        private int _delayBeforeMs = 0;
        public int DelayBeforeMs
        {
            get => _delayBeforeMs;
            set => SetProperty(ref _delayBeforeMs, value);
        }
        private int _timeoutMs = 1000;
        public int TimeoutMs
        {
            get => _timeoutMs;
            set => SetProperty(ref _timeoutMs, value);
        }

        private StepRunState _state = StepRunState.Pending;
        public StepRunState State 
        {
            get => _state;
            set => SetProperty(ref _state, value);
        }

        private string _lastResult = string.Empty;
        public string LastResult
        {
            get => _lastResult;
            set => SetProperty(ref _lastResult, value);
        }
    }
}
