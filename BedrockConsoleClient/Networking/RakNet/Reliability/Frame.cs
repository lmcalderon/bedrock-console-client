namespace BedrockConsoleClient.Networking.RakNet.Reliability;

using BedrockConsoleClient.Networking.RakNet.IO;

// One encapsulated message inside a Datagram. Field layout, including the
// "length is in bits, not bytes" quirk, checked against go-raknet's
// packet.write/read rather than guessed.
internal sealed record Frame
{
  private const byte SplitFlag = 0x10;

  public required FrameReliability Reliability { get; init; }

  public uint MessageIndex { get; init; }

  public uint SequenceIndex { get; init; }

  public uint OrderIndex { get; init; }

  public byte OrderChannel { get; init; }

  // Set together when this frame is one fragment of a larger message that
  // didn't fit in one MTU-sized datagram (e.g. a Bedrock Login packet's JWTs,
  // or a large StartGame). SplitId identifies which logical message a
  // fragment belongs to; SplitIndex/SplitCount give its position.
  public bool IsSplit { get; init; }

  public uint SplitCount { get; init; }

  public ushort SplitId { get; init; }

  public uint SplitIndex { get; init; }

  public required ReadOnlyMemory<byte> Content { get; init; }

  public int WrittenSize => 3
      + (Reliability.IsReliable() ? 3 : 0)
      + (Reliability.IsSequenced() ? 3 : 0)
      + (Reliability.IsSequencedOrOrdered() ? 4 : 0)
      + (IsSplit ? 10 : 0)
      + Content.Length;

  public int Write(Span<byte> destination)
  {
    var writer = new RakNetSpanWriter(destination);
    byte header = (byte)((byte)Reliability << 5);
    if (IsSplit)
    {
      header |= SplitFlag;
    }

    writer.WriteByte(header);
    writer.WriteUInt16BE((ushort)(Content.Length << 3));

    if (Reliability.IsReliable())
    {
      writer.WriteUInt24LE(MessageIndex);
    }

    if (Reliability.IsSequenced())
    {
      writer.WriteUInt24LE(SequenceIndex);
    }

    if (Reliability.IsSequencedOrOrdered())
    {
      writer.WriteUInt24LE(OrderIndex);
      writer.WriteByte(OrderChannel);
    }

    if (IsSplit)
    {
      writer.WriteUInt32BE(SplitCount);
      writer.WriteUInt16BE(SplitId);
      writer.WriteUInt32BE(SplitIndex);
    }

    writer.WriteBytes(Content.Span);
    return writer.Position;
  }

  public static Frame Read(ReadOnlySpan<byte> data, out int consumed)
  {
    var reader = new RakNetSpanReader(data);
    byte header = reader.ReadByte();
    bool split = (header & SplitFlag) != 0;
    var reliability = (FrameReliability)((header & 0xE0) >> 5);
    int lengthBits = reader.ReadUInt16BE();
    int length = lengthBits >> 3;

    uint messageIndex = 0, sequenceIndex = 0, orderIndex = 0;
    byte orderChannel = 0;
    if (reliability.IsReliable())
    {
      messageIndex = reader.ReadUInt24LE();
    }

    if (reliability.IsSequenced())
    {
      sequenceIndex = reader.ReadUInt24LE();
    }

    if (reliability.IsSequencedOrOrdered())
    {
      orderIndex = reader.ReadUInt24LE();
      orderChannel = reader.ReadByte();
    }

    uint splitCount = 0;
    ushort splitId = 0;
    uint splitIndex = 0;
    if (split)
    {
      splitCount = reader.ReadUInt32BE();
      splitId = reader.ReadUInt16BE();
      splitIndex = reader.ReadUInt32BE();
    }

    byte[] content = reader.ReadBytes(length).ToArray();
    consumed = reader.Position;
    return new Frame
    {
      Reliability = reliability,
      MessageIndex = messageIndex,
      SequenceIndex = sequenceIndex,
      OrderIndex = orderIndex,
      OrderChannel = orderChannel,
      IsSplit = split,
      SplitCount = splitCount,
      SplitId = splitId,
      SplitIndex = splitIndex,
      Content = content,
    };
  }
}
