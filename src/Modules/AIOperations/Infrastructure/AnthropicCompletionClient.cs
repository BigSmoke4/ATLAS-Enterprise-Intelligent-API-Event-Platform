using System.Net.Http.Json;
using System.Text.Json;
using Atlas.Modules.AIOperations.Application;

namespace Atlas.Modules.AIOperations.Infrastructure;

public class AnthropicCompletionClientOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "claude-sonnet-4-6";
    public int MaxTokens { get; set; } = 1000;
}

/// <summary>
/// Real HTTP integration with the Anthropic Messages API — genuinely calls
/// out over the network and parses the response; not a stub. Only
/// registered when AI:AnthropicApiKey is configured (see
/// AIOperationsModule), since there's no key to call with otherwise.
/// The prompt explicitly instructs the model to answer ONLY from the
/// supplied evidence and to say so if the evidence is insufficient — this
/// mirrors AiOperationsAssistant's own no-evidence-no-answer rule at the
/// prose-generation layer too.
/// </summary>
public class AnthropicCompletionClient : IAiCompletionClient
{
    private readonly HttpClient _httpClient;
    private readonly AnthropicCompletionClientOptions _options;

    public AnthropicCompletionClient(HttpClient httpClient, AnthropicCompletionClientOptions options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public async Task<string> SummarizeAsync(string question, IReadOnlyList<string> evidenceDescriptions, CancellationToken ct = default)
    {
        var prompt =
            $"""
             You are ATLAS's operations assistant. Answer the question using ONLY the evidence listed below.
             Do not use any outside knowledge or invent facts. If the evidence doesn't fully answer the
             question, say what's missing.

             Question: {question}

             Evidence:
             {string.Join("\n", evidenceDescriptions.Select(e => $"- {e}"))}
             """;

        var request = new
        {
            model = _options.Model,
            max_tokens = _options.MaxTokens,
            messages = new[] { new { role = "user", content = prompt } }
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
        {
            Content = JsonContent.Create(request)
        };
        httpRequest.Headers.Add("x-api-key", _options.ApiKey);
        httpRequest.Headers.Add("anthropic-version", "2023-06-01");

        var response = await _httpClient.SendAsync(httpRequest, ct);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var textParts = doc.RootElement.GetProperty("content").EnumerateArray()
            .Where(c => c.GetProperty("type").GetString() == "text")
            .Select(c => c.GetProperty("text").GetString() ?? string.Empty);

        return string.Join("\n", textParts);
    }
}
