using System;
using System.Collections.Generic;
using System.Text;

namespace ModuleMotor.Models
{
    public enum StepRunState
    {
        Pending,
        Running,
        Passed,
        Failed,
        Skipped
    }
}
