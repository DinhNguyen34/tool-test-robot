using Prism.Mvvm;
using System.Collections.ObjectModel;

namespace ModuleTestLed.Models
{
    public class LedItem : BindableBase
    {
        public int Port { get; set; }
        public int Address { get; set; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public string DisplayName => Address.ToString();
    }

    public class PortLedGroup : BindableBase
    {
        public int PortIndex { get; set; }
        public string PortName => $"Port {PortIndex}";
        public ObservableCollection<LedItem> Leds { get; } = new();

        private bool _isAllSelected;
        public bool IsAllSelected
        {
            get => _isAllSelected;
            set
            {
                if (SetProperty(ref _isAllSelected, value))
                {
                    foreach (var led in Leds)
                        led.IsSelected = value;
                }
            }
        }
    }
}
