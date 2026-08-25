namespace BedrockConsoleClient.Networking.Bedrock.IO;

using System.Buffers.Binary;
using System.Text;

internal ref struct BedrockVarIntWriter(Span<byte> destination)
{
  private readonly Span<byte> _destination = destination;
  private int _position;

  public readonly int Position => _position;

  public void WriteByte(byte value) => _destination[_position++] = value;

  public void WriteBool(bool value) => WriteByte((byte)(value ? 1 : 0));

  public void WriteVarUInt32(uint value)
  {
    while (true)
    {
      if ((value & ~0x7Fu) == 0)
      {
        WriteByte((byte)value);
        return;
      }

      WriteByte((byte)((value & 0x7F) | 0x80));
      value >>= 7;
    }
  }

  public void WriteVarInt32(int value)
  {
    uint zigzag = (uint)((value << 1) ^ (value >> 31));
    WriteVarUInt32(zigzag);
  }

  public void WriteVarUInt64(ulong value)
  {
    while (true)
    {
      if ((value & ~0x7Ful) == 0)
      {
        WriteByte((byte)value);
        return;
      }

      WriteByte((byte)((value & 0x7F) | 0x80));
      value >>= 7;
    }
  }

  public void WriteVarInt64(long value)
  {
    ulong zigzag = (ulong)((value << 1) ^ (value >> 63));
    WriteVarUInt64(zigzag);
  }

  public void WriteString(string value)
  {
    int byteCount = Encoding.UTF8.GetByteCount(value);
    WriteVarUInt32((uint)byteCount);
    Encoding.UTF8.GetBytes(value, _destination[_position..]);
    _position += byteCount;
  }

  public void WriteUInt16LE(ushort value)
  {
    BinaryPrimitives.WriteUInt16LittleEndian(_destination[_position..], value);
    _position += 2;
  }

  public void WriteUInt32LE(uint value)
  {
    BinaryPrimitives.WriteUInt32LittleEndian(_destination[_position..], value);
    _position += 4;
  }

  public void WriteUInt64LE(ulong value)
  {
    BinaryPrimitives.WriteUInt64LittleEndian(_destination[_position..], value);
    _position += 8;
  }

  public void WriteFloatLE(float value)
  {
    BinaryPrimitives.WriteSingleLittleEndian(_destination[_position..], value);
    _position += 4;
  }

  public void WriteUInt32BE(uint value)
  {
    BinaryPrimitives.WriteUInt32BigEndian(_destination[_position..], value);
    _position += 4;
  }

  public void WriteBytes(ReadOnlySpan<byte> bytes)
  {
    bytes.CopyTo(_destination[_position..]);
    _position += bytes.Length;
  }
}
