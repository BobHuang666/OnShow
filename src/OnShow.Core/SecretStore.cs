using System.Security.Cryptography;
using System.Text.Json;

namespace OnShow.Core;

/// <summary>
/// 密钥安全存储:DPAPI(当前用户)加密后落盘 tokens.dat。
/// 结构:{ accountId: { credentialKey: value } };UI 永不回显明文,导出配置天然脱敏。
/// </summary>
public static class SecretStore
{
    static readonly byte[] Entropy = "OnShow.SecureStore.v1"u8.ToArray();

    public static Dictionary<string, Dictionary<string, string>> LoadAll()
    {
        try
        {
            if (!File.Exists(AppPaths.SecretFile)) return new();
            var protectedBytes = File.ReadAllBytes(AppPaths.SecretFile);
            var json = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(json) ?? new();
        }
        catch (Exception ex)
        {
            Log.Error("读取密钥存储失败", ex);
            return new();
        }
    }

    public static void SaveAll(Dictionary<string, Dictionary<string, string>> secrets)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(secrets);
        var data = ProtectedData.Protect(json, Entropy, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(AppPaths.SecretFile, data);
    }

    public static IReadOnlyDictionary<string, string> Get(string accountId)
        => LoadAll().TryGetValue(accountId, out var d) ? d : new Dictionary<string, string>();

    /// <summary>写入某账户的凭据;values 为空则整体移除。</summary>
    public static void Set(string accountId, Dictionary<string, string> values)
    {
        var all = LoadAll();
        if (values.Count == 0) all.Remove(accountId);
        else all[accountId] = values;
        SaveAll(all);
    }

    public static void Remove(string accountId)
    {
        var all = LoadAll();
        if (all.Remove(accountId)) SaveAll(all);
    }
}
