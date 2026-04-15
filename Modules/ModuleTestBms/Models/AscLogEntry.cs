using Prism.Mvvm;
using System.Collections.ObjectModel;

namespace ModuleTestBms.Models
{
    /// <summary>
    /// Represents a single parsed CAN message line from a Vector ASC log file.
    /// </summary>
    public class AscLogEntry : BindableBase
    {
        private bool _isExpanded;

        public double AbsoluteTime { get; set; }
        public double DeltaTime { get; set; }
        public string DisplayTime { get; set; } = string.Empty;
        public uint MessageId { get; set; }
        public string MessageIdHex => $"0x{MessageId:X3}";
        public string Direction { get; set; } = string.Empty;
        public string MessageName { get; set; } = string.Empty;
        public int Dlc { get; set; }
        public string DataHex { get; set; } = string.Empty;
        public byte[] Payload { get; set; } = [];
        public ObservableCollection<CanSignalItem> Signals { get; } = [];

        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
        }
    }

    /// <summary>
    /// Helper item for the MsgID filter ComboBox.
    /// </summary>
    public class MsgIdFilterItem
    {
        public uint? MsgId { get; set; }
        public string Display { get; set; } = "All";

        public override string ToString() => Display;
    }
}
