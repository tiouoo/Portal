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

    public static async Task<YggdrasilRefreshResult?> RefreshYggdrasil(
        MinecraftAccount account,
        IEnumerable<MinecraftAccount> allAccounts)
    {
        if (account.AccountType != AccountType.Yggdrasil ||
            string.IsNullOrEmpty(account.YggdrasilServerUrl) ||
            string.IsNullOrEmpty(account.Email) ||
            string.IsNullOrEmpty(account.Password))
        {
            return null;
        }

        try
        {
            Logger.Debug("正在重新登录外置账户 " + account.AsJson());

            var normalizedUrl = UrlHelper.NormalizeUrl(account.YggdrasilServerUrl);
            var existingAccounts = allAccounts
                .Where(candidate => candidate.AccountType == AccountType.Yggdrasil &&
                                    candidate.Email == account.Email &&
                                    candidate.Password == account.Password &&
                                    string.Equals(
                                        UrlHelper.NormalizeUrl(candidate.YggdrasilServerUrl ?? string.Empty),
                                        normalizedUrl,
                                        StringComparison.OrdinalIgnoreCase))
                .ToList();

            var authenticator = new YggdrasilAuthenticator(account.YggdrasilServerUrl, account.Email, account.Password);
            var authenticatedAccounts = await authenticator.AuthenticateAsync();
            if (authenticatedAccounts == null)
            {
                return null;
            }

            var existingByUuid = existingAccounts
                .Where(candidate => candidate.Uuid.HasValue)
                .GroupBy(candidate => candidate.Uuid!.Value)
                .ToDictionary(group => group.Key, group => group.First());
            var refreshedAccounts = new List<MinecraftAccount>();
            var refreshedUuids = new HashSet<Guid>();

            foreach (var authenticatedAccount in authenticatedAccounts)
            {
                if (!refreshedUuids.Add(authenticatedAccount.Uuid))
                {
                    continue;
                }

                existingByUuid.TryGetValue(authenticatedAccount.Uuid, out var existingAccount);
                refreshedAccounts.Add(await CreateYggdrasilAccount(
                    authenticatedAccount,
                    account,
                    existingAccount));
            }

            var updated = refreshedAccounts.Where(candidate => existingByUuid.ContainsKey(candidate.Uuid!.Value)).ToList();
            var added = refreshedAccounts.Where(candidate => !existingByUuid.ContainsKey(candidate.Uuid!.Value)).ToList();
            var removed = existingAccounts.Where(candidate => !candidate.Uuid.HasValue || !refreshedUuids.Contains(candidate.Uuid.Value))
                .ToList();

            return new YggdrasilRefreshResult(existingAccounts, refreshedAccounts, updated, added, removed);
        }
        catch (Exception e)
        {
            Logger.Error("重新登录外置账户失败  " + e.Message);
            return null;
        }
    }

    private static async Task<MinecraftAccount> CreateYggdrasilAccount(
        YggdrasilAccount authenticatedAccount,
        MinecraftAccount loginAccount,
        MinecraftAccount? existingAccount)
    {
        var skinBase64 = MinecraftAccount.SteveSkin;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await using var skinStream = await SkinProvider.GetYggdrasilSkinDataAsync(authenticatedAccount, cts.Token);
            using var ms = new MemoryStream();
            await skinStream.CopyToAsync(ms, cts.Token);
            skinBase64 = ms.ToArray().ToBase64();
        }
        catch (Exception e)
        {
            Logger.Error("获取皮肤失败  " + e.Message);
        }

        return new MinecraftAccount(AccountType.Yggdrasil)
        {
            AccessToken = authenticatedAccount.AccessToken,
            ClientToken = authenticatedAccount.ClientToken,
            CreateAt = existingAccount?.CreateAt ?? DateTime.Now,
            LastLoginTime = existingAccount?.LastLoginTime ?? DateTime.MinValue,
            LastRefreshTime = DateTime.Now,
            Uuid = authenticatedAccount.Uuid,
            Name = authenticatedAccount.Name,
            YggdrasilServerUrl = loginAccount.YggdrasilServerUrl,
            Skin = skinBase64,
            AccountNote = existingAccount?.AccountNote,
            ServerNote = existingAccount?.ServerNote ?? loginAccount.ServerNote,
            MetaData = authenticatedAccount.MetaData,
            Email = loginAccount.Email,
            Password = loginAccount.Password,
        };
    }
}

public record YggdrasilRefreshResult(
    IReadOnlyList<MinecraftAccount> Existing,
    IReadOnlyList<MinecraftAccount> Refreshed,
    IReadOnlyList<MinecraftAccount> Updated,
    IReadOnlyList<MinecraftAccount> Added,
    IReadOnlyList<MinecraftAccount> Removed);
