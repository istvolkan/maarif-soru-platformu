using System.Text.Json;
using Anthropic.Models.Messages;
using MaarifPlatform.Infrastructure.Ai;

namespace MaarifPlatform.Tests.Ai;

public class ClaudeCliLLMProviderTests
{
    private const string SuccessOutput = """
        {"type":"result","subtype":"success","is_error":false,"duration_ms":2227,"total_cost_usd":0.006303,
         "usage":{"input_tokens":2,"output_tokens":60,"cache_creation_input_tokens":1179,"cache_read_input_tokens":5},
         "modelUsage":{"claude-haiku-4-5-20251001":{"outputTokens":15},"claude-sonnet-5":{"outputTokens":60}},
         "result":"{\"answer\":5}","structured_output":{"answer":5,"notes":["a","b"]}}
        """;

    [Fact]
    public void ParseOutput_Success_ReturnsStructuredOutputAndUsage()
    {
        var (input, usage) = ClaudeCliLLMProvider.ParseOutput(0, SuccessOutput, "", "sonnet", 9999);

        Assert.Equal(5, input["answer"].GetInt32());
        Assert.Equal(2, input["notes"].GetArrayLength());
        Assert.Equal("claude-cli", usage.Provider);
        Assert.Equal(2, usage.InputTokens);
        Assert.Equal(60, usage.OutputTokens);
        Assert.Equal(1179, usage.CacheCreationInputTokens);
        Assert.Equal(5, usage.CacheReadInputTokens);
        Assert.Equal(0.006303m, usage.CostUsd);
        Assert.Equal(2227, usage.LatencyMs);
    }

    [Fact]
    public void ParseOutput_PicksModelWithMostOutputTokens_NotHelperModel()
    {
        var (_, usage) = ClaudeCliLLMProvider.ParseOutput(0, SuccessOutput, "", "sonnet", 0);

        Assert.Equal("claude-sonnet-5", usage.Model);
    }

    [Fact]
    public void ParseOutput_IsError_ThrowsWithResultText()
    {
        const string output = """{"type":"result","subtype":"success","is_error":true,"result":"Not logged in"}""";

        var ex = Assert.Throws<InvalidOperationException>(() => ClaudeCliLLMProvider.ParseOutput(1, output, "", null, 0));

        Assert.Contains("Not logged in", ex.Message);
    }

    [Fact]
    public void ParseOutput_NonJson_ThrowsWithStderr()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => ClaudeCliLLMProvider.ParseOutput(1, "", "command not found", null, 0));

        Assert.Contains("command not found", ex.Message);
    }

    [Fact]
    public void ParseOutput_MissingStructuredOutput_Throws()
    {
        const string output = """{"type":"result","subtype":"success","is_error":false,"result":"düz metin"}""";

        Assert.Throws<InvalidOperationException>(() => ClaudeCliLLMProvider.ParseOutput(0, output, "", null, 0));
    }

    [Fact]
    public void BuildJsonSchema_WrapsToolPropertiesAndRequired()
    {
        var tool = new Tool
        {
            Name = "submit_x",
            Description = "test",
            InputSchema = new()
            {
                Properties = new Dictionary<string, JsonElement>
                {
                    ["score"] = JsonSerializer.SerializeToElement(new { type = "integer", description = "0-100" })
                },
                Required = ["score"]
            }
        };

        using var schema = JsonDocument.Parse(ClaudeCliLLMProvider.BuildJsonSchema(tool));
        var root = schema.RootElement;

        Assert.Equal("object", root.GetProperty("type").GetString());
        Assert.Equal("integer", root.GetProperty("properties").GetProperty("score").GetProperty("type").GetString());
        Assert.Equal("score", root.GetProperty("required")[0].GetString());
    }

    [Fact]
    public void BuildJsonSchema_OptionalPropertiesAcceptNull_RequiredStayStrict()
    {
        var tool = new Tool
        {
            Name = "submit_x",
            InputSchema = new()
            {
                Properties = new Dictionary<string, JsonElement>
                {
                    ["question"] = JsonSerializer.SerializeToElement(new { type = "string" }),
                    ["code"] = JsonSerializer.SerializeToElement(new { type = "string" }),
                    ["kind"] = JsonSerializer.SerializeToElement(new { type = "string", @enum = new[] { "a", "b" } }),
                    ["items"] = JsonSerializer.SerializeToElement(new
                    {
                        type = "array",
                        items = new
                        {
                            type = "object",
                            properties = new { label = new { type = "string" }, note = new { type = "string" } },
                            required = new[] { "label" }
                        }
                    })
                },
                Required = ["question", "items"]
            }
        };

        using var schema = JsonDocument.Parse(ClaudeCliLLMProvider.BuildJsonSchema(tool));
        var properties = schema.RootElement.GetProperty("properties");
        var itemProperties = properties.GetProperty("items").GetProperty("items").GetProperty("properties");

        Assert.Equal("string", properties.GetProperty("question").GetProperty("type").GetString());
        Assert.Equal("array", properties.GetProperty("items").GetProperty("type").GetString());
        Assert.Equal("[\"string\",\"null\"]", properties.GetProperty("code").GetProperty("type").GetRawText());
        Assert.Equal("[\"a\",\"b\",null]", properties.GetProperty("kind").GetProperty("enum").GetRawText());
        Assert.Equal("string", itemProperties.GetProperty("label").GetProperty("type").GetString());
        Assert.Equal("[\"string\",\"null\"]", itemProperties.GetProperty("note").GetProperty("type").GetRawText());
    }
}
