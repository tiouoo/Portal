using System;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace Portal.Core.Minecraft.Account;

public class OfflineAccount() : AccountBase(AccountType.Offline), IEquatable<OfflineAccount>
{
    public string Name { get; set; } = "OfflinePlayer";
    public Guid? Uuid { get; init; }

    public static Guid GetMinecraftOfflineUuid(string name)
    {
        if (string.IsNullOrEmpty(name)) return Guid.Empty;
        var bytes = Encoding.UTF8.GetBytes($"OfflinePlayer:{name}");
        var hash = MD5.HashData(bytes);
        hash[6] = (byte)((hash[6] & 0x0f) | 0x30);
        hash[8] = (byte)((hash[8] & 0x3f) | 0x80);
        var guidBytes = new byte[16];
        Array.Copy(hash, guidBytes, 16);
        Array.Reverse(guidBytes, 0, 4);
        Array.Reverse(guidBytes, 4, 2);
        Array.Reverse(guidBytes, 6, 2);
        return new Guid(guidBytes);
    }

    public bool Equals(OfflineAccount? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Uuid != Guid.Empty && Uuid.Equals(other.Uuid);
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as OfflineAccount);
    }

    public override int GetHashCode()
    {
        return Uuid.GetHashCode();
    }

    public static bool operator ==(OfflineAccount? left, OfflineAccount? right)
    {
        if (left is null) return right is null;
        return left.Equals(right);
    }

    public static bool operator !=(OfflineAccount? left, OfflineAccount? right)
    {
        return !(left == right);
    }
}