using Aevatar.BusinessServer.Samples;
using Xunit;

namespace Aevatar.BusinessServer.MongoDB.Domains;

[Collection(BusinessServerTestConsts.CollectionDefinitionName)]
public class MongoDBSampleDomainTests : SampleDomainTests<BusinessServerMongoDbTestModule>
{

}
