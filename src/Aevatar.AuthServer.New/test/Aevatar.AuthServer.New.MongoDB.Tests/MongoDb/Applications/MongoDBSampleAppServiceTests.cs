using Aevatar.AuthServer.New.MongoDB;
using Aevatar.AuthServer.New.Samples;
using Xunit;

namespace Aevatar.AuthServer.New.MongoDb.Applications;

[Collection(NewTestConsts.CollectionDefinitionName)]
public class MongoDBSampleAppServiceTests : SampleAppServiceTests<NewMongoDbTestModule>
{

}
