namespace AzureAIProxy.Shared.Database;

public enum ModelType
{
    OpenAI_Chat,
    OpenAI_Embedding,
    OpenAI_Dalle3,
    OpenAI_Whisper,
    OpenAI_Completion,
    Azure_AI_Search,
    OpenAI_Assistant
}

public static class ModelTypeExtensions
{
    private static readonly Dictionary<string, ModelType> _map = new()
    {
        ["openai-chat"] = ModelType.OpenAI_Chat,
        ["openai-embedding"] = ModelType.OpenAI_Embedding,
        ["openai-dalle3"] = ModelType.OpenAI_Dalle3,
        ["openai-whisper"] = ModelType.OpenAI_Whisper,
        ["openai-completion"] = ModelType.OpenAI_Completion,
        ["openai-assistant"] = ModelType.OpenAI_Assistant,
        ["azure-ai-search"] = ModelType.Azure_AI_Search,
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
