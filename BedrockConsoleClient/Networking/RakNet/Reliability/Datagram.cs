namespace BedrockConsoleClient.Networking.RakNet.Reliability;

using BedrockConsoleClient.Networking.RakNet.IO;

// A data datagram: header byte (0x80, the "this is a datagram" flag, distinct
// from the 0x40/0x20 ACK/NAK datagrams handled separately) + a little-endian
// 24-bit sequence number + one or more Frames.
internal sealed record Datagram
{
  public const byte DatagramFlag = 0x80;

  public required uint SequenceNumber { get; init; }

  public required IReadOnlyList<Frame> Frames { get; init; }

  public int Write(Span<byte> destination)
  {
    var writer = new RakNetSpanWriter(destination);
    writer.WriteByte(DatagramFlag);
    writer.WriteUInt24LE(SequenceNumber);
    foreach (var frame in Frames)
    {
      writer.Advance(frame.Write(writer.Remaining));
    }

    return writer.Position;
  }

  // Caller has already checked data[0] has DatagramFlag set (and not ACK/NAK).
  public static Datagram Read(ReadOnlySpan<byte> data)
  {
    var reader = new RakNetSpanReader(data);
    reader.ReadByte();
    uint sequenceNumber = reader.ReadUInt24LE();

    var frames = new List<Frame>();
    while (reader.Remaining > 0)
    {
      var frame = Frame.Read(reader.ReadRemaining(), out int consumed);
      frames.Add(frame);
      reader.Advance(consumed);
    }

    return new Datagram { SequenceNumber = sequenceNumber, Frames = frames };
  }
}
