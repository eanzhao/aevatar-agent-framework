using Aevatar.AuthServer.Samples;
using Xunit;

namespace Aevatar.AuthServer.MongoDB.Domains;

[Collection(AuthServerTestConsts.CollectionDefinitionName)]
public class MongoDBSampleDomainTests : SampleDomainTests<AuthServerMongoDbTestModule>
{

}
