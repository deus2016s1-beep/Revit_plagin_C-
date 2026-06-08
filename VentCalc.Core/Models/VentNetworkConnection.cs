namespace VentCalc.Core.Models
{
    public sealed class VentNetworkConnection
    {
        public VentNetworkConnection(
            string fromElementId,
            string toElementId,
            int fromConnectorIndex,
            int toConnectorIndex,
            string connectionKind)
        {
            FromElementId = fromElementId;
            ToElementId = toElementId;
            FromConnectorIndex = fromConnectorIndex;
            ToConnectorIndex = toConnectorIndex;
            ConnectionKind = connectionKind;
        }

        public string FromElementId { get; }

        public string ToElementId { get; }

        public int FromConnectorIndex { get; }

        public int ToConnectorIndex { get; }

        public string ConnectionKind { get; }
    }
}
