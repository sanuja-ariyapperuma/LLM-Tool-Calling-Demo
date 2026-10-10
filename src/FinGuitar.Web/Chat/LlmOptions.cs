namespace FinGuitar.Web.Chat;

/// <summary>
/// The "Llm" section of appsettings.json; override with Llm__Provider, Llm__Endpoint, Llm__Model, Llm__ApiKey, Llm__TimeoutSeconds.
/// Provider "Ollama" talks to an Ollama server. Provider "OpenAI" talks to any OpenAI-compatible API (OpenAI, Azure OpenAI, Groq, OpenRouter, vLLM, LM Studio, ...).
/// </summary>
public class LlmOptions
{
    public const string Ollama = "Ollama";
    public const string OpenAI = "OpenAI";

    public string Provider { get; set; } = Ollama;
    public string Endpoint { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "qwen3.5:4b";
    /// <summary>Required for the OpenAI provider; ignored by Ollama. Set it with Llm__ApiKey or user-secrets, not appsettings.json.</summary>
    public string? ApiKey { get; set; }
    public int TimeoutSeconds { get; set; } = 120;
}
