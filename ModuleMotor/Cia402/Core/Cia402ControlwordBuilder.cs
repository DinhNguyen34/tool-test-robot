namespace ModuleMotor.Cia402.Core
{
    public static class Cia402ControlwordBuilder
    {
        public const ushort Shutdown = 0x0006;
        public const ushort SwitchOn = 0x0007;
        public const ushort EnableOperation = 0x000F;
        // DisableOperation transition reuses the same bits as SwitchOn (CiA 402 Table 19)
        public const ushort DisableOperation = 0x0007;
        public const ushort FaultReset = 0x0080;
        // Quick Stop: bits 1=0, 2=1 (CiA 402 Table 19, transition 11)
        public const ushort QuickStop = 0x0002;

        /// <summary>
        /// Profile Position mode: sets New Set Point (bit 4) to trigger a move.
        /// Clear bit 4 after SetPointAcknowledge (statusword bit 12) goes high.
        /// </summary>
        public static ushort BuildProfilePositionTrigger(bool immediateChange, bool absoluteMove)
        {
            ushort controlword = EnableOperation;
            controlword |= 1 << 4; // New Set Point

            if (immediateChange)
                controlword |= 1 << 5; // Change Set Immediately

            if (!absoluteMove)
                controlword |= 1 << 6; // bit 6 clear = absolute, set = relative

            return controlword;
        }
    }
}
