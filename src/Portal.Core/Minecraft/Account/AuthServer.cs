namespace Portal.Core.Minecraft.Account;

public record AuthServer(AccountType authType, string displayText)
{
    public AccountType AuthType { get; set; } = authType;
    public string DisplayText { get; set; } = displayText;
    public string ServerUrl { get; set; }
}