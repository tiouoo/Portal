namespace Portal.Bedrock.Standard.Interface;

public sealed record BedrockAuthentication(
    string Gamertag,
    string Xuid,
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAt);
