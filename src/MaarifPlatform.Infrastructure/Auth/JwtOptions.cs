namespace MaarifPlatform.Infrastructure.Auth;

public class JwtOptions
{
    public string Issuer { get; set; } = "maarif-platform";
    public string Audience { get; set; } = "maarif-platform";
    public string SigningKey { get; set; } = string.Empty;
    public int ExpiryMinutes { get; set; } = 60;
}
