namespace BedrockConsoleClient.Networking.RakNet.IO;

using System.Buffers.Binary;

internal ref struct RakNetSpanReader(ReadOnlySpan<byte> data)
{
  private readonly ReadOnlySpan<byte> _data = data;
  private int _position;

  public readonly int Position => _position;

  public readonly int Remaining => _data.Length - _position;

  public void Advance(int count) => _position += count;

  public byte ReadByte() => _data[_position++];

  public bool ReadBool() => ReadByte() != 0;

  public ushort ReadUInt16BE()
  {
    ushort value = BinaryPrimitives.ReadUInt16BigEndian(_data[_position..]);
    _position += 2;
    return value;
  }

  // RakNet's 24-bit fields (datagram sequence number, message/order/sequence index)
  // are little-endian, unlike every other field in the protocol. See docs/notes/raknet-design.md.
  public uint ReadUInt24LE()
  {
    uint value = (uint)(_data[_position] | (_data[_position + 1] << 8) | (_data[_position + 2] << 16));
    _position += 3;
    return value;
  }

  public uint ReadUInt32BE()
  {
    uint value = BinaryPrimitives.ReadUInt32BigEndian(_data[_position..]);
    _position += 4;
    return value;
  }

  public long ReadInt64BE()
  {
    long value = BinaryPrimitives.ReadInt64BigEndian(_data[_position..]);
    _position += 8;
    return value;
  }

  public ReadOnlySpan<byte> ReadBytes(int count)
  {
    var slice = _data.Slice(_position, count);
    _position += count;
    return slice;
  }

  public readonly ReadOnlySpan<byte> ReadRemaining() => _data[_position..];
}
