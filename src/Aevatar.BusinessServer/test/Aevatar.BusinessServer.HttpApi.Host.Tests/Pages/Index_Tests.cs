using System.Threading.Tasks;
using Shouldly;
using Xunit;

namespace Aevatar.BusinessServer.Pages;

[Collection(BusinessServerTestConsts.CollectionDefinitionName)]
public class Index_Tests : BusinessServerWebTestBase
{
    [Fact]
    public async Task Welcome_Page()
    {
        var response = await GetResponseAsStringAsync("/");
        response.ShouldNotBeNull();
    }
}
