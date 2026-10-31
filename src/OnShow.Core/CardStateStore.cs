using System.Text.Json;

namespace OnShow.Core;

/// <summary>
/// 卡片运行状态仓库(state.json):已触发的提醒、上次快照、当日基线等非配置数据。
/// 各卡片按 cardId 隔离,互不污染。
/// </summary>
public sealed class CardStateStore
{
    readonly object _gate = new();
    Dictionary<string, JsonElement> _data;

    public CardStateStore()
        => _data = Json.Load<Dictionary<string, JsonElement>>(AppPaths.StateFile) ?? new();

    public T Get<T>(string cardId, T fallback)
    {
        lock (_gate)
        {
            if (_data.TryGetValue(cardId, out var el))
            {
                try { return el.Deserialize<T>(Json.Options) ?? fallback; }
                catch (Exception ex) { Log.Error($"解析卡片状态 {cardId} 失败", ex); }
            }
            return fallback;
        }
    }

    public void Set<T>(string cardId, T value)
    {
        lock (_gate) _data[cardId] = JsonSerializer.SerializeToElement(value, Json.Options)!;
    }

    public void Flush()
    {
        lock (_gate) Json.Save(AppPaths.StateFile, _data);
    }
}
