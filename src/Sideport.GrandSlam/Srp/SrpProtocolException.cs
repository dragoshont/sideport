namespace Sideport.GrandSlam.Srp;

/// <summary>
/// Thrown when the SRP exchange violates a protocol safety invariant (for
/// example, the server public value <c>B ≡ 0 (mod N)</c>, or <c>u = 0</c>),
/// which would collapse the security of the handshake.
/// </summary>
public sealed class SrpProtocolException : Exception
{
    public SrpProtocolException(string message) : base(message) { }

    public SrpProtocolException(string message, Exception innerException)
        : base(message, innerException) { }
}
