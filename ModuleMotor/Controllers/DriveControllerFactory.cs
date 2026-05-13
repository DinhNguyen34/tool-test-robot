using ModuleMotor.Cia402;
using ModuleMotor.Cia402.Abstractions;
using ModuleMotor.Cia402.Canopen;
using ModuleMotor.Cia402.Ethercat;
using ModuleMotor.Cia402.Ethercat.Soem;
using ModuleMotor.Canopen;
using ModuleMotor.Canopen.Transport;
using ModuleMotor.Models;

namespace ModuleMotor.Controllers
{
    public static class DriveControllerFactory
    {
        public static IDriveController CreateLegacyVendorController(
            MotorProtocolKind protocol,
            MotorModel model,
            MotorConfig config)
        {
            return protocol switch
            {
                MotorProtocolKind.Robstride => new RobstrideController(model, config),
                MotorProtocolKind.Encos => new EncosController(model, config),
                _ => throw new NotSupportedException($"Unsupported legacy protocol: {protocol}.")
            };
        }

        public static IDriveController CreateCia402Controller(
            ICia402ObjectAccess objectAccess,
            ICia402ProcessData? processData = null)
        {
            return new Cia402Controller(objectAccess, processData);
        }

        /// <summary>
        /// Creates a CiA 402 controller for an eRob joint connected via CANopen.
        /// Wires: MotorModel → VCanMotorModelTransport → CanopenCia402Adapter → Cia402Controller.
        ///
        /// Call <see cref="CanopenCia402Adapter.OpenAsync"/> before using the controller
        /// to send NMT Start Remote Node to the eRob.
        /// </summary>
        /// <param name="enablePdo">
        /// If true, passes the adapter as both object-access and process-data provider,
        /// enabling SYNC-triggered PDO exchange.
        /// If false, uses SDO-only mode (no PDO, safer for initial setup).
        /// </param>
        public static (IDriveController Controller, CanopenCia402Adapter Adapter)
            CreateCanopenCia402Controller(
                MotorModel model,
                byte nodeId,
                bool enablePdo = true,
                ErobCanopenPdoMap? pdoMap = null)
        {
            IVCanFrameTransport transport = new VCanMotorModelTransport(model);
            CanopenCia402Adapter adapter = new(nodeId, transport, pdoMap);
            IDriveController controller = new Cia402Controller(
                adapter,
                enablePdo ? adapter : null);
            return (controller, adapter);
        }

        /// <summary>
        /// Creates a CiA 402 controller for an eRob joint connected via EtherCAT (SOEM).
        /// Wires: SoemMaster → EthercatCoeCia402Adapter → Cia402Controller.
        ///
        /// Call <see cref="SoemMaster.OpenAsync"/> before using the controller
        /// to scan slaves and bring them to Operational state.
        /// </summary>
        /// <param name="interfaceName">NIC device name, e.g. "\Device\NPF_{GUID}".</param>
        /// <param name="slaveIndex">1-based EtherCAT slave position on the bus.</param>
        /// <param name="enablePdo">
        /// If true, passes the adapter as process-data provider (reads from IO map).
        /// If false, uses CoE SDO mailbox only.
        /// </param>
        /// <param name="cyclePeriodMs">SOEM cyclic PDO period in milliseconds.</param>
        public static (IDriveController Controller, SoemMaster Master, EthercatCoeCia402Adapter Adapter)
            CreateEthercatCia402Controller(
                string interfaceName,
                ushort slaveIndex = 1,
                bool enablePdo = true,
                int cyclePeriodMs = 1,
                ErobPdoMap? pdoMap = null)
        {
            SoemMaster master = new(interfaceName, cyclePeriodMs);
            EthercatCoeCia402Adapter adapter = new(master, slaveIndex, pdoMap);
            IDriveController controller = new Cia402Controller(
                adapter,
                enablePdo ? adapter : null);
            return (controller, master, adapter);
        }
    }
}
