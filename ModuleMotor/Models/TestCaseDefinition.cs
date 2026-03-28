using System;
using System.Collections.Generic;
using System.Configuration;
using System.Security.RightsManagement;
using System.Text;

namespace ModuleMotor.Models
{
    public class TestCaseDefinition : BindableBase
    {
        private int _number;
        public int Number
        {
            get => _number;
            set => SetProperty(ref _number, value);
        }
        private string _code = String.Empty;
        public string Code
        {
            get => _code;
            set => SetProperty(ref _code, value);
        }
        private string _label = String.Empty;
        public string Label
        {
            get => _label;
            set => SetProperty(ref _label, value);
        }
        private string _description = String.Empty;
        public string Description
        {
            get => _description;
            set => SetProperty(ref _description, value);
        }
        private string _canPayLoad = String.Empty;
        public string CanPayLoad
        {
            get => _canPayLoad;
            set => SetProperty(ref _canPayLoad, value);
        }
        private string _expectedRespone = String.Empty;
        public string ExpectedRespone
        {
            get => _expectedRespone;
            set => SetProperty( ref _expectedRespone, value);
        }
        private int _timeoutMs =1000;
        public int TimeoutMs
        {
            get => _timeoutMs;
            set => SetProperty(ref _timeoutMs, value);
        }
        private CanSendMode _defaultSendMode = CanSendMode.SendOnce;
        public CanSendMode DefaultSendMode
        {
            get => _defaultSendMode;
            set => SetProperty(ref _defaultSendMode, value);
        }
        private int _intervalMs = 2;
        public int IntervalMs
        {
            get => _intervalMs;
            set => SetProperty(ref _intervalMs, value);
        }
        private bool _isbuiltIn;
        public bool IsbuiltIn
        {
            get => _isbuiltIn;
            set => SetProperty(ref _isbuiltIn, value);
        }
    }
}
