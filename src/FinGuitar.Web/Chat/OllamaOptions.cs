namespace FinGuitar.Web.Chat;

/// <summary>The "Ollama" section of appsettings.json; override with Ollama__Endpoint, Ollama__Model, Ollama__TimeoutSeconds.</summary>
public class OllamaOptions
{
    public string Endpoint { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "qwen3.5:4b";
    public int TimeoutSeconds { get; set; } = 120;
}
