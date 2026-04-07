// Chat completions with Azure AI Search (Your Data) example

using System.ClientModel;
using Azure.AI.OpenAI;
using Azure.AI.OpenAI.Chat;
using DotNetEnv;
using OpenAI.Chat;

Env.Load();

string? key = Environment.GetEnvironmentVariable("PROXY_API_KEY");
string? endpoint = Environment.GetEnvironmentVariable("PROXY_ENDPOINT");

string? searchEndpoint = Environment.GetEnvironmentVariable("AZURE_AI_SEARCH_ENDPOINT");
string? indexName = Environment.GetEnvironmentVariable("AZURE_AI_SEARCH_INDEX_NAME");
string? searchKey = Environment.GetEnvironmentVariable("AZURE_AI_SEARCH_KEY");

if (key == null || endpoint == null || searchEndpoint == null || indexName == null || searchKey == null)
{
    Console.WriteLine("Please set the PROXY_API_KEY, PROXY_ENDPOINT, AZURE_AI_SEARCH_ENDPOINT, AZURE_AI_SEARCH_INDEX_NAME, and AZURE_AI_SEARCH_KEY environment variables.");
    return;
}

var client = new AzureOpenAIClient(new Uri(endpoint), new ApiKeyCredential(key));
ChatClient chatClient = client.GetChatClient("gpt-4.1-mini");

ChatCompletionOptions options = new();

#pragma warning disable AOAI001
options.AddDataSource(new AzureSearchChatDataSource()
{
    Endpoint = new Uri(searchEndpoint),
    IndexName = indexName,
    Authentication = DataSourceAuthentication.FromApiKey(searchKey),
});
#pragma warning restore AOAI001

ChatCompletion completion = chatClient.CompleteChat(
    [new UserChatMessage("What are the differences between Azure Machine Learning and Azure AI services?")],
    options);

Console.WriteLine($"Content: {completion.Content[0].Text}");
