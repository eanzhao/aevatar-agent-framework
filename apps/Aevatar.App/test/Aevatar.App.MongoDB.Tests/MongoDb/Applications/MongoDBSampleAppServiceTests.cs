using Aevatar.App.MongoDB;
using Aevatar.App.Samples;
using Xunit;

namespace Aevatar.App.MongoDb.Applications;

[Collection(AppTestConsts.CollectionDefinitionName)]
public class MongoDBSampleAppServiceTests : SampleAppServiceTests<AppMongoDbTestModule>
{

}
