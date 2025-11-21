using Aevatar.BusinessServer.MongoDB;
using Aevatar.BusinessServer.Samples;
using Xunit;

namespace Aevatar.BusinessServer.MongoDb.Applications;

[Collection(BusinessServerTestConsts.CollectionDefinitionName)]
public class MongoDBSampleAppServiceTests : SampleAppServiceTests<BusinessServerMongoDbTestModule>
{

}
