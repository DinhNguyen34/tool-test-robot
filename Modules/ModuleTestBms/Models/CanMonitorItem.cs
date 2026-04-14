using Prism.Mvvm;
using System.Collections.ObjectModel;

namespace ModuleTestBms.Models
{
    /// <summary>
    /// Represents a decoded signal value displayed under a CAN message row.
    /// </summary>
    public class CanSignalItem : BindableBase
    {
        private string _signalName = string.Empty;
        private double _physicalValue;
        private string _rawValue = string.Empty;
        private string _unit = string.Empty;
        private string _description = string.Empty;

        public string SignalName
        {
            get => _signalName;
            set => SetProperty(ref _signalName, value);
        }

        public double PhysicalValue
        {
            get => _physicalValue;
            set => SetProperty(ref _physicalValue, value);
        }

        public string RawValue
        {
            get => _rawValue;
            set => SetProperty(ref _rawValue, value);
        }

        public string Unit
        {
            get => _unit;
            set => SetProperty(ref _unit, value);
        }

        public string Description
        {
            get => _description;
            set => SetProperty(ref _description, value);
        }

        public string DisplayText => $"{SignalName} = {PhysicalValue} {Unit}  (0x{RawValue})";

        public void NotifyDisplayTextChanged() => RaisePropertyChanged(nameof(DisplayText));
    }

    /// <summary>
    /// Represents a single CAN message row in the monitor, expandable to show signals.
    /// </summary>
    public class CanMonitorItem : BindableBase
    {
        private uint _messageId;
        private string _messageName = string.Empty;
        private int _dataLen;
        private string _dataHex = string.Empty;
        private string _cyclic = "-";
        private bool _isExpanded;
        private double _lastTimestamp;

        public uint MessageId
        {
            get => _messageId;
            set => SetProperty(ref _messageId, value);
        }

        public string MessageIdHex => $"0x{MessageId:X3}";

        public string MessageName
        {
            get => _messageName;
            set => SetProperty(ref _messageName, value);
        }

        public int DataLen
        {
            get => _dataLen;
            set => SetProperty(ref _dataLen, value);
        }

        public string DataHex
        {
            get => _dataHex;
            set => SetProperty(ref _dataHex, value);
        }

        public string Cyclic
        {
            get => _cyclic;
            set => SetProperty(ref _cyclic, value);
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
        }

        public double LastTimestamp
        {
            get => _lastTimestamp;
            set => SetProperty(ref _lastTimestamp, value);
        }

        public ObservableCollection<CanSignalItem> Signals { get; } = [];
    }
}
