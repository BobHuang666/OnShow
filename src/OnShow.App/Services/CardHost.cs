using OnShow.Core;
using OnShow.Core.Cards;

namespace OnShow.App.Services;

/// <summary>ICardHost 实现:向卡片注入配置、状态仓库与系统通知能力。</summary>
sealed class CardHost : ICardHost
{
    readonly App _app;

    public CardHost(App app) => _app = app;

    public AppConfig Config => _app.Config;
    public CardStateStore State => _app.StateStore;

    public void Notify(string title, string message) => _app.Tray?.ShowInfo(title, message);
}
