# OnShow 设计文档

> Windows 桌面常驻信息小部件 · v0.3 · 2026-10-31
> 定位:固定在桌面的个人信息 HUD,首期承载「DDL 倒计时」与「Token 用量监控」两个卡片,卡片框架为后续扩展预留。

---

## 1. 产品定义

**OnShow** = 常驻桌面的卡片式信息面板,核心体验三件事:

| # | 需求 | 一句话定义 |
|---|------|-----------|
| 1 | 固定在桌面显示 | 开机即出现在桌面,像桌面的一部分;不抢焦点、不打断操作 |
| 2 | 任务栏 hover 显示 | 鼠标悬停任务栏图标,直接看「最紧急 DDL + Token 总览」迷你仪表 |
| 3 | 开机自启动 | 登录 Windows 后自动拉起,支持延迟启动 |

内容层首批两个卡片,未来按同一框架扩展(天气、汇率、番茄钟、系统监控、日程、股票……):

- **DDL 倒计时卡**:多条截止日期,按紧急度排序、临期染色、系统通知提醒
- **Token 监控卡**:聚合多个 AI 服务/中转站的额度与用量,进度条 + 汇总,超阈值变色

## 2. 技术选型

**技术栈(已确认):WPF + .NET 8(LTS)+ Win32 P/Invoke**,理由:

1. 本应用核心难点全部是 Windows 原生行为——桌面层嵌入(Progman/WorkerW)、任务栏自定义缩略图(DWM)、注册表自启动、托盘。C# 对这些有几十年沉淀的成熟方案,出问题好查。
2. XAML 数据绑定天然契合「卡片 → ViewModel → UI」的插件结构。
3. 发布为单文件 exe,无常驻浏览器内核;空闲内存 60–100MB,是 Electron 的 1/3。
4. 你在 Windows 上,后续自己扩展卡片时,C# + XAML 的资料密度最高。

备选对比(决策依据记录):

| 方案 | UI 效率 | 原生集成 | 体积/内存 | 结论 |
|------|--------|---------|----------|------|
| **WPF (.NET 8)** ✅ | 中(XAML) | 最好 | 5MB(装运行时)/ 60–100MB | 主选 |
| Tauri 2 | 高(HTML/CSS) | 需手写 Rust 原生模块,任务栏缩略图方案绕 | ~10MB / ~80MB | 备选 |
| Electron | 最高 | 需原生 addon | 150MB+ / 200MB+ | 不推荐,太重 |

## 3. 总体架构

单进程常驻:Widget 窗口(桌面卡片区)+ 托盘 + 按需弹出的设置/快览窗口。

```
┌────────────────────────────────────────────────────┐
│ UI 宿主层 (WPF)                                     │
│  WidgetWindow   FlyoutWindow   SettingsWindow   Tray│
├────────────────────────────────────────────────────┤
│ 卡片框架 (CardHost)                                  │
│  ICard 生命周期: OnLoaded → Refresh → Dispose        │
│   ├── DdlCard(内置)      ├── TokenCard(内置)        │
│   └── (未来卡片,编译内置或 DLL)                       │
├────────────────────────────────────────────────────┤
│ 服务层                                              │
│  PinService(桌面固定)  QuickGlance(任务栏hover/托盘) │
│  AutoStartService   ConfigService   ToastService    │
│  SecureStore(DPAPI)  HttpFactory(重试/退避)          │
├────────────────────────────────────────────────────┤
│ 数据层  %APPDATA%\OnShow\                           │
│  config.json  ddl.json  token.json  tokens.dat(密钥)│
└────────────────────────────────────────────────────┘
```

原则:**Core(卡片框架+服务)不引用 UI**,可独立单元测试;卡片通过接口访问宿主能力,不直接碰 Windows API。

## 4. 关键技术方案

### 4.1 桌面固定(PinService)——两种模式可切换

| 模式 | 原理 | 效果 | 代价 |
|------|------|------|------|
| **DesktopTop**(默认) | 无边框窗口 + `WS_EX_TOOLWINDOW`(不进 Alt-Tab),z 序保持在桌面之上、其他窗口之下;捕获「显示桌面」后自动还原 | 图标之上、应用之下,Win+D 不消失 | 需自行维护 z 序 |
| **AlwaysTop** | `WS_EX_TOPMOST` | 置顶画中画,任何全屏应用上都可见 | 可能挡住工作区 |

- 两种模式都保留任务栏按钮与独立窗口形态,任务栏 hover 缩略图(4.2)在两种模式下均生效。
- 通用细节:`WS_EX_NOACTIVATE` + `ShowActivated=false` 不抢焦点;PerMonitorV2 DPI;位置按显示器记忆、贴边吸附;设置里提供「锁定位置」防误拖(锁定后拖动需先在托盘菜单解锁)。

### 4.2 任务栏 hover 显示(QuickGlance 服务)

两个宿主渲染同一个 `GlanceModel`,互补覆盖:

| 宿主 | 机制 | 适用 |
|------|------|------|
| **任务栏自定义缩略图** | `DWMWA_HAS_ICONIC_BITMAP` + `DWMWA_FORCE_ICONIC_REPRESENTATION` 置 TRUE,处理 `WM_DWMSENDICONICTHUMBNAIL`,按系统请求尺寸渲染迷你仪表 → `DwmSetIconicThumbnail` 回传;Live Preview 同理 | DesktopTop / AlwaysTop 模式(有任务栏按钮) |
| **托盘 hover flyout** | NotifyIcon `MouseEnter` 延迟 300ms 弹出无边框小面板(紧急 DDL + Token 汇总),`MouseLeave` 收回 | 所有模式,任务栏之外的第二个 hover 入口 |

```csharp
// 两种宿主共享的渲染模型
record GlanceModel(
    string? NextDdlTitle, TimeSpan? NextDdlRemain, string NextDdlColor,
    decimal TokenUsedPct, string TokenSummary, DateTime RefreshedAt);
```

> 实现期验证点:Win11(23H2)任务栏对 iconic thumbnail 支持正常,但 hover 弹出延迟/尺寸由系统决定;若体验不达标,flyout 承担主通道,缩略图退化为显示 widget 本体截图。

### 4.3 开机自启动(AutoStartService)

- 默认:**注册表 HKCU\Software\Microsoft\Windows\CurrentVersion\Run** 写入 `"OnShow.exe" --minimized`(用户级、免管理员、卸载即清)。
- 高级选项:计划任务 `schtasks /sc onlogon /delay 0000:10` 延迟 10 秒启动,避开开机 IO 高峰。
- `--minimized`:启动进托盘,Widget 是否显示遵循上次退出时的状态。
- 设置页开关,实时回显注册表状态;配置写入经 ConfigService 统一持久化。

### 4.4 托盘与单实例

- 托盘:左键切换 Widget 显隐;右键菜单(显示/隐藏、前置/置顶切换、锁定/解锁位置、设置、开机自启、退出)。
- 互斥体单实例:二次启动唤起已有实例。

## 5. 卡片框架(扩展性核心)

```csharp
public interface ICard
{
    CardMetadata Metadata { get; }               // id / 名称 / 图标 / 默认尺寸 / 版本
    void OnLoaded(ICardHost host);               // host 注入: SettingStore / IToast / Http / 时钟
    Task RefreshAsync(CancellationToken ct);     // 数据类卡片的主动刷新
    object GetViewModel();                       // 只读模型,UI 绑定
    event Action ViewModelChanged;               // 通知宿主重绘
    void Dispose();
}
```

- 卡片自身**不碰 Windows API、不碰网络细节**,通过 `ICardHost` 拿能力,保证未来卡片(自己写或别人 DLL)行为一致。
- v1 两个内置卡片编译进主程序;框架按「卡片 = 独立程序集」的边界设计,后续放开 `%APPDATA%\OnShow\cards\*.dll` 即成插件目录(远期再考虑 JS 脚本卡)。
- v1 设置页由宿主针对两张内建卡直接实现;声明式设置 Schema(自动生成 UI)留待 v2 卡片插件化时引入。
- 布局 v1:右下角锚定 + 垂直流式堆叠(可折叠单卡);自由拖拽网格留到 v2。
- 每卡自带刷新间隔声明,宿主统一调度定时器(合并唤醒、灭屏/全屏时暂停轮询)。

## 6. DDL 倒计时卡

**数据模型**(`%APPDATA%\OnShow\ddl.json`):

```json
{ "id": "uuid", "title": "毕设答辩", "due": "2026-06-08T14:00:00+08:00",
  "priority": 2, "tags": ["学校"], "done": false,
  "remindBefore": ["3d", "24h", "1h"], "sort": 0 }
```

**渲染规则**:

| 剩余时间 | 显示 | 颜色 |
|---------|------|------|
| > 7 天 | `xx 天` | 常规 |
| ≤ 7 天 | `x 天 h 时` | 橙 |
| ≤ 24 h | `hh:mm:ss` 实时翻秒 | 红 |
| 已过期 | `+x 天` | 灰 + 删除线 |

- 排序:按 due 升序,Widget 内默认显示前 5 条(可配),悬浮展开全部。
- 秒级定时器**仅当存在 ≤24h 条目时才开启**,其余分钟级刷新,控功耗。
- 提醒:临期阈值逐条触发 Windows Toast(系统通知中心,不弹抢焦点对话框);已完成/过期项 Toast 静默。
- 交互:卡片上 `+` 快速新增(小输入面板:标题 + 日期时间);右键进入管理列表(编辑/完成/删除);点击条目展开备注。

## 7. Token 监控卡(插件式 Provider)

统一抽象,每家数据源一个适配器,失败可见不静默(显示上次快照 + 刷新时间):

```csharp
public interface ITokenProvider
{
    string Id { get; }                                  // openai-compat / deepseek / zhipu ...
    Task<TokenSnapshot> FetchAsync(TokenAccount acct, CancellationToken ct);
}
public record TokenAccount(string Id, string Name, string ProviderId,
                           string BaseUrl, IReadOnlyDictionary<string, string> Credentials,
                           decimal? ManualLimit);   // 凭据按 provider 自定:ApiKey / AccessKey 对 / 会话凭据
public record TokenSnapshot(string AccountId, SnapshotKind Kind, decimal? UsedAmount, decimal? LimitAmount,
                            decimal? UsedTokens, string Currency, DateTime FetchedAt);
public enum SnapshotKind { Balance, Quota, UsageTokens }   // 余额型 / 配额型 / 用量型
```

**v1 适配器清单**(首批:DeepSeek、智谱 GLM、讯飞 + Manual 兜底;快照类型指 3 种 `SnapshotKind`):

| Provider | 接口 | 快照类型 | 关键字段 | 鉴权 |
|----------|------|---------|---------|------|
| DeepSeek | `GET https://api.deepseek.com/user/balance`(官方) | 余额型 | `balance_infos[].total_balance / granted_balance / topped_up_balance`,`is_available` | Bearer API Key |
| 智谱 GLM | `GET https://open.bigmodel.cn/api/monitor/usage`(用量查询) | 用量型 | 分时段/分模型用量;现金余额无 API(仅财务中心) | API Key |
| 讯飞(星火) | `GET /v3/stat/usage`(大模型 Token 用量查询,[官方文档](https://www.xfyun.cn/doc/spark/usage_query.html)) | 用量型 | 天/小时粒度、分模型 token 用量 | OAuth2.0 API Key |
| Manual(内置兜底) | 无 | 配额型 | 手动录入 used / limit,本地累计 | — |
| 本地 CLI 用量(远期) | 读本地会话记录(Claude Code 等) | 用量型 | tokens / 成本,离线统计 | — |

- **轮询**:默认 10 分钟(5–60 可配);指数退避 + 10s 超时;每账户独立节流。
- **展示**(按快照类型分别渲染):
  - 配额型 → 名称 + 进度条 + `已用/额度`;
  - 余额型 → `余额 ¥xx` + 今日消耗(相邻快照差值本地累计,容忍重启);
  - 用量型 → 今日 / 本月 token 数。
  - 阈值染色统一:用量 ≥80% 橙、≥95% 红(余额型按剩余 <20% / <5% 触发);悬浮显示明细与上次刷新时间;顶部聚合行汇总今日消耗。
- **安全**:全部凭据经 **DPAPI(ProtectedData)** 加密后存 `tokens.dat`,UI 永不回显明文;导出配置时不含密钥。

> 接口参考:[DeepSeek 余额](https://api-docs.deepseek.com/zh-cn/api/get-user-balance) · [讯飞用量查询](https://www.xfyun.cn/doc/spark/usage_query.html) · [智谱开放平台](https://open.bigmodel.cn)

## 8. 配置与数据

```
%APPDATA%\OnShow\
├─ config.json      # 窗口模式/位置/尺寸/透明度/主题/自启动/卡片启停排序
├─ ddl.json         # DDL 条目
├─ token.json       # 账户配置(不含密钥)
├─ tokens.dat       # DPAPI 加密的密钥
└─ logs\            # 滚动日志,保留 7 天
```

- `config.json` 草案:窗口 `{ mode, monitor, anchor:"bottomRight", offset, size, opacity, locked }`、`autostart { enabled, delay }`、`cards: [{ id, enabled, order, ... }]`。
- 文件 watcher 热生效;全局异常捕获 + 崩溃后重启恢复窗口状态。
- 主题:深色默认,跟随系统;Win11 用 Mica/Acrylic 质感,Win10 退化为半透明纯色。

## 9. 非功能指标

| 项 | 目标 |
|----|------|
| 空闲 CPU | < 0.5%(定时器合并、无后台线程空转) |
| 常驻内存 | 60–100MB |
| 冷启动 | < 1.5s |
| 网络 | 统一 HttpClient + Polly;离线时全部卡片降级显示缓存快照 |
| 打包 | 单文件 exe(框架依赖,安装器自动装 .NET 8 Desktop Runtime)或自包含 ~130MB,Inno Setup 出安装包 |

## 10. 里程碑

| 阶段 | 内容 | 验收 |
|------|------|------|
| **M1 壳** | Widget 窗口 + 桌面三模式 + 托盘 + 自启动 + 配置持久化 + 单实例 | 重启电脑后 widget 自动出现在原位,不抢焦点 |
| **M2 DDL 卡** | CRUD + 倒计时渲染 + 染色 + Toast + 快速新增 | 添加「明天 18:00」条目,变红翻秒、到期收通知 |
| **M3 QuickGlance** | DWM 任务栏缩略图 + 托盘 hover flyout | 悬停任务栏看到迷你仪表;托盘 hover 弹出快览面板 |
| **M4 Token 卡** | Provider 框架 + DeepSeek/智谱/讯飞适配 + Manual 兜底 + DPAPI 存储 | 真实数据随轮询刷新,断网显示缓存快照 |
| **M5 打磨** | 多屏/DPI、主题、全屏暂停轮询、安装包 | 发布 v1.0 安装包 |

M1–M3 是一体的「壳体验」,M2/M4 相互独立可并行。

## 11. 风险与对策

| 风险 | 对策 |
|------|------|
| 智谱现金余额无公开 API(仅控制台财务中心) | 按用量型展示;余额以 Manual 兜底 |
| 各家接口形态不一(余额/配额/用量) | `SnapshotKind` 三型抽象,渲染层分别适配;失败明确显示错误与时间戳 |
| Win11 任务栏对 DWM 缩略图的行为差异 | M3 首日先做技术验证 spike,不达标则以 flyout 为主通道 |
| 密钥泄露 | DPAPI 加密、不回显、导出脱敏 |

## 12. 决策记录

| 日期 | 决策 |
|------|------|
| 2026-10-31 | 技术栈:WPF + .NET 8 |
| 2026-10-31 | 桌面固定只做 DesktopTop(默认)与 AlwaysTop 两档,不做 WorkerW 桌面嵌入 |
| 2026-10-31 | Token 数据源首批:DeepSeek、智谱 GLM、WorkBuddy、阿里云百炼、讯飞;Manual 兜底保留 |
| 2026-10-31 | 收敛:放弃阿里云百炼与 WorkBuddy,首批 = DeepSeek、智谱 GLM、讯飞 + Manual 兜底 |

**下一步**:搭建 M1 骨架 —— 解决方案结构、Widget 窗口、DesktopTop/AlwaysTop 切换、托盘、开机自启动、配置持久化。
