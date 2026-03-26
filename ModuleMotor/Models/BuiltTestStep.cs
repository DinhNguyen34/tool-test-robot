using System;
using System.Collections.Generic;
using System.Text;

namespace ModuleMotor.Models
{
    public class BuiltTestStep
    {
        public int StepNo { get; set; }
        public int TestCaseNumber { get; set; }
        public string Label {  get; set; } = string.Empty;
        public bool IsEnabled { get; set; } = true;
        public CanSendMode SendMode { get; set; } = CanSendMode.SendOnce;
        public int IntervalMs { get; set; } = 2;
        public int RepeatCount { get; set; } = 1;
        public int DelayBeforeMs { get; set; } = 0;
        public int TimeoutMs { get; set; } = 1000;

        public StepRunState State { get; set; } = StepRunState.Pending;
        public string LastResult { get; set; } = string.Empty;
    }
}
