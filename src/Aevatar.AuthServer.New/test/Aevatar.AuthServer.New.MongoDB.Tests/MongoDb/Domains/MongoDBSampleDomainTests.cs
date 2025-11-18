using Aevatar.AuthServer.New.Samples;
using Xunit;

namespace Aevatar.AuthServer.New.MongoDB.Domains;

[Collection(NewTestConsts.CollectionDefinitionName)]
public class MongoDBSampleDomainTests : SampleDomainTests<NewMongoDbTestModule>
{

}
