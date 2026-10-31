namespace OnShow.Cards.Ddl;

/// <summary>DDL 条目(ddl.json 持久化模型)。</summary>
public class DdlItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public DateTime Due { get; set; }
    public bool Done { get; set; }

    /// <summary>提醒阈值(到期前):30m / 1h / 3h / 24h / 3d / 7d。</summary>
    public string[] RemindBefore { get; set; } = { "24h", "1h" };

    public string Note { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>手动排序号(设置页拖拽调整);0 表示未初始化,按原默认排序迁移。</summary>
    public int Order { get; set; }
}
