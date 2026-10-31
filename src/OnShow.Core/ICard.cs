namespace OnShow.Core.Cards;

/// <summary>卡片元数据。</summary>
public sealed record CardMetadata(string Id, string Name, string Icon);

/// <summary>宿主注入给卡片的能力集。卡片不得直接依赖 UI 或 Windows API。</summary>
public interface ICardHost
{
    AppConfig Config { get; }
    CardStateStore State { get; }

    /// <summary>系统级通知(托盘气泡 → Windows 通知中心)。</summary>
    void Notify(string title, string message);
}

/// <summary>
/// 卡片契约。生命周期:构造 → OnLoaded → (定时)RefreshAsync → Dispose。
/// ViewModelChanged 可能在后台线程触发,宿主负责编组到 UI 线程。
/// </summary>
public interface ICard : IDisposable
{
    CardMetadata Metadata { get; }

    void OnLoaded(ICardHost host);

    Task RefreshAsync(CancellationToken ct = default);

    /// <summary>UI 绑定的视图模型(通常为可变 INPC 对象,卡片原地更新以避免整表重绘)。</summary>
    object GetViewModel();

    event Action? ViewModelChanged;
}
