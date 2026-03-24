using System.Windows.Input;

namespace ModuleMotor.Models
{
    public class TestCaseItem
    {
        public int    Number  { get; set; }
        public string Label   { get; set; } = string.Empty;
        public ICommand Command { get; set; } = null!;
    }
}
