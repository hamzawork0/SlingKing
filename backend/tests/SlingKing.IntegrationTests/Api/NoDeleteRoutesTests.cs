using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SlingKing.IntegrationTests.Fixtures;
using Xunit;

namespace SlingKing.IntegrationTests.Api;

/// <summary>
/// "Never delete transactional data" is a standing constraint. Reading the controllers to
/// check it is exactly the kind of review that passes right up until someone adds one, so
/// this enumerates what the application actually routes.
/// </summary>
[Collection("database")]
public class NoDeleteRoutesTests : IClassFixture<SlingKingApiFactory>
{
    private readonly SlingKingApiFactory _factory;

    public NoDeleteRoutesTests(SlingKingApiFactory factory) => _factory = factory;

    [Fact]
    public void The_application_routes_no_delete_endpoint()
    {
        var actions = _factory.Services
            .GetRequiredService<IActionDescriptorCollectionProvider>()
            .ActionDescriptors.Items;

        var deletes = actions
            .Where(a => a.ActionConstraints?
                .OfType<Microsoft.AspNetCore.Mvc.ActionConstraints.HttpMethodActionConstraint>()
                .Any(c => c.HttpMethods.Contains("DELETE")) == true)
            .Select(a => a.AttributeRouteInfo?.Template ?? a.DisplayName)
            .ToList();

        deletes.ShouldBeEmpty();
    }
}
