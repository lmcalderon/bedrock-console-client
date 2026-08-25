namespace BedrockConsoleClient.Networking.RakNet.Reliability;

// Buffers fragments of a split Frame until all arrive, then yields the
// reassembled content. Keyed by SplitId, since more than one split message
// can be in flight at once. Only ever touched from RakNetConnection's single
// receive loop, so no internal locking.
internal sealed class SplitPacketReassembler
{
  private readonly Dictionary<ushort, byte[]?[]> _pending = [];

  public byte[]? Add(Frame frame)
  {
    if (!_pending.TryGetValue(frame.SplitId, out byte[]?[]? fragments))
    {
      fragments = new byte[frame.SplitCount][];
      _pending[frame.SplitId] = fragments;
    }

    if (frame.SplitIndex >= fragments.Length)
    {
      // Inconsistent split count for this ID; drop defensively rather than
      // risk an out-of-range write.
      return null;
    }

    fragments[frame.SplitIndex] = frame.Content.ToArray();
    if (Array.Exists(fragments, f => f is null))
    {
      return null;
    }

    _pending.Remove(frame.SplitId);
    int totalLength = 0;
    foreach (byte[]? fragment in fragments)
    {
      totalLength += fragment!.Length;
    }

    var result = new byte[totalLength];
    int offset = 0;
    foreach (byte[]? fragment in fragments)
    {
      fragment!.CopyTo(result.AsSpan(offset));
      offset += fragment!.Length;
    }

    return result;
  }
}
