namespace BedrockConsoleClient.Networking.RakNet.Reliability;

using System.Buffers.Binary;
using BedrockConsoleClient.Networking.RakNet.IO;

// ACK/NAK body: a 2-byte big-endian record count, then records, each either a
// single sequence number or a contiguous [first, last] range, run-length-encoded
// to keep large acknowledgement bursts compact. Matches go-raknet's
// acknowledge.go. Doesn't need a separate range DTO: callers just work with a
// flat, sorted list of sequence numbers.
internal static class AckNakCodec
{
  private const byte RecordTypeRange = 0;
  private const byte RecordTypeSingle = 1;

  public static int Write(Span<byte> destination, IReadOnlyList<uint> sortedSequenceNumbers)
  {
    var writer = new RakNetSpanWriter(destination);
    int countPosition = writer.Position;
    writer.WriteUInt16BE(0); // patched below once the real record count is known
    if (sortedSequenceNumbers.Count == 0)
    {
      return writer.Position;
    }

    ushort records = 0;
    uint rangeStart = sortedSequenceNumbers[0];
    uint rangeEnd = rangeStart;
    for (int i = 1; i < sortedSequenceNumbers.Count; i++)
    {
      uint next = sortedSequenceNumbers[i];
      if (next == rangeEnd + 1)
      {
        rangeEnd = next;
        continue;
      }

      WriteRecord(ref writer, rangeStart, rangeEnd, ref records);
      rangeStart = rangeEnd = next;
    }

    WriteRecord(ref writer, rangeStart, rangeEnd, ref records);

    BinaryPrimitives.WriteUInt16BigEndian(destination[countPosition..], records);
    return writer.Position;
  }

  private static void WriteRecord(ref RakNetSpanWriter writer, uint first, uint last, ref ushort count)
  {
    if (first == last)
    {
      writer.WriteByte(RecordTypeSingle);
      writer.WriteUInt24LE(first);
    }
    else
    {
      writer.WriteByte(RecordTypeRange);
      writer.WriteUInt24LE(first);
      writer.WriteUInt24LE(last);
    }

    count++;
  }

  public static List<uint> Read(ReadOnlySpan<byte> data)
  {
    var reader = new RakNetSpanReader(data);
    ushort recordCount = reader.ReadUInt16BE();
    var sequenceNumbers = new List<uint>();
    for (int i = 0; i < recordCount; i++)
    {
      byte recordType = reader.ReadByte();
      if (recordType == RecordTypeRange)
      {
        uint start = reader.ReadUInt24LE();
        uint end = reader.ReadUInt24LE();
        for (uint seq = start; seq <= end; seq++)
        {
          sequenceNumbers.Add(seq);
        }
      }
      else
      {
        sequenceNumbers.Add(reader.ReadUInt24LE());
      }
    }

    return sequenceNumbers;
  }
}
