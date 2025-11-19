using Aevatar.AuthServer.MongoDB;
using Aevatar.AuthServer.Samples;
using Xunit;

namespace Aevatar.AuthServer.MongoDb.Applications;

[Collection(AuthServerTestConsts.CollectionDefinitionName)]
public class MongoDBSampleAppServiceTests : SampleAppServiceTests<AuthServerMongoDbTestModule>
{

}
