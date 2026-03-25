using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace ModuleMotor.Models
{
    public class TestPlan
    {
        public string Name { get; set; } = "New Test Plan";
        public ObservableCollection<BuiltTestStep> Steps { get; set; } = new();
    }
}
