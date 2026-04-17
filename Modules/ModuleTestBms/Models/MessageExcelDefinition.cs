using System;
using System.Collections.Generic;
using System.Text;

namespace ModuleTestBms.Models
{
    public class MessageExcelDefinition
    {
        public string MessageName { get; set; }
        public uint MessageId { get; set; }
        public List<SignalDefinition> Signals { get; set; } = new List<SignalDefinition>();
    }

    public class SignalDefinition
    {
        public string SignalName { get; set; }
        public int StartBit { get; set; }
        public int Length { get; set; }
        public string ByteOrder { get; set; } // "Intel" hoặc "Motorola"
        public string DataType { get; set; }  // "Signed" hoặc "Unsigned"
        public double Factor { get; set; }
        public double Offset { get; set; }
        public string Unit { get; set; }
    }
}
