using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.Domain.Entities;

namespace Aevatar.Agents.AuthServer.User;

public class IdentityUserExtension: FullAuditedAggregateRoot<Guid>
{
    public Guid UserId { get; set; }
    /// <summary>
    /// EOA Address or CA Address
    /// </summary>
    public string WalletAddress { get; set; }
    
    public IdentityUserExtension(Guid id)
    {
        Id = id;
    }
}

