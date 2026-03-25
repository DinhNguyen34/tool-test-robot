using System;
using System.Collections.Generic;
using System.Security.RightsManagement;
using System.Text;

namespace ModuleMotor.Models
{
    public class TestCaseDefinition
    {
        public int Number { get; set;  }
        public string Code { get; set; } = String .Empty;
        public string Label { get; set; } = String.Empty;
        public string Description { get; set; } = string.Empty;
        
        public string CanPayload {  get; set; } = String.Empty;
        public string ExpectedRespone {  get; set; } = String.Empty;
        public int TimeoutMs { get; set; } = 1000;
        public CanSendMode DefaultSendMode { get; set; } = CanSendMode.SendOnce;
        public int IntervalMs { get; set; } = 2;
        public bool IsbuiltIn { get; set; }
    }
}
