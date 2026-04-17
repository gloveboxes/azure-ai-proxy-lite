namespace AzureAIProxy.Shared.Database;

public enum ModelType
{
    Azure_AI_Search,
    Foundry_Agent,
    MCP_Server,
    Foundry_Toolkit,
    Foundry_Model
}

public static class ModelTypeExtensions
{
    private static readonly Dictionary<string, ModelType> _map = new()
    {
        ["azure-ai-search"] = ModelType.Azure_AI_Search,
        ["foundry-agent"] = ModelType.Foundry_Agent,
        ["mcp-server"] = ModelType.MCP_Server,
        ["foundry-toolkit"] = ModelType.Foundry_Toolkit,
        ["foundry-model"] = ModelType.Foundry_Model,
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
