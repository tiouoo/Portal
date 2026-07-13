using MinecraftLaunch.Base.Models.Authentication;
using MinecraftLaunch.Components.Authenticator;
using MinecraftLaunch.Skin.Class.Fetchers;
using Portal.Core.Minecraft.Classes;
using Tio.Avalonia.Standard.Modules.Extensions;

namespace Portal.Core.Operations.Account;

public static class AccountRefresher
{
    public static async Task<MinecraftAccount?> RefreshMicrosoft(MinecraftAccount account)
    {
        if (account.AccountType != AccountType.Microsoft || string.IsNullOrEmpty(account.RefreshToken))
            return null;

        try
        {
            var authenticator = new MicrosoftAuthenticator("c06d4d68-7751-4a8a-a2ff-d1b46688f428");
            var authResult = await authenticator.RefreshAsync(new MicrosoftAccount(account.Name, (Guid)account.Uuid!,
                account.AccessToken, account.RefreshToken, account.LastLoginTime));

            var skinFetcher = new MicrosoftSkinFetcher(authResult.Uuid.ToString());
            var skinBytes = await skinFetcher.GetSkinAsync();
            var skinBase64 = skinBytes.ToBase64();

            var newAccount = new MinecraftAccount(AccountType.Microsoft)
            {
                CreateAt = account.CreateAt,
                LastLoginTime = account.LastLoginTime,
                LastRefreshTime = DateTime.Now,
                RefreshToken = authResult.RefreshToken,
                AccessToken = authResult.AccessToken,
                Uuid = authResult.Uuid,
                Name = authResult.Name,
                Skin = skinBase64,
                AccountNote = account.AccountNote,
            };

            return newAccount;
        }
        catch
        {
            return null;
        }
    }

    public static async Task<MinecraftAccount?> RefreshYggdrasil(MinecraftAccount account)
    {
        if (account.AccountType != AccountType.Yggdrasil ||
            string.IsNullOrEmpty(account.YggdrasilServerUrl) ||
            string.IsNullOrEmpty(account.AccessToken))
            return null;

        try
        {
            var authenticator = new YggdrasilAuthenticator(
                account.YggdrasilServerUrl,
                account.Email,
                account.Password);

            var result = await authenticator.RefreshAsync(new YggdrasilAccount(account.Name, (Guid)account.Uuid!,
                account.AccessToken, account.YggdrasilServerUrl, account.ClientToken));
            if (result == null) return null;

            var skinFetcher = new YggdrasilSkinFetcher(account.YggdrasilServerUrl, result.Uuid.ToString());
            var skinBase64 = (await skinFetcher.GetSkinAsync()).ToBase64();

            var newAccount = new MinecraftAccount(AccountType.Yggdrasil)
            {
                AccessToken = result.AccessToken,
                ClientToken = result.ClientToken,
                CreateAt = account.CreateAt,
                LastLoginTime = account.LastLoginTime,
                LastRefreshTime = DateTime.Now,
                Uuid = result.Uuid,
                Name = result.Name,
                YggdrasilServerUrl = account.YggdrasilServerUrl,
                Skin = skinBase64,
                AccountNote = account.AccountNote,
                ServerNote = account.ServerNote,
                MetaData = result.MetaData,
                Email = account.Email,
                Password = account.Password,
            };

            return newAccount;
        }
        catch
        {
            return null;
        }
    }
}