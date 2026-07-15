using MinecraftLaunch.Base.Models.Authentication;
using MinecraftLaunch.Components.Authenticator;
using MinecraftLaunch.Components.Provider;
using Portal.Core.Helpers;
using Portal.Core.Minecraft.Classes;
using Tio.Avalonia.Standard.Modules.DiskIO;
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
            Logger.Debug("正在刷新账号 " + account.AsJson());
            
            var authenticator = new MicrosoftAuthenticator("c06d4d68-7751-4a8a-a2ff-d1b46688f428");
            var authResult = await authenticator.RefreshAsync(new MicrosoftAccount(account.Name, (Guid)account.Uuid!,
                account.AccessToken, account.RefreshToken, account.LastLoginTime));

            string skinBase64 = MinecraftAccount.SteveSkin;
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await using var skinStream = await SkinProvider.GetMicrosoftSkinDataAsync(authResult, cts.Token);
                using var ms = new MemoryStream();
                await skinStream.CopyToAsync(ms, cts.Token);
                skinBase64 = ms.ToArray().ToBase64();
            }
            catch (Exception e)
            {
                Logger.Error("获取皮肤失败  " + e.Message);
            }

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
        catch (Exception e)
        {
            Logger.Error("刷新账号失败  " + e.Message);
            return null;
        }
    }

  public static async Task<MinecraftAccount?> RefreshYggdrasil(
    MinecraftAccount account, 
    IEnumerable<MinecraftAccount> allAccounts)
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
        
        Logger.Debug("正在刷新账号 " + account.AsJson());

        var result = await authenticator.RefreshAsync(new YggdrasilAccount(account.Name, (Guid)account.Uuid!,
            account.AccessToken, account.YggdrasilServerUrl));
        if (result == null) return null;

        string skinBase64 = MinecraftAccount.SteveSkin;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await using var skinStream = await SkinProvider.GetYggdrasilSkinDataAsync(result, cts.Token);
            using var ms = new MemoryStream();
            await skinStream.CopyToAsync(ms, cts.Token);
            skinBase64 = ms.ToArray().ToBase64();
        }
        catch (Exception e)
        {
            Logger.Error("获取皮肤失败  " + e.Message);
        }

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

        var currentNormalizedUrl =UrlHelper.NormalizeUrl(account.YggdrasilServerUrl);

        foreach (var acc in allAccounts)
        {
            if (acc == account) continue;

            if (acc.AccountType == AccountType.Yggdrasil &&
                acc.Email == account.Email &&
                acc.Password == account.Password &&
                UrlHelper.NormalizeUrl(acc.YggdrasilServerUrl!) == currentNormalizedUrl)
            {
                acc.AccessToken = result.AccessToken;
                acc.ClientToken = result.ClientToken;
                acc.LastRefreshTime = DateTime.Now;
                
                Logger.Debug($"已同步更新相同配置的账号 Token: {acc.Name}");
            }
        }

        return newAccount;
    }
    catch (Exception e)
    {
        Logger.Error("刷新账号失败  " + e.Message);
        return null;
    }
}
}