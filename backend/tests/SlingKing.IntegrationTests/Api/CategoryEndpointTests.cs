using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SlingKing.Infrastructure.Persistence.Seed;
using SlingKing.IntegrationTests.Fixtures;
using Xunit;

namespace SlingKing.IntegrationTests.Api;

[Collection("database")]
public class CategoryEndpointTests : IClassFixture<SlingKingApiFactory>, IAsyncLifetime
{
    private readonly SlingKingApiFactory _factory;
    private readonly HttpClient _client;

    public CategoryEndpointTests(SlingKingApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync();
        await AuthenticateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private record LoginRequest(string Email, string Password);
    private record LoginResponse(string AccessToken);
    private record CategoryOption(int Id, string Name);
    private record CreateCategoryRequest(string Name);

    private async Task AuthenticateAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(SlingKingApiFactory.SeedEmail, SlingKingApiFactory.SeedPassword));

        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body!.AccessToken);
    }

    private static string UniqueName() => $"Cat {Guid.NewGuid():N}"[..14];

    [Fact]
    public async Task Listing_categories_requires_a_token()
    {
        using var anonymous = _factory.CreateClient();

        (await anonymous.GetAsync("/api/v1/categories")).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_created_category_appears_in_the_list()
    {
        var name = UniqueName();

        var created = await _client.PostAsJsonAsync(
            "/api/v1/categories", new CreateCategoryRequest(name));
        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        var option = await created.Content.ReadFromJsonAsync<CategoryOption>();
        option!.Name.ShouldBe(name);
        option.Id.ShouldBeGreaterThan(0);

        var list = await _client.GetFromJsonAsync<List<CategoryOption>>("/api/v1/categories");
        list!.ShouldContain(c => c.Id == option.Id && c.Name == name);
    }

    [Fact]
    public async Task A_duplicate_name_is_rejected_as_a_conflict()
    {
        var name = UniqueName();

        (await _client.PostAsJsonAsync("/api/v1/categories", new CreateCategoryRequest(name)))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var second = await _client.PostAsJsonAsync(
            "/api/v1/categories", new CreateCategoryRequest(name.ToUpper()));

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        second.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");

        // The message is ours and safe to show; nothing internal rides along with it.
        var body = await second.Content.ReadAsStringAsync();
        body.ShouldContain("already exists");
        body.ShouldNotContain("Exception");
        body.ShouldNotContain("SlingKing");
        body.ShouldNotContain("   at ");
    }

    [Fact]
    public async Task A_blank_name_is_rejected_as_validation()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/v1/categories", new CreateCategoryRequest("   "));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
