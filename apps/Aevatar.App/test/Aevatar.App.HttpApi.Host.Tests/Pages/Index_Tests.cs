using System.Threading.Tasks;
using Shouldly;
using Xunit;

namespace Aevatar.App.Pages;

[Collection(AppTestConsts.CollectionDefinitionName)]
public class Index_Tests : AppWebTestBase
{
    [Fact]
    public async Task Welcome_Page()
    {
        var response = await GetResponseAsStringAsync("/");
        response.ShouldNotBeNull();
    }
}
