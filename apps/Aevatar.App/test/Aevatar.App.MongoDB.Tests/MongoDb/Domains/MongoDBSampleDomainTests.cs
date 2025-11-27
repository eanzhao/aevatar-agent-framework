using Aevatar.App.Samples;
using Xunit;

namespace Aevatar.App.MongoDB.Domains;

[Collection(AppTestConsts.CollectionDefinitionName)]
public class MongoDBSampleDomainTests : SampleDomainTests<AppMongoDbTestModule>
{

}
