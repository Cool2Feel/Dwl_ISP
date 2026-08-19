using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SensorAdjust.Models
{
    public class RegisterEntry : INotifyPropertyChanged
    {
        private string _address = "00";
        private string _value = "00";
        private bool _isSelected;

        public string Address
        {
            get => _address;
            set { _address = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayText)); }
        }

        public string Value
        {
            get => _value;
            set { _value = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayText)); }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public string DisplayText => $"addr: 0x{_address}         value: 0x{_value}";

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}