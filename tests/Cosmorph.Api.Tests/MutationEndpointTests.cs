using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Cosmorph.Api.Tests;

/// <summary>
/// Mutations are deny-by-default: an idempotency key is required, every field is bounded, the actor
/// is resolved server-side and nothing is written directly to the simulation.
/// </summary>
public sealed class MutationEndpointTests : IClassFixture<CosmorphApiFactory>
{
    private readonly CosmorphApiFactory _factory;

    public MutationEndpointTests(CosmorphApiFactory factory) => _factory = factory;

    private const string ValidCharter =
        """
        {"displayName":"Moss Keeper","controlledSpecies":"verdant-moss","controlledRegion":[0,1,2],
         "goals":["Preserve"],"goalWeights":[100],"taboos":["NeverBurn"],
         "actionBudget":10,"budgetRenewalPerChapter":5,"impactCeilingPerChapter":20}
        """;

    private static HttpRequestMessage Request(HttpMethod method, string uri, string? body, string? idempotencyKey)
    {
        var request = new HttpRequestMessage(method, uri);
        if (body is not null)
        {
            request.Content = CosmorphApiFactory.JsonBody(body);
        }

        if (idempotencyKey is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        }

        return request;
    }

    [Fact]
    public async Task CreatingAWorldRequiresAnIdempotencyKey()
    {
        var client = _factory.CreateClient();

        using var request = Request(HttpMethod.Post, "/api/worlds", """{"worldId":"no-key-world","name":"n","seed":1,"isPublic":true}""", null);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("a key with spaces")]
    [InlineData("../../escape")]
    public async Task MalformedIdempotencyKeysAreRejected(string key)
    {
        var client = _factory.CreateClient();

        using var request = Request(HttpMethod.Post, "/api/worlds", """{"worldId":"bad-key-world","name":"n","seed":1,"isPublic":true}""", key);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task OverlongIdempotencyKeysAreRejected()
    {
        var client = _factory.CreateClient();

        using var request = Request(
            HttpMethod.Post,
            "/api/worlds",
            """{"worldId":"long-key-world","name":"n","seed":1,"isPublic":true}""",
            new string('k', 65));
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RepeatingAWorldCreationDoesNotCreateASecondWorld()
    {
        var client = _factory.CreateClient();
        var body = """{"worldId":"repeat-world","name":"Repeat","seed":9,"isPublic":true}""";

        using var first = await client.SendAsync(Request(HttpMethod.Post, "/api/worlds", body, "repeat-1"));
        using var second = await client.SendAsync(Request(HttpMethod.Post, "/api/worlds", body, "repeat-2"));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Theory]
    [InlineData("""{"worldId":"UPPER","name":"n","seed":1,"isPublic":true}""")]
    [InlineData("""{"worldId":"../escape","name":"n","seed":1,"isPublic":true}""")]
    [InlineData("""{"worldId":"ok-world","name":"","seed":1,"isPublic":true}""")]
    [InlineData("""{"worldId":"ok-world","name":"   ","seed":1,"isPublic":true}""")]
    public async Task InvalidWorldRequestsAreRejected(string body)
    {
        var client = _factory.CreateClient();

        using var response = await client.SendAsync(Request(HttpMethod.Post, "/api/worlds", body, "invalid-1"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AnOverlongWorldNameIsRejected()
    {
        var client = _factory.CreateClient();
        var body = $$"""{"worldId":"long-name-world","name":"{{new string('n', 61)}}","seed":1,"isPublic":true}""";

        using var response = await client.SendAsync(Request(HttpMethod.Post, "/api/worlds", body, "long-name-1"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AValidCharterBecomesACommandRatherThanAnImmediateMutation()
    {
        var client = await _factory.CreateWorldAsync("charter-world");

        using var response = await client.SendAsync(
            Request(HttpMethod.Put, "/api/worlds/charter-world/wardens/moss-keeper/charter", ValidCharter, "charter-1"));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var accepted = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(accepted.GetProperty("accepted").GetBoolean());

        // The API never advances the simulation; the world is still at its created position.
        var summary = await client.GetFromJsonAsync<JsonElement>("/api/worlds/charter-world");
        Assert.Equal(0, summary.GetProperty("tick").GetInt64());
    }

    [Fact]
    public async Task ARepeatedCharterCommandIsNotAcceptedTwice()
    {
        var client = await _factory.CreateWorldAsync("charter-repeat-world");

        using var first = await client.SendAsync(
            Request(HttpMethod.Put, "/api/worlds/charter-repeat-world/wardens/moss-keeper/charter", ValidCharter, "charter-repeat"));
        using var second = await client.SendAsync(
            Request(HttpMethod.Put, "/api/worlds/charter-repeat-world/wardens/moss-keeper/charter", ValidCharter, "charter-repeat"));

        Assert.True((await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accepted").GetBoolean());
        Assert.False((await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accepted").GetBoolean());
    }

    [Theory]
    [InlineData("""{"displayName":"n","controlledSpecies":"not-a-species","controlledRegion":[0],"goals":["Preserve"],"goalWeights":[100],"taboos":[],"actionBudget":1,"budgetRenewalPerChapter":1,"impactCeilingPerChapter":1}""")]
    [InlineData("""{"displayName":"n","controlledSpecies":"verdant-moss","controlledRegion":[-1],"goals":["Preserve"],"goalWeights":[100],"taboos":[],"actionBudget":1,"budgetRenewalPerChapter":1,"impactCeilingPerChapter":1}""")]
    [InlineData("""{"displayName":"n","controlledSpecies":"verdant-moss","controlledRegion":[0],"goals":["Preserve"],"goalWeights":[100,50],"taboos":[],"actionBudget":1,"budgetRenewalPerChapter":1,"impactCeilingPerChapter":1}""")]
    [InlineData("""{"displayName":"n","controlledSpecies":"verdant-moss","controlledRegion":[0],"goals":[],"goalWeights":[],"taboos":[],"actionBudget":1,"budgetRenewalPerChapter":1,"impactCeilingPerChapter":1}""")]
    [InlineData("""{"displayName":"n","controlledSpecies":"verdant-moss","controlledRegion":[0],"goals":["Preserve"],"goalWeights":[100],"taboos":[],"actionBudget":2147483647,"budgetRenewalPerChapter":1,"impactCeilingPerChapter":1}""")]
    [InlineData("""{"displayName":"n","controlledSpecies":"verdant-moss","controlledRegion":[0],"goals":["Preserve"],"goalWeights":[-5],"taboos":[],"actionBudget":1,"budgetRenewalPerChapter":1,"impactCeilingPerChapter":1}""")]
    [InlineData("""{"displayName":"n","controlledSpecies":"verdant-moss","controlledRegion":[0],"goals":["ConquerEverything"],"goalWeights":[100],"taboos":[],"actionBudget":1,"budgetRenewalPerChapter":1,"impactCeilingPerChapter":1}""")]
    public async Task CharterValuesOutsideTheFixedVocabularyAreRejected(string body)
    {
        var client = await _factory.CreateWorldAsync("charter-validation-world");

        using var response = await client.SendAsync(
            Request(HttpMethod.Put, "/api/worlds/charter-validation-world/wardens/moss-keeper/charter", body, "charter-invalid"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ACharterForAnotherWorldCannotBeSubmittedThroughAnUnknownWorldPath()
    {
        var client = await _factory.CreateWorldAsync("charter-scope-world");

        using var response = await client.SendAsync(
            Request(HttpMethod.Put, "/api/worlds/no-such-world/wardens/moss-keeper/charter", ValidCharter, "charter-scope"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/worlds/NOT-VALID/wardens/moss-keeper/charter")]
    [InlineData("/api/worlds/charter-scope-world/wardens/NOT-VALID/charter")]
    public async Task InvalidIdentifiersInThePathAreNotFound(string uri)
    {
        var client = await _factory.CreateWorldAsync("charter-scope-world");

        using var response = await client.SendAsync(Request(HttpMethod.Put, uri, ValidCharter, "charter-path"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AnOversizedBodyIsRefused()
    {
        var client = await _factory.CreateWorldAsync("charter-size-world");
        var region = string.Join(',', Enumerable.Range(0, 20_000));
        var body = $$"""
            {"displayName":"n","controlledSpecies":"verdant-moss","controlledRegion":[{{region}}],
             "goals":["Preserve"],"goalWeights":[100],"taboos":[],"actionBudget":1,
             "budgetRenewalPerChapter":1,"impactCeilingPerChapter":1}
            """;

        using var response = await client.SendAsync(
            Request(HttpMethod.Put, "/api/worlds/charter-size-world/wardens/moss-keeper/charter", body, "charter-size"));

        Assert.True(
            response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge,
            $"Unexpected status {(int)response.StatusCode} for an oversized charter body.");
    }

    [Fact]
    public async Task FailureResponsesNeverRevealInternalDetail()
    {
        var client = _factory.CreateClient();

        using var response = await client.SendAsync(Request(HttpMethod.Post, "/api/worlds", "{not json", "bad-json"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode is HttpStatusCode.BadRequest);
        Assert.DoesNotContain("Cosmorph.", body, StringComparison.Ordinal);
        Assert.DoesNotContain("at ", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", body, StringComparison.Ordinal);
    }
}
