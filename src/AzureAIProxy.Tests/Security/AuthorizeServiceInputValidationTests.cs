using Azure.Data.Tables;
using AzureAIProxy.Services;
using AzureAIProxy.Shared.Services;
using AzureAIProxy.Shared.TableStorage;
using Microsoft.Extensions.Logging.Abstractions;

namespace AzureAIProxy.Tests.Security;

public class AuthorizeServiceInputValidationTests
{
    private static AuthorizeService CreateSut() =>
        new(new ThrowingTableStorageService(), new NullEventLookupService(), NullLogger<AuthorizeService>.Instance);

    [Fact]
    public async Task IsUserAuthorizedAsync_ShortApiKey_ReturnsNullWithoutThrowing()
    {
        var sut = CreateSut();

        var result = await sut.IsUserAuthorizedAsync("x");

        Assert.Null(result);
    }

    [Fact]
    public async Task IsUserAuthorizedAsync_ApiKeyWithControlCharacters_ReturnsNullWithoutThrowing()
    {
        var sut = CreateSut();

        var result = await sut.IsUserAuthorizedAsync("ab\ncd");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetRequestContextFromJwtAsync_InvalidBase64_ReturnsNull()
    {
        var sut = CreateSut();

        var result = await sut.GetRequestContextFromJwtAsync("%%%not-base64%%%");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetRequestContextFromJwtAsync_InvalidJson_ReturnsNull()
    {
        var sut = CreateSut();
        var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("not-json"));

        var result = await sut.GetRequestContextFromJwtAsync(encoded);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetRequestContextFromJwtAsync_ValidPayload_ReturnsUserId()
    {
        var sut = CreateSut();
        var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("{\"userId\":\"user-123\"}"));

        var result = await sut.GetRequestContextFromJwtAsync(encoded);

        Assert.Equal("user-123", result);
    }

    [Fact]
    public void AttendeeLookupEntity_TryGetPartitionKey_RejectsShortInput()
    {
        var success = AttendeeLookupEntity.TryGetPartitionKey("a", out var partitionKey);

        Assert.False(success);
        Assert.Equal(string.Empty, partitionKey);
    }

    [Fact]
    public void AttendeeLookupEntity_TryGetPartitionKey_NormalizesPrefix()
    {
        var success = AttendeeLookupEntity.TryGetPartitionKey("ABCD", out var partitionKey);

        Assert.True(success);
        Assert.Equal("ab", partitionKey);
    }

    private sealed class ThrowingTableStorageService : ITableStorageService
    {
        public TableClient GetTableClient(string tableName) =>
            throw new InvalidOperationException("GetTableClient should not be called for malformed input.");
    }

    private sealed class NullEventLookupService : IEventLookupService
    {
        public Task<EventEntity?> GetEventAsync(string eventId) => Task.FromResult<EventEntity?>(null);
    }
}
