namespace ModuleMotor.Cia402.Core
{
    public readonly record struct Cia402Statusword(ushort Value)
    {
        public bool ReadyToSwitchOn => (Value & (1 << 0)) != 0;
        public bool SwitchedOn => (Value & (1 << 1)) != 0;
        public bool OperationEnabled => (Value & (1 << 2)) != 0;
        public bool Fault => (Value & (1 << 3)) != 0;
        public bool VoltageEnabled => (Value & (1 << 4)) != 0;
        public bool QuickStop => (Value & (1 << 5)) != 0;
        public bool SwitchOnDisabled => (Value & (1 << 6)) != 0;
        public bool Warning => (Value & (1 << 7)) != 0;
        public bool Remote => (Value & (1 << 9)) != 0;
        public bool TargetReached => (Value & (1 << 10)) != 0;
        public bool InternalLimitActive => (Value & (1 << 11)) != 0;
        // eRob does not support Homing mode, so bits 12/13 are interpreted only
        // for the supported position-oriented modes.
        public bool SetPointAcknowledge => (Value & (1 << 12)) != 0;
        public bool FollowingError => (Value & (1 << 13)) != 0;

        public string ToDisplayText()
        {
            if (Fault)
                return "Fault";

            if (OperationEnabled)
                return "Operation enabled";

            if (SwitchedOn)
                return "Switched on";

            if (ReadyToSwitchOn)
                return "Ready to switch on";

            if (SwitchOnDisabled)
                return "Switch on disabled";

            return "Not ready to switch on";
        }
    }
}
