using Aevatar.Agents.TestBase;
using Xunit;

namespace Aevatar.Agents.Orleans.Tests;

[CollectionDefinition("OrleansCluster")]
public class OrleansClusterCollection : ICollectionFixture<ClusterFixture>
{
    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionDefinition] and all the
    // ICollectionFixture<> interfaces.
}
