namespace ModuleMotor.Canopen
{
    /// <summary>
    /// CAN IDs and byte offsets for the eRob CANopen PDO frames.
    /// Standard CAN frames are limited to 8 bytes, so two TPDOs and two RPDOs
    /// are used to cover the full CiA 402 process data set.
    ///
    /// Defaults match the eRob CANopen default PDO mapping (manual v1.9).
    /// Verify against your firmware using objects 0x1600–0x1603 (RPDO) and
    /// 0x1A00–0x1A03 (TPDO).
    /// </summary>
    public sealed class ErobCanopenPdoMap
    {
        // ── RPDO1 (master → eRob): COB-ID = 0x200 + nodeId, 8 bytes ──────────
        // Object 0x6040: Controlword      UINT16 bytes [0-1]
        // Object 0x6060: ModesOfOperation INT8   byte  [2]
        //                padding                 byte  [3]
        // Object 0x607A: TargetPosition   INT32  bytes [4-7]
        public int Rpdo1Controlword      { get; init; } = 0;
        public int Rpdo1ModesOfOperation { get; init; } = 2;
        public int Rpdo1TargetPosition   { get; init; } = 4;
        public int Rpdo1Bytes            { get; init; } = 8;

        // ── RPDO2 (master → eRob): COB-ID = 0x300 + nodeId, 6 bytes ──────────
        // Object 0x60FF: TargetVelocity   INT32  bytes [0-3]
        // Object 0x6071: TargetTorque     INT16  bytes [4-5]
        public int Rpdo2TargetVelocity { get; init; } = 0;
        public int Rpdo2TargetTorque   { get; init; } = 4;
        public int Rpdo2Bytes          { get; init; } = 6;

        // ── TPDO1 (eRob → master): COB-ID = 0x180 + nodeId, 8 bytes ──────────
        // Object 0x6041: Statusword              UINT16 bytes [0-1]
        // Object 0x6061: ModesOfOperationDisplay INT8   byte  [2]
        //                padding                        byte  [3]
        // Object 0x6064: PositionActualValue      INT32  bytes [4-7]
        public int Tpdo1Statusword              { get; init; } = 0;
        public int Tpdo1ModesOfOperationDisplay { get; init; } = 2;
        public int Tpdo1PositionActualValue     { get; init; } = 4;
        public int Tpdo1Bytes                   { get; init; } = 8;

        // ── TPDO2 (eRob → master): COB-ID = 0x280 + nodeId, 8 bytes ──────────
        // Object 0x606C: VelocityActualValue  INT32  bytes [0-3]
        // Object 0x6077: TorqueActualValue    INT16  bytes [4-5]
        // Object 0x603F: ErrorCode            UINT16 bytes [6-7]
        public int Tpdo2VelocityActualValue { get; init; } = 0;
        public int Tpdo2TorqueActualValue   { get; init; } = 4;
        public int Tpdo2ErrorCode           { get; init; } = 6;
        public int Tpdo2Bytes               { get; init; } = 8;

        /// <summary>Factory-default PDO map for eRob CANopen v1.9.</summary>
        public static ErobCanopenPdoMap Default { get; } = new ErobCanopenPdoMap();
    }
}
