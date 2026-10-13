namespace AmhsSim.Protocol;

/// <summary>
/// One message on the wire. TxId 0 is reserved for frames the vehicle pushes by itself;
/// a reply always echoes the TxId of the request it answers.
/// </summary>
public sealed record Frame(MessageType Type, byte TxId, byte[] Payload)
{
    public override string ToString() => $"{Type} tx={TxId} payload={Convert.ToHexString(Payload)}";
}