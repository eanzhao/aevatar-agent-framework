using Aevatar.Agents.AuthServer.User;
using Volo.Abp.Data;
using Volo.Abp.MongoDB;
using MongoDB.Driver;

namespace Aevatar.Agents.AuthServer.MongoDB;

[ConnectionStringName("Default")]
public class AevatarAgentsAuthServerMongoDbContext : AbpMongoDbContext
{
    public IMongoCollection<IdentityUserExtension> UserExtensions => Collection<IdentityUserExtension>();

    protected override void CreateModel(IMongoModelBuilder modelBuilder)
    {
        base.CreateModel(modelBuilder);

        modelBuilder.Entity<IdentityUserExtension>(b =>
        {
            b.CollectionName = "UserExtensions";
        });
    }
}

