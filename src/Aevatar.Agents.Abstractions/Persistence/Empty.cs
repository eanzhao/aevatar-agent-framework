using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace Aevatar.Agents.Abstractions.Persistence;

/// <summary>
/// Represents an empty configuration/state for agents that do not require one.
/// </summary>
public class Empty : IMessage<Empty>
{
    private static readonly MessageParser<Empty> _parser = new MessageParser<Empty>(() => new Empty());
    public static MessageParser<Empty> Parser => _parser;

    public static MessageDescriptor Descriptor => null!; // Descriptor is not strictly required for internal usage if not serialized via standard proto tools

    MessageDescriptor IMessage.Descriptor => Descriptor;

    public void MergeFrom(Empty other)
    {
        // No fields to merge
    }

    public void MergeFrom(CodedInputStream input)
    {
        // No fields to read
        while (input.ReadTag() != 0)
        {
            input.SkipLastField();
        }
    }

    public void WriteTo(CodedOutputStream output)
    {
        // No fields to write
    }

    public int CalculateSize()
    {
        return 0;
    }

    public bool Equals(Empty? other)
    {
        return other != null;
    }

    public Empty Clone()
    {
        return new Empty();
    }
}


