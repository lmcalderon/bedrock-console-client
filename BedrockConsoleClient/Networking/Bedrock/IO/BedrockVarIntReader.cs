namespace BedrockConsoleClient.Networking.Bedrock.IO;

using System.Buffers.Binary;
using System.Text;

// Bedrock's own wire format: mostly LEB128 VarInts (unlike RakNet's
// fixed-width fields), plus a handful of fixed-width exceptions: some
// little-endian, and a few (protocol version fields) still big-endian.
// A distinct type from RakNetSpanReader on purpose: mixing the two would be
// a footgun given the different default endianness.
internal ref struct BedrockVarIntReader(ReadOnlySpan<byte> data)
{
  private readonly ReadOnlySpan<byte> _data = data;
  private int _position;

  public readonly int Position => _position;

  public readonly int Remaining => _data.Length - _position;

  public void Advance(int count) => _position += count;

  public byte ReadByte() => _data[_position++];

  public bool ReadBool() => ReadByte() != 0;

  public uint ReadVarUInt32()
  {
    uint result = 0;
    int shift = 0;
    while (true)
    {
      byte b = ReadByte();
      result |= (uint)(b & 0x7F) << shift;
      if ((b & 0x80) == 0)
      {
        return result;
      }

      shift += 7;
    }
  }

  public int ReadVarInt32()
  {
    uint raw = ReadVarUInt32();
    return (int)(raw >> 1) ^ -(int)(raw & 1);
  }

  public ulong ReadVarUInt64()
  {
    ulong result = 0;
    int shift = 0;
    while (true)
    {
      byte b = ReadByte();
      result |= (ulong)(b & 0x7F) << shift;
      if ((b & 0x80) == 0)
      {
        return result;
      }

      shift += 7;
    }
  }

  public long ReadVarInt64()
  {
    ulong raw = ReadVarUInt64();
    return (long)(raw >> 1) ^ -(long)(raw & 1);
  }

  public string ReadString()
  {
    int length = (int)ReadVarUInt32();
    string value = Encoding.UTF8.GetString(_data.Slice(_position, length));
    _position += length;
    return value;
  }

  public ushort ReadUInt16LE()
  {
    ushort value = BinaryPrimitives.ReadUInt16LittleEndian(_data[_position..]);
    _position += 2;
    return value;
  }

  public uint ReadUInt32LE()
  {
    uint value = BinaryPrimitives.ReadUInt32LittleEndian(_data[_position..]);
    _position += 4;
    return value;
  }

  public ulong ReadUInt64LE()
  {
    ulong value = BinaryPrimitives.ReadUInt64LittleEndian(_data[_position..]);
    _position += 8;
    return value;
  }

  public float ReadFloatLE()
  {
    float value = BinaryPrimitives.ReadSingleLittleEndian(_data[_position..]);
    _position += 4;
    return value;
  }

  // The one place Bedrock's own format still uses big-endian: protocol
  // version fields (Login, RequestNetworkSettings, PlayStatus.status).
  public uint ReadUInt32BE()
  {
    uint value = BinaryPrimitives.ReadUInt32BigEndian(_data[_position..]);
    _position += 4;
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
