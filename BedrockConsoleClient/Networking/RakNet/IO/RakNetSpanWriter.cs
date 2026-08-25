namespace BedrockConsoleClient.Networking.RakNet.IO;

using System.Buffers.Binary;

internal ref struct RakNetSpanWriter(Span<byte> destination)
{
  private readonly Span<byte> _destination = destination;
  private int _position;

  public readonly int Position => _position;

  public readonly Span<byte> Remaining => _destination[_position..];

  public void Advance(int count) => _position += count;

  public void WriteByte(byte value) => _destination[_position++] = value;

  public void WriteBool(bool value) => WriteByte((byte)(value ? 1 : 0));

  public void WriteUInt16BE(ushort value)
  {
    BinaryPrimitives.WriteUInt16BigEndian(_destination[_position..], value);
    _position += 2;
  }

  // See RakNetSpanReader.ReadUInt24LE for why this one field family is little-endian.
  public void WriteUInt24LE(uint value)
  {
    _destination[_position] = (byte)value;
    _destination[_position + 1] = (byte)(value >> 8);
    _destination[_position + 2] = (byte)(value >> 16);
    _position += 3;
  }

  public void WriteUInt32BE(uint value)
  {
    BinaryPrimitives.WriteUInt32BigEndian(_destination[_position..], value);
    _position += 4;
  }

  public void WriteInt64BE(long value)
  {
    BinaryPrimitives.WriteInt64BigEndian(_destination[_position..], value);
    _position += 8;
  }

  public void WriteBytes(ReadOnlySpan<byte> bytes)
  {
    bytes.CopyTo(_destination[_position..]);
    _position += bytes.Length;
  }
}
