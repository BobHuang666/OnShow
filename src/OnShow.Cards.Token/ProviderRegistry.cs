namespace OnShow.Cards.Token;

/// <summary>内置 Provider 注册表(未来外部 DLL 卡片可扩展此机制)。</summary>
public static class ProviderRegistry
{
    public static IReadOnlyList<ITokenProvider> All { get; } = new ITokenProvider[]
    {
        new DeepSeekProvider(),
        new ZhipuProvider(),
        new ZaiProvider(),
        new XunfeiProvider(),
        new ManualProvider(),
    };

    public static ITokenProvider? Get(string id) => All.FirstOrDefault(p => p.Id == id);
}
