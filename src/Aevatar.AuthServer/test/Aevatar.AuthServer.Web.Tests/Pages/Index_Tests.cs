using System.Threading.Tasks;
using Shouldly;
using Xunit;

namespace Aevatar.AuthServer.Pages;

[Collection(AuthServerTestConsts.CollectionDefinitionName)]
public class Index_Tests : AuthServerWebTestBase
{
    [Fact]
    public async Task Welcome_Page()
    {
        var response = await GetResponseAsStringAsync("/");
        response.ShouldNotBeNull();
    }
}
