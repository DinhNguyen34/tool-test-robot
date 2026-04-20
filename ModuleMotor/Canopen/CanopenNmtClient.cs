using ModuleMotor.Canopen.Transport;

namespace ModuleMotor.Canopen
{
    public sealed class CanopenNmtClient
    {
        private readonly IVCanFrameTransport _transport;

        public CanopenNmtClient(IVCanFrameTransport transport)
        {
            _transport = transport;
        }

        public Task StartRemoteNodeAsync(byte nodeId, CancellationToken ct)
            => _transport.SendAsync(CanopenFrameBuilder.BuildNmt(0x01, nodeId), ct);

        public Task StopRemoteNodeAsync(byte nodeId, CancellationToken ct)
            => _transport.SendAsync(CanopenFrameBuilder.BuildNmt(0x02, nodeId), ct);

        public Task ResetNodeAsync(byte nodeId, CancellationToken ct)
            => _transport.SendAsync(CanopenFrameBuilder.BuildNmt(0x81, nodeId), ct);

        public Task ResetCommunicationAsync(byte nodeId, CancellationToken ct)
            => _transport.SendAsync(CanopenFrameBuilder.BuildNmt(0x82, nodeId), ct);
    }
}
