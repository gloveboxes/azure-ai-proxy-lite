using System.Net;
using System.Text;
using System.Text.Json;
using AzureAIProxy.Tests.Fixtures;

namespace AzureAIProxy.Tests.Routes;

/// <summary>
/// Tests the /api/v1/attendee/event/{eventId}/register (POST) and
/// /api/v1/attendee/event/{eventId}/ (GET) endpoints.
/// These routes use [JwtAuthorize] which requires x-ms-client-principal header.
/// </summary>
public class AttendeeRouteTests : IClassFixture<ProxyAppFixture>
{
    private readonly ProxyAppFixture _fixture;

    public AttendeeRouteTests(ProxyAppFixture fixture)
    {
        _fixture = fixture;
    }

    private static string JwtHeader(string userId) =>
        Convert.ToBase64String(Encoding.ASCII.GetBytes(JsonSerializer.Serialize(new { userId })));

    // --- JWT authentication ---

    [SkippableFact]
    public async Task Register_WithoutJwt_Returns401()
    {
        Skip.IfNot(_fixture.Available, "Azurite not available");

        var response = await _fixture.Client.PostAsync(
            "/api/v1/attendee/event/some-event/register",
            new StringContent("", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [SkippableFact]
    public async Task Register_EmptyJwt_Returns401()
    {
        Skip.IfNot(_fixture.Available, "Azurite not available");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/attendee/event/some-event/register");
        request.Headers.Add("x-ms-client-principal", "");
        request.Content = new StringContent("", Encoding.UTF8, "application/json");

        var response = await _fixture.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [SkippableFact]
    public async Task Register_JwtWithNoUserId_Returns401()
    {
        Skip.IfNot(_fixture.Available, "Azurite not available");

        // Base64 of {"name":"test"} — no userId field
        var jwt = Convert.ToBase64String(Encoding.ASCII.GetBytes("{\"name\":\"test\"}"));

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/attendee/event/some-event/register");
        request.Headers.Add("x-ms-client-principal", jwt);
        request.Content = new StringContent("", Encoding.UTF8, "application/json");

        var response = await _fixture.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // --- Attendee registration flow ---

    [SkippableFact]
    public async Task Register_ValidJwt_Returns201WithApiKey()
    {
        Skip.IfNot(_fixture.Available, "Azurite not available");

        var eventId = $"evt-{Guid.NewGuid():N}";
        await _fixture.SeedEventAsync(eventId, "owner-1");

        var request = new HttpRequestMessage(HttpMethod.Post,
            $"/api/v1/attendee/event/{eventId}/register");
        request.Headers.Add("x-ms-client-principal", JwtHeader("jwt-user-1"));
        request.Content = new StringContent("", Encoding.UTF8, "application/json");

        var response = await _fixture.Client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("api_key", out var keyProp));
        Assert.False(string.IsNullOrEmpty(keyProp.GetString()));
    }

    [SkippableFact]
    public async Task Register_SameUserTwice_ReturnsSameApiKey()
    {
        Skip.IfNot(_fixture.Available, "Azurite not available");

        var eventId = $"evt-{Guid.NewGuid():N}";
        await _fixture.SeedEventAsync(eventId, "owner-1");

        var jwt = JwtHeader("jwt-user-idempotent");

        // First registration
        var req1 = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/attendee/event/{eventId}/register");
        req1.Headers.Add("x-ms-client-principal", jwt);
        req1.Content = new StringContent("", Encoding.UTF8, "application/json");
        var resp1 = await _fixture.Client.SendAsync(req1);
        var body1 = await resp1.Content.ReadAsStringAsync();

        // Second registration
        var req2 = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/attendee/event/{eventId}/register");
        req2.Headers.Add("x-ms-client-principal", jwt);
        req2.Content = new StringContent("", Encoding.UTF8, "application/json");
        var resp2 = await _fixture.Client.SendAsync(req2);
        var body2 = await resp2.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Created, resp1.StatusCode);
        Assert.Equal(HttpStatusCode.Created, resp2.StatusCode);

        using var doc1 = JsonDocument.Parse(body1);
        using var doc2 = JsonDocument.Parse(body2);
        Assert.Equal(
            doc1.RootElement.GetProperty("api_key").GetString(),
            doc2.RootElement.GetProperty("api_key").GetString());
    }

    // --- Get attendee key ---

    [SkippableFact]
    public async Task GetKey_NonexistentAttendee_Returns404()
    {
        Skip.IfNot(_fixture.Available, "Azurite not available");

        var eventId = $"evt-{Guid.NewGuid():N}";
        await _fixture.SeedEventAsync(eventId, "owner-1");

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/attendee/event/{eventId}/");
        request.Headers.Add("x-ms-client-principal", JwtHeader("nonexistent-user"));

        var response = await _fixture.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [SkippableFact]
    public async Task GetKey_AfterRegistration_ReturnsApiKey()
    {
        Skip.IfNot(_fixture.Available, "Azurite not available");

        var eventId = $"evt-{Guid.NewGuid():N}";
        await _fixture.SeedEventAsync(eventId, "owner-1");
        var userId = "jwt-user-getkey";

        // Register first
        var regReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/attendee/event/{eventId}/register");
        regReq.Headers.Add("x-ms-client-principal", JwtHeader(userId));
        regReq.Content = new StringContent("", Encoding.UTF8, "application/json");
        var regResp = await _fixture.Client.SendAsync(regReq);
        var regBody = await regResp.Content.ReadAsStringAsync();
        using var regDoc = JsonDocument.Parse(regBody);
        var registeredKey = regDoc.RootElement.GetProperty("api_key").GetString();

        // Get key
        var getReq = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/attendee/event/{eventId}/");
        getReq.Headers.Add("x-ms-client-principal", JwtHeader(userId));

        var getResp = await _fixture.Client.SendAsync(getReq);
        var getBody = await getResp.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        using var getDoc = JsonDocument.Parse(getBody);
        Assert.Equal(registeredKey, getDoc.RootElement.GetProperty("apiKey").GetString());
        Assert.True(getDoc.RootElement.GetProperty("active").GetBoolean());
    }
}
