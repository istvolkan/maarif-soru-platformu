using MaarifPlatform.Application.Vision;
using Microsoft.Extensions.DependencyInjection;

namespace MaarifPlatform.Infrastructure.Vision;

public class VisionProviderFactory(IServiceProvider serviceProvider) : IVisionProviderFactory
{
    public IVisionProvider Get(string providerName) => providerName?.Trim().ToLowerInvariant() switch
    {
        "gemini" => serviceProvider.GetRequiredService<GeminiVisionProvider>(),
        "anthropic" => serviceProvider.GetRequiredService<AnthropicVisionProvider>(),
        _ => serviceProvider.GetRequiredService<LocalMockVisionProvider>()
    };
}
