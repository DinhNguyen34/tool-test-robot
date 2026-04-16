using ModuleMotor.Cia402;
using ModuleMotor.Cia402.Abstractions;
using ModuleMotor.Cia402.Canopen;
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
    }
}
