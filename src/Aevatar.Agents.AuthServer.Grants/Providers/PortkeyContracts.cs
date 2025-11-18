using AElf.Types;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace Aevatar.Agents.AuthServer.Grants.Providers;

// Placeholder types for Portkey.Contracts.CA
// These should be replaced with actual Portkey.Contracts.CA types when available

public class GetHolderInfoInput : IMessage<GetHolderInfoInput>
{
    public Hash CaHash { get; set; } = Hash.Empty;
    public Hash LoginGuardianIdentifierHash { get; set; } = Hash.Empty;
    
    public MessageDescriptor Descriptor => throw new NotImplementedException();
    public int CalculateSize() => throw new NotImplementedException();
    public GetHolderInfoInput Clone() => throw new NotImplementedException();
    public bool Equals(GetHolderInfoInput? other) => throw new NotImplementedException();
    public void MergeFrom(GetHolderInfoInput message) => throw new NotImplementedException();
    public void MergeFrom(CodedInputStream input) => throw new NotImplementedException();
    public void WriteTo(CodedOutputStream output) => throw new NotImplementedException();
}

public class GetHolderInfoOutput : IMessage<GetHolderInfoOutput>
{
    public Address CaAddress { get; set; } = new Address();
    public List<PortkeyManagerInfo> ManagerInfos { get; set; } = new();
    
    public MessageDescriptor Descriptor => throw new NotImplementedException();
    public int CalculateSize() => throw new NotImplementedException();
    public GetHolderInfoOutput Clone() => throw new NotImplementedException();
    public bool Equals(GetHolderInfoOutput? other) => throw new NotImplementedException();
    public void MergeFrom(GetHolderInfoOutput message) => throw new NotImplementedException();
    public void MergeFrom(CodedInputStream input) => throw new NotImplementedException();
    public void WriteTo(CodedOutputStream output) => throw new NotImplementedException();
}

public class PortkeyManagerInfo
{
    public Address Address { get; set; } = new Address();
    public string ExtraData { get; set; } = string.Empty;
}
