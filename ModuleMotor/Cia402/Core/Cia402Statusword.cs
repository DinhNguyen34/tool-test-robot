namespace ModuleMotor.Cia402.Core
{
    public enum Cia402DriveState
    {
        NotReadyToSwitchOn,
        SwitchOnDisabled,
        ReadyToSwitchOn,
        SwitchedOn,
        OperationEnabled,
        QuickStopActive,
        FaultReactionActive,
        Fault,
        Unknown
    }

    public readonly record struct Cia402Statusword(ushort Value)
    {
        public bool ReadyToSwitchOn => (Value & (1 << 0)) != 0;
        public bool SwitchedOn => (Value & (1 << 1)) != 0;
        public bool OperationEnabled => (Value & (1 << 2)) != 0;
        public bool Fault => (Value & (1 << 3)) != 0;
        public bool VoltageEnabled => (Value & (1 << 4)) != 0;
        public bool QuickStopNotActive => (Value & (1 << 5)) != 0;
        public bool SwitchOnDisabled => (Value & (1 << 6)) != 0;
        public bool Warning => (Value & (1 << 7)) != 0;
        public bool Remote => (Value & (1 << 9)) != 0;
        public bool TargetReached => (Value & (1 << 10)) != 0;
        public bool InternalLimitActive => (Value & (1 << 11)) != 0;
        // eRob does not support Homing mode, so bits 12/13 are interpreted only
        // for the supported position-oriented modes.
        public bool SetPointAcknowledge => (Value & (1 << 12)) != 0;
        public bool FollowingError => (Value & (1 << 13)) != 0;

        [Obsolete("Bit 5 is true when Quick Stop is not active. Use QuickStopNotActive instead.")]
        public bool QuickStop => QuickStopNotActive;

        public Cia402DriveState DecodeState()
        {
            ushort stateWithoutQuickStop = (ushort)(Value & 0x004F);
            switch (stateWithoutQuickStop)
            {
                case 0x0000:
                    return Cia402DriveState.NotReadyToSwitchOn;
                case 0x0040:
                    return Cia402DriveState.SwitchOnDisabled;
                case 0x000F:
                    return Cia402DriveState.FaultReactionActive;
                case 0x0008:
                    return Cia402DriveState.Fault;
            }

            ushort stateWithQuickStop = (ushort)(Value & 0x006F);
            return stateWithQuickStop switch
            {
                0x0021 => Cia402DriveState.ReadyToSwitchOn,
                0x0023 => Cia402DriveState.SwitchedOn,
                0x0027 => Cia402DriveState.OperationEnabled,
                0x0007 => Cia402DriveState.QuickStopActive,
                _ => Cia402DriveState.Unknown
            };
        }

        public string ToDisplayText()
        {
            return DecodeState() switch
            {
                Cia402DriveState.NotReadyToSwitchOn => "Not ready to switch on",
                Cia402DriveState.SwitchOnDisabled => "Switch on disabled",
                Cia402DriveState.ReadyToSwitchOn => "Ready to switch on",
                Cia402DriveState.SwitchedOn => "Switched on",
                Cia402DriveState.OperationEnabled => "Operation enabled",
                Cia402DriveState.QuickStopActive => "Quick stop active",
                Cia402DriveState.FaultReactionActive => "Fault reaction active",
                Cia402DriveState.Fault => "Fault",
                _ => $"Unknown state (0x{Value:X4})"
            };
        }
    }
}
