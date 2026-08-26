using Iridium.Models.Authentication;
using Iridium.Minecraft;
using Iridium.Authentication;
using Portal.Core.Helpers;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Services;
using Portal.Localization;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Modules.Extensions;

namespace Portal.Core.Minecraft.Services;

public static class AccountRefresher
{
    public static async Task<MinecraftAccount?> RefreshMicrosoft(MinecraftAccount account)
    {
        if (account.AccountType != AccountType.Microsoft || string.IsNullOrEmpty(account.RefreshToken))
            return null;

        try
        {
            Logger.Info(string.Format(LogLanguageManager.Instance.accountRefresher_microsoftRefreshStart.CurrentValue(), account.Name));

            var authenticator = new MicrosoftAuthenticator(CredentialsService.MicrosoftClientId);
            var authResult = await authenticator.RefreshAsync(new MicrosoftAccount(account.Name, (Guid)account.Uuid!,
                account.AccessToken, account.RefreshToken, account.LastLoginTime));

            var newAccount = new MinecraftAccount(AccountType.Microsoft)
            {
                CreateAt = account.CreateAt,
                LastLoginTime = account.LastLoginTime,
                LastRefreshTime = DateTime.Now,
                RefreshToken = authResult.RefreshToken,
                AccessToken = authResult.AccessToken,
                Uuid = authResult.Uuid,
                Name = authResult.Name,
                Skin = account.Skin ?? MinecraftAccount.SteveSkin,
                AccountNote = account.AccountNote
            };

            Logger.Info(string.Format(LogLanguageManager.Instance.accountRefresher_microsoftRefreshComplete.CurrentValue(), newAccount.Name));
            return newAccount;
        }
        catch (Exception e)
        {
            Logger.Error(string.Format(LogLanguageManager.Instance.accountRefresher_microsoftRefreshFailed.CurrentValue(), account.Name, e));
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
            return null;

        try
        {
            Logger.Info(string.Format(LogLanguageManager.Instance.accountRefresher_yggdrasilLoginStart.CurrentValue(), account.Name, account.YggdrasilServerUrl));

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

            var authenticator = new YggdrasilAuthenticator(account.YggdrasilServerUrl);
            var authenticatedAccounts = await authenticator.AuthenticateAsync(account.Email, account.Password);

            var existingByUuid = existingAccounts
                .Where(candidate => candidate.Uuid.HasValue)
                .GroupBy(candidate => candidate.Uuid!.Value)
                .ToDictionary(group => group.Key, group => group.First());
            var refreshedAccounts = new List<MinecraftAccount>();
            var refreshedUuids = new HashSet<Guid>();

            foreach (var authenticatedAccount in authenticatedAccounts)
            {
                if (!refreshedUuids.Add(authenticatedAccount.Uuid)) continue;

                existingByUuid.TryGetValue(authenticatedAccount.Uuid, out var existingAccount);
                refreshedAccounts.Add(await CreateYggdrasilAccount(
                    authenticatedAccount,
                    account,
                    existingAccount));
            }

            var updated = refreshedAccounts.Where(candidate => existingByUuid.ContainsKey(candidate.Uuid!.Value))
                .ToList();
            var added = refreshedAccounts.Where(candidate => !existingByUuid.ContainsKey(candidate.Uuid!.Value))
                .ToList();
            var removed = existingAccounts.Where(candidate =>
                    !candidate.Uuid.HasValue || !refreshedUuids.Contains(candidate.Uuid.Value))
                .ToList();

            Logger.Info(string.Format(LogLanguageManager.Instance.accountRefresher_yggdrasilLoginComplete.CurrentValue(),
                account.Name, refreshedAccounts.Count, added.Count, removed.Count));
            return new YggdrasilRefreshResult(existingAccounts, refreshedAccounts, updated, added, removed);
        }
        catch (Exception e)
        {
            Logger.Error(string.Format(LogLanguageManager.Instance.accountRefresher_yggdrasilLoginFailed.CurrentValue(), account.Name, e));
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
            Logger.Error(LogLanguageManager.Instance.accountRefresher_skinFetchFailed.CurrentValue(), e);
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
            Password = loginAccount.Password
        };
    }
}

public record YggdrasilRefreshResult(
    IReadOnlyList<MinecraftAccount> Existing,
    IReadOnlyList<MinecraftAccount> Refreshed,
    IReadOnlyList<MinecraftAccount> Updated,
    IReadOnlyList<MinecraftAccount> Added,
    IReadOnlyList<MinecraftAccount> Removed);
