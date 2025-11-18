using System.Threading.Tasks;
using Shouldly;
using Xunit;

namespace Aevatar.AuthServer.New.Pages;

[Collection(NewTestConsts.CollectionDefinitionName)]
public class Index_Tests : NewWebTestBase
{
    [Fact]
    public async Task Welcome_Page()
    {
        var response = await GetResponseAsStringAsync("/");
        response.ShouldNotBeNull();
    }
}
