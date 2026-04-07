// Chat completions example
using System.ClientModel;
using Azure.AI.OpenAI;
using DotNetEnv;
using OpenAI.Chat;

Env.Load();

string? key = Environment.GetEnvironmentVariable("PROXY_API_KEY");
string? endpoint = Environment.GetEnvironmentVariable("PROXY_ENDPOINT");

if (key == null || endpoint == null)
{
    Console.WriteLine("Please set the PROXY_API_KEY and PROXY_ENDPOINT environment variables.");
    return;
}

var client = new AzureOpenAIClient(new Uri(endpoint), new ApiKeyCredential(key));
ChatClient chatClient = client.GetChatClient("gpt-4.1-mini");

ChatCompletion completion = chatClient.CompleteChat(
[
    new SystemChatMessage("You are a helpful assistant. You will talk like a pirate."),
    new UserChatMessage("Can you help me?"),
    new AssistantChatMessage("Arrrr! Of course, me hearty! What can I do for ye?"),
    new UserChatMessage("What's the best way to train a parrot?"),
]);

Console.WriteLine(completion.Content[0].Text);
