using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using MaarifPlatform.Application.Providers;
using MaarifPlatform.Infrastructure.Ai;
using Microsoft.Extensions.Options;
using OpenAI.Responses;

#pragma warning disable OPENAI001

namespace MaarifPlatform.Tests.Ai;

public class OpenAiLLMProviderTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("gpt-6-sol")]
    [InlineData("gpt-6-astra")]
    public async Task Generate_UsesResponsesAndParsesToolOutput(string? modelOverride)
    {
        var options = new OpenAiOptions { ApiKey = "test-key", Model = "gpt-6-luna", MaxTokens = 4096 };
        var model = modelOverride ?? options.Model;
        using var handler = new ResponseHandler();
        using var httpClient = new HttpClient(handler);
        var client = new ResponsesClient(new ApiKeyCredential(options.ApiKey), new ResponsesClientOptions
        {
            Transport = new HttpClientPipelineTransport(httpClient)
        });
        var provider = new OpenAiLLMProvider(new Monitor(options));
        // Inject a transport without making external API calls or using real credentials.
        typeof(OpenAiLLMProvider).GetField("_client", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(provider, client);
        typeof(OpenAiLLMProvider).GetField("_clientKey", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(provider, $"{options.ApiKey}|{model}");

        var result = await provider.GenerateQuestionAsync(new GenerateQuestionRequest(
            9, "Matematik", "Sayilar", "MAT.9.1.1", "Kolay", "CoktanSecmeli", "", "", [], ModelOverride: modelOverride));

        Assert.Equal("/v1/responses", handler.Uri!.AbsolutePath);
        using var body = JsonDocument.Parse(handler.Body!);
        var root = body.RootElement;
        Assert.Equal(model, root.GetProperty("model").GetString());
        Assert.False(root.GetProperty("store").GetBoolean());
        Assert.Equal(4096, root.GetProperty("max_output_tokens").GetInt32());
        Assert.False(root.TryGetProperty("reasoning_effort", out _));
        Assert.Equal("submit_generation", root.GetProperty("tool_choice").GetProperty("name").GetString());
        var tool = root.GetProperty("tools")[0];
        Assert.Equal("function", tool.GetProperty("type").GetString());
        Assert.False(tool.GetProperty("strict").GetBoolean());
        Assert.True(tool.GetProperty("parameters").GetProperty("properties").TryGetProperty("question", out _));
        Assert.Equal("system", root.GetProperty("input")[0].GetProperty("role").GetString());
        Assert.Equal("user", root.GetProperty("input")[1].GetProperty("role").GetString());
        Assert.Equal("2 + 2?", result.Question);
        Assert.Equal("4", result.CorrectAnswer);
        Assert.Equal(model, result.Usage.Model);
        Assert.Equal(100, result.Usage.InputTokens);
        Assert.Equal(25, result.Usage.OutputTokens);
        Assert.Equal(20, result.Usage.CacheReadInputTokens);
    }

    private sealed class Monitor(OpenAiOptions value) : IOptionsMonitor<OpenAiOptions>
    {
        public OpenAiOptions CurrentValue => value;
        public OpenAiOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<OpenAiOptions, string?> listener) => null;
    }

    private sealed class ResponseHandler : HttpMessageHandler
    {
        public Uri? Uri { get; private set; }
        public string? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri = request.RequestUri;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            var arguments = JsonSerializer.Serialize(new
            {
                question = "2 + 2?", options = new[] { "3", "4", "5" }, correct_answer = "4",
                solution = "2 + 2 = 4", distractors = Array.Empty<object>(), visual_required = false
            });
            var response = JsonSerializer.Serialize(new
            {
                id = "resp_test", @object = "response", created_at = 1, status = "completed", model = "gpt-6-sol",
                output = new[] { new { type = "function_call", id = "fc_test", call_id = "call_test", name = "submit_generation", arguments, status = "completed" } },
                usage = new { input_tokens = 100, output_tokens = 25, total_tokens = 125,
                    input_tokens_details = new { cached_tokens = 20 }, output_tokens_details = new { reasoning_tokens = 10 } }
            });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }
}
