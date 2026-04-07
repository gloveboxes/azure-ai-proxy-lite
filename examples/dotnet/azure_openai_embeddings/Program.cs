using System.ClientModel;
using Azure.AI.OpenAI;
using DotNetEnv;
using OpenAI.Embeddings;

Env.Load();

string? key = Environment.GetEnvironmentVariable("PROXY_API_KEY");
string? endpoint = Environment.GetEnvironmentVariable("PROXY_ENDPOINT");

if (key == null || endpoint == null)
{
    Console.WriteLine("Please set the PROXY_API_KEY and PROXY_ENDPOINT environment variables.");
    return;
}

var client = new AzureOpenAIClient(new Uri(endpoint), new ApiKeyCredential(key));
EmbeddingClient embeddingClient = client.GetEmbeddingClient("text-embedding-3-large");

OpenAIEmbedding embedding = embeddingClient.GenerateEmbedding("Your text string goes here");

ReadOnlyMemory<float> vector = embedding.ToFloats();
Console.WriteLine($"Embedding vector length: {vector.Length}");
Console.WriteLine($"Embedding vector: {string.Join(", ", vector.ToArray())}");
