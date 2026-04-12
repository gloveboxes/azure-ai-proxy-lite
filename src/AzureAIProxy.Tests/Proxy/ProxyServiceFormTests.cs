using System.Net;
using System.Text;
using AzureAIProxy.Models;
using AzureAIProxy.Services;
using AzureAIProxy.Shared.Database;
using AzureAIProxy.Tests.TestDoubles;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace AzureAIProxy.Tests.Proxy;

/// <summary>
/// Tests for ProxyService.HttpPostFormAsync — verifies that form field values
/// are forwarded as-is to the upstream API (no HTML encoding).
/// </summary>
public class ProxyServiceFormTests
{
    [Fact]
    public async Task HttpPostFormAsync_FormFields_ForwardedWithoutEncoding()
    {
        string? capturedContent = null;

        var handler = new RecordingHttpMessageHandler(async (request, _) =>
        {
            // Capture the multipart content for inspection
            capturedContent = request.Content is not null
                ? await request.Content.ReadAsStringAsync()
                : null;
            return TestData.JsonResponse(HttpStatusCode.OK, "{\"id\":\"file-123\"}");
        });

        var service = new ProxyService(
            new StubHttpClientFactory(new HttpClient(handler)),
            new NoopMetricService(),
            NullLogger<ProxyService>.Instance);

        var deployment = TestData.CreateDeployment(
            ModelType.Foundry_Agent.ToStorageString(),
            useManagedIdentity: false);

        // Build a fake multipart form request with fields containing special characters
        var context = new DefaultHttpContext();
        var formFileContent = "file content here"u8.ToArray();
        var formFile = new FormFile(
            new MemoryStream(formFileContent), 0, formFileContent.Length, "file", "test.txt")
        {
            Headers = new HeaderDictionary(),
            ContentType = "text/plain"
        };

        // Value with HTML-sensitive characters that should NOT be encoded
        const string purposeValue = "test with <angle> & \"quotes\"";

        var formCollection = new FormCollection(
            new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
            {
                ["purpose"] = purposeValue,
                ["extra"] = "normal-value"
            },
            new FormFileCollection { formFile });

        context.Request.ContentType = "multipart/form-data";
        context.Request.Form = formCollection;

        var (responseContent, statusCode) = await service.HttpPostFormAsync(
            new UriBuilder("https://upstream.example.com/files"),
            [new RequestHeader("api-key", "test-key")],
            context,
            context.Request,
            TestData.CreateRequestContext(),
            deployment);

        Assert.Equal(200, statusCode);
        Assert.NotNull(capturedContent);

        // The raw value must appear as-is, NOT as &lt;angle&gt; &amp; &quot;quotes&quot;
        Assert.Contains(purposeValue, capturedContent);
        Assert.DoesNotContain("&amp;", capturedContent);
        Assert.DoesNotContain("&lt;", capturedContent);
        Assert.DoesNotContain("&gt;", capturedContent);
        Assert.DoesNotContain("&quot;", capturedContent);
    }

    [Fact]
    public async Task HttpPostFormAsync_NoFile_ReturnsUnsupportedMediaType()
    {
        var handler = new RecordingHttpMessageHandler((_, _) =>
            Task.FromResult(TestData.JsonResponse(HttpStatusCode.OK, "{}")));

        var service = new ProxyService(
            new StubHttpClientFactory(new HttpClient(handler)),
            new NoopMetricService(),
            NullLogger<ProxyService>.Instance);

        var deployment = TestData.CreateDeployment(
            ModelType.Foundry_Agent.ToStorageString());

        var context = new DefaultHttpContext();
        // Form with no files
        context.Request.ContentType = "multipart/form-data";
        context.Request.Form = new FormCollection(
            new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
            {
                ["purpose"] = "assistants"
            },
            new FormFileCollection());

        var (_, statusCode) = await service.HttpPostFormAsync(
            new UriBuilder("https://upstream.example.com/files"),
            [new RequestHeader("api-key", "test-key")],
            context,
            context.Request,
            TestData.CreateRequestContext(),
            deployment);

        Assert.Equal((int)HttpStatusCode.UnsupportedMediaType, statusCode);
    }
}
