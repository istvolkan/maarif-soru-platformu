using MaarifPlatform.Application.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace MaarifPlatform.Infrastructure.Ai;

public class LLMProviderFactory(IServiceProvider serviceProvider) : ILLMProviderFactory
{
    public ILLMProvider Get(string providerName) => providerName?.Trim().ToLowerInvariant() switch
    {
        "anthropic" => serviceProvider.GetRequiredService<AnthropicLLMProvider>(),
        "openai" => serviceProvider.GetRequiredService<OpenAiLLMProvider>(),
        _ => serviceProvider.GetRequiredService<LocalHeuristicLLMProvider>()
    };
}
