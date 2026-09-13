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
public class SupplierEndpointTests : IClassFixture<SlingKingApiFactory>, IAsyncLifetime
{
    private readonly SlingKingApiFactory _factory;
    private readonly HttpClient _client;

    public SupplierEndpointTests(SlingKingApiFactory factory)
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
    private record SupplierOption(int Id, string Name, string? Phone);
    private record SupplierRequest(string Name, string? Phone);

    private async Task AuthenticateAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(SlingKingApiFactory.SeedEmail, SlingKingApiFactory.SeedPassword));

        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body!.AccessToken);
    }

    private static string UniqueName() => $"Sup {Guid.NewGuid():N}"[..14];

    [Fact]
    public async Task Listing_suppliers_requires_a_token()
    {
        using var anonymous = _factory.CreateClient();

        (await anonymous.GetAsync("/api/v1/suppliers")).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_created_supplier_appears_in_the_list()
    {
        var name = UniqueName();

        var created = await _client.PostAsJsonAsync(
            "/api/v1/suppliers", new SupplierRequest(name, "98xxxxxx45"));
        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        var option = await created.Content.ReadFromJsonAsync<SupplierOption>();
        option!.Name.ShouldBe(name);
        option.Phone.ShouldBe("98xxxxxx45");
        option.Id.ShouldBeGreaterThan(0);

        var list = await _client.GetFromJsonAsync<List<SupplierOption>>("/api/v1/suppliers");
        list!.ShouldContain(s => s.Id == option.Id && s.Name == name);
    }

    [Fact]
    public async Task A_supplier_may_have_no_phone_number()
    {
        var created = await _client.PostAsJsonAsync(
            "/api/v1/suppliers", new SupplierRequest(UniqueName(), null));

        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await created.Content.ReadFromJsonAsync<SupplierOption>())!.Phone.ShouldBeNull();
    }

    [Fact]
    public async Task A_duplicate_name_is_rejected_as_a_conflict()
    {
        var name = UniqueName();

        await _client.PostAsJsonAsync("/api/v1/suppliers", new SupplierRequest(name, null));
        var again = await _client.PostAsJsonAsync(
            "/api/v1/suppliers", new SupplierRequest(name, null));

        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var problem = await again.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        problem!["title"].ToString().ShouldBe($"A supplier named \"{name}\" already exists.");
    }

    [Fact]
    public async Task A_blank_name_is_rejected()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/v1/suppliers", new SupplierRequest("   ", null));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_supplier_can_be_renamed_and_keeps_its_id()
    {
        var created = await _client.PostAsJsonAsync(
            "/api/v1/suppliers", new SupplierRequest(UniqueName(), "1111"));
        var option = await created.Content.ReadFromJsonAsync<SupplierOption>();

        var newName = UniqueName();
        var renamed = await _client.PutAsJsonAsync(
            $"/api/v1/suppliers/{option!.Id}", new SupplierRequest(newName, "2222"));

        renamed.StatusCode.ShouldBe(HttpStatusCode.OK);

        var updated = await renamed.Content.ReadFromJsonAsync<SupplierOption>();
        updated!.Id.ShouldBe(option.Id);
        updated.Name.ShouldBe(newName);
        updated.Phone.ShouldBe("2222");
    }

    [Fact]
    public async Task Correcting_only_the_capitalisation_of_a_name_is_allowed()
    {
        // The row being renamed must be excluded from the duplicate check, or fixing
        // capitalisation collides with its own existing name.
        var name = UniqueName();
        var created = await _client.PostAsJsonAsync(
            "/api/v1/suppliers", new SupplierRequest(name, null));
        var option = await created.Content.ReadFromJsonAsync<SupplierOption>();

        var response = await _client.PutAsJsonAsync(
            $"/api/v1/suppliers/{option!.Id}", new SupplierRequest(name.ToUpperInvariant(), null));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Renaming_to_a_name_another_supplier_holds_is_a_conflict()
    {
        var first = UniqueName();
        await _client.PostAsJsonAsync("/api/v1/suppliers", new SupplierRequest(first, null));

        var created = await _client.PostAsJsonAsync(
            "/api/v1/suppliers", new SupplierRequest(UniqueName(), null));
        var second = await created.Content.ReadFromJsonAsync<SupplierOption>();

        var response = await _client.PutAsJsonAsync(
            $"/api/v1/suppliers/{second!.Id}", new SupplierRequest(first, null));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Renaming_an_unknown_supplier_is_not_found()
    {
        var response = await _client.PutAsJsonAsync(
            "/api/v1/suppliers/999999", new SupplierRequest(UniqueName(), null));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // A missing route is also a 404, so assert the authored body — otherwise this test
        // passes just as well with no endpoint at all.
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        problem!["title"].ToString().ShouldBe("That supplier no longer exists.");
    }
}
