using System.Security.Cryptography;
using MaarifPlatform.Infrastructure.Configuration;

namespace MaarifPlatform.Tests.Configuration;

public class SettingsSecretProtectorTests
{
    [Fact]
    public void Ciphertext_IsRandomizedAndBoundToSettingKey()
    {
        var protector = new SettingsSecretProtector(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        var encrypted = protector.Protect("Ai:Anthropic:ApiKey", "secret");
        Assert.NotEqual(encrypted, protector.Protect("Ai:Anthropic:ApiKey", "secret"));
        Assert.Equal("secret", protector.Unprotect("Ai:Anthropic:ApiKey", encrypted));
        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect("Judge:OpenAI:ApiKey", encrypted));
        var other = new SettingsSecretProtector(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        Assert.ThrowsAny<CryptographicException>(() => other.Unprotect("Ai:Anthropic:ApiKey", encrypted));
    }
}
