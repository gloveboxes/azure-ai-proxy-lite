namespace AzureAIProxy.Shared.Database;

public enum ModelType
{
    OpenAI_Chat,
    OpenAI_Embedding,
    Azure_AI_Search,
    Foundry_Agent,
    MCP_Server,
    AI_Toolkit
}

public static class ModelTypeExtensions
{
    private static readonly Dictionary<string, ModelType> _map = new()
    {
        ["openai-chat"] = ModelType.OpenAI_Chat,
        ["openai-embedding"] = ModelType.OpenAI_Embedding,
        ["azure-ai-search"] = ModelType.Azure_AI_Search,
        ["foundry-agent"] = ModelType.Foundry_Agent,
        ["mcp-server"] = ModelType.MCP_Server,
        ["ai-toolkit"] = ModelType.AI_Toolkit,
    };

    private static readonly Dictionary<ModelType, string> _reverseMap =
        _map.ToDictionary(kvp => kvp.Value, kvp => kvp.Key);

    public static ModelType ParsePostgresValue(string value) =>
        _map.TryGetValue(value, out var result)
            ? result
            : throw new ArgumentOutOfRangeException(nameof(value), value, null);

    public static string ToStorageString(this ModelType modelType) =>
        _reverseMap[modelType];

    public static ModelType FromStorageString(string value) =>
        _map.TryGetValue(value, out var result) ? result : throw new ArgumentOutOfRangeException(nameof(value), value, null);
}
