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

    private record RenameCategoryRequest(string Name);

    [Fact]
    public async Task Renaming_changes_the_name_in_the_list()
    {
        var created = await _client.PostAsJsonAsync(
            "/api/v1/categories", new CreateCategoryRequest(UniqueName()));
        var option = await created.Content.ReadFromJsonAsync<CategoryOption>();

        var corrected = UniqueName();
        var response = await _client.PutAsJsonAsync(
            $"/api/v1/categories/{option!.Id}", new RenameCategoryRequest(corrected));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<CategoryOption>())!.Name.ShouldBe(corrected);

        var list = await _client.GetFromJsonAsync<List<CategoryOption>>("/api/v1/categories");
        list!.ShouldContain(c => c.Id == option.Id && c.Name == corrected);
    }

    /// <summary>
    /// The duplicate check must exclude the row being renamed. Without that, correcting a
    /// category's capitalisation — the most likely reason to rename one at all — collides
    /// with itself and is refused.
    /// </summary>
    [Fact]
    public async Task A_category_can_be_recapitalised_without_clashing_with_itself()
    {
        var name = UniqueName().ToLowerInvariant();
        var created = await _client.PostAsJsonAsync(
            "/api/v1/categories", new CreateCategoryRequest(name));
        var option = await created.Content.ReadFromJsonAsync<CategoryOption>();

        var response = await _client.PutAsJsonAsync(
            $"/api/v1/categories/{option!.Id}", new RenameCategoryRequest(name.ToUpperInvariant()));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<CategoryOption>())!
            .Name.ShouldBe(name.ToUpperInvariant());
    }

    [Fact]
    public async Task Renaming_onto_another_categorys_name_is_a_conflict()
    {
        var first = UniqueName();
        var second = UniqueName();

        await _client.PostAsJsonAsync("/api/v1/categories", new CreateCategoryRequest(first));
        var other = await _client.PostAsJsonAsync(
            "/api/v1/categories", new CreateCategoryRequest(second));
        var option = await other.Content.ReadFromJsonAsync<CategoryOption>();

        var response = await _client.PutAsJsonAsync(
            $"/api/v1/categories/{option!.Id}", new RenameCategoryRequest(first));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).ShouldContain("already exists");
    }

    [Fact]
    public async Task Renaming_an_unknown_category_is_not_found()
    {
        var response = await _client.PutAsJsonAsync(
            "/api/v1/categories/999999", new RenameCategoryRequest("Ghost"));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Status alone does not discriminate: an absent route is also a 404. Only the
        // service's own rejection carries a problem document with our authored message,
        // so without these the test would pass with the endpoint never written.
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        (await response.Content.ReadAsStringAsync()).ShouldContain("does not exist");
    }

    [Fact]
    public async Task Renaming_to_a_blank_name_is_rejected_as_validation()
    {
        var created = await _client.PostAsJsonAsync(
            "/api/v1/categories", new CreateCategoryRequest(UniqueName()));
        var option = await created.Content.ReadFromJsonAsync<CategoryOption>();

        var response = await _client.PutAsJsonAsync(
            $"/api/v1/categories/{option!.Id}", new RenameCategoryRequest("   "));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_blank_name_is_rejected_as_validation()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/v1/categories", new CreateCategoryRequest("   "));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
