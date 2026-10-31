# OnShow

Windows 桌面常驻信息小部件:**DDL 倒计时 + Token 用量监控**,卡片式框架可持续扩展。

![技术栈](https://img.shields.io/badge/WPF-.NET%208-blue) ![平台](https://img.shields.io/badge/Windows-10%20%2F%2011-lightgrey) ![Release](https://img.shields.io/github/v/release/BobHuang666/OnShow) ![License](https://img.shields.io/github/license/BobHuang666/OnShow)

## 下载与安装

1. 到 [Releases](https://github.com/BobHuang666/OnShow/releases/latest) 下载 `OnShow-vX.Y.Z-win-x64.zip`(自包含单文件,免装 .NET 运行时)
2. 解压出 `OnShow.exe`,放到**任意非系统盘目录**(如 `D:\Tools\OnShow\`),双击运行
   - 首次运行如遇 Windows SmartScreen 蓝色提示(程序未做代码签名):点击 **更多信息 → 仍要运行**
   - 可用 zip 同目录 `checksums.txt` 核对文件 SHA256
3. 首次启动会弹出用户协议,同意后进入;程序驻留托盘(托盘 O 图标 → 左键显示/隐藏 Widget,双击开设置)

**自动更新**:自 v1.1.0 起,应用默认每 24 小时静默检查一次新版本(设置 → 关于 中可手动「检查更新」),发现新版本后弹窗展示更新日志,一键下载校验并自动重启替换,无需重新到 GitHub 下载。v1.0.0 用户升级请手动下载新版覆盖。

## 功能

| 功能 | 说明 |
|------|------|
| 桌面固定 | `桌面前置`(默认:图标之上、应用之下,Win+D 后自动还原)/ `始终置顶` 两档,托盘菜单即切 |
| 不抢焦点 | 全程 `WS_EX_NOACTIVATE`,点击面板不打断当前工作;空白区拖动换位(可锁定),位置按显示器记忆 |
| DDL 倒计时 | 多条按截止时间排序;>7 天常规 / ≤7 天橙 / ≤24h 红色秒级倒计时 / 过期置灰;临期气泡提醒(3 天 / 24 小时 / 1 小时可选,每阈值只提醒一次) |
| Token 监控 | DeepSeek 余额、Z.ai / 智谱 GLM Coding Plan 用量百分比、讯飞星火 MaaS 免费额度、手动录入;大数 M/B 格式(如 `14.44M / 1.00B`);≥80% 橙、≥95% 红;断网显示上次快照 |
| 控制台跳转 | 点击面板上的 Token 行,直接打开对应服务商的额度/费用网页(账户可自定义网址) |
| 开机自启动 | HKCU 注册表 Run 键 + `--minimized` 参数(仅托盘启动);设置页一键开关 |
| 安全 | API Key / Token / Cookie 经 **DPAPI** 加密存储于 `tokens.dat`,配置导出天然脱敏 |
| 主题化 UI | 深色卡片式设置窗口、编辑窗、确认对话框、托盘菜单;应用图标(任务栏 / 资源管理器 / 托盘) |

## 快速开始

```powershell
dotnet build OnShow.sln -c Release

# 直接运行
src\OnShow.App\bin\Release\net8.0-windows\OnShow.exe

# 或发布单文件
.\publish.ps1              # 框架依赖(需目标机装有 .NET 8 Desktop Runtime)
.\publish.ps1 -SelfContained   # 自包含(免运行时,~130MB)
```

首次运行:托盘出现 **O** 图标 → 左键显示 / 隐藏 Widget → 双击或右键「设置」。

## 使用入口

| 操作 | 入口 |
|------|------|
| 添加 / 编辑 / 删除 DDL | Widget 右上角 `＋`;点击条目;设置 → DDL 管理 |
| 配置 Token 账户 | 设置 → Token 账户 → 新建 / 编辑(可「测试连接」) |
| 打开服务商额度/费用网页 | 点击 Widget 上的 Token 行 |
| 切换桌面模式 / 锁定位置 | 托盘右键菜单 |
| 刷新 Token | Widget 右上角 `⟳` |
| 托盘悬停快览 | 悬停托盘图标(设置中可关) |
| 检查更新 / 更新日志 | 托盘右键菜单,或 设置 → 关于 |
| 问题反馈 | 托盘右键菜单 →「反馈」,或 [GitHub Issues](https://github.com/BobHuang666/OnShow/issues) |

## Token Provider 配置

| 服务商 | 凭据 | 获取位置 | 说明 |
|--------|------|---------|------|
| DeepSeek | API Key | [platform.deepseek.com](https://platform.deepseek.com) | 官方余额接口(总额 / 赠金 / 充值),最稳定 |
| Z.ai (GLM Coding Plan) | Coding Plan Token | 配置 ZCode / Claude Code 时使用的 `ANTHROPIC_AUTH_TOKEN` | 官方插件同源接口,显示 5 小时窗口用量百分比;**普通 API Key 无套餐会报"当前用户不存在coding plan"** |
| 智谱 GLM | API Key | [open.bigmodel.cn](https://open.bigmodel.cn) → API Keys | 与 Z.ai 为两套账号体系;需国内 Coding Plan 套餐,按量付费 key 查不到套餐 |
| 讯飞星火 MaaS | Cookie | [maas.xfyun.cn](https://maas.xfyun.cn) 用量页 F12 → 复制整行 Cookie | 控制台会话鉴权,显示免费额度 `已用/总量` + 到期倒计时;`tenantToken` 约一天过期,过期后重新复制 Cookie 即可 |
| 手动录入 | 无 | — | 手动维护 已用 / 额度,兜底 |

所有 Provider 均支持在账户里填写「控制台网址」覆盖默认跳转地址。

## 数据位置

```
%APPDATA%\OnShow\
├─ config.json   全局配置(桌面模式/位置/自启动/轮询间隔)
├─ ddl.json      DDL 条目
├─ token.json    Token 账户(不含凭据)
├─ tokens.dat    DPAPI 加密的凭据
├─ state.json    运行状态(已提醒阈值/上次快照/当日基线)
└─ logs\         滚动日志(保留 7 天)
```

## 代码结构

```
src/
├─ OnShow.Core/        卡片框架(ICard/ICardHost)、配置、日志、DPAPI、自启动
├─ OnShow.Cards.Ddl/   DDL 卡片(数据、视图模型、提醒引擎)
├─ OnShow.Cards.Token/ Token 卡片 + Provider(DeepSeek/Z.ai/智谱/讯飞/Manual)+ HTTP 栈
└─ OnShow.App/         WPF 宿主:Widget 窗口、托盘、快览、设置、对话框、主题
tests/OnShow.Core.Tests/   单元测试(27 项)
tools/
├─ HttpProbe           与应用同一 HTTP 栈的连通性探测
└─ gen-ico.ps1         重新生成应用图标(多尺寸 ico)
```

新增卡片 = 新建类库实现 `ICard`(`OnLoaded → RefreshAsync → ViewModelChanged → Dispose`),宿主注册即可;能力(配置 / 状态 / 通知)经 `ICardHost` 注入,卡片不接触 Windows API。新增 Token 数据源 = 实现 `ITokenProvider` 并注册到 `ProviderRegistry`。

## 已知事项

- Z.ai / 智谱 Coding Plan 的用量按 **5 小时滚动窗口**计算,面板显示为百分比进度条。
- 讯飞 MaaS 接口为控制台会话鉴权,Cookie 过期后面板提示重新复制;无免费额度套餐的账号不适用。
- 任务栏悬停显示系统原生窗口预览。
- 「桌面前置」模式下窗口会出现在 Alt-Tab 列表中(保留任务栏按钮的代价)。

## 设计文档

完整设计(架构、里程碑、决策记录)见 [DESIGN.md](DESIGN.md)。

## 反馈

- **应用内反馈**(推荐):托盘右键菜单 →「反馈」,支持文字 + 截图,自动附带版本/系统信息,一键跳转 GitHub Issue(截图已放入剪贴板,Ctrl+V 即可粘贴)
- **GitHub Issues**:直接到 [Issues](https://github.com/BobHuang666/OnShow/issues) 新建,建议附上 `%APPDATA%\OnShow\logs` 中的相关日志片段
- **邮件**(无 GitHub 账号时):bobhuang666@foxmail.com

⚠️ 反馈时请注意脱敏:不要粘贴 `tokens.dat`,日志片段中如含 Cookie / 账户信息请先行删除。

## 用户协议与隐私

首次启动需同意[用户协议](src/OnShow.App/Assets/Docs/UserAgreement.md)(应用内「关于」窗口可随时查看全文)。核心承诺:

- 所有配置与凭据只保存在本机 `%APPDATA%\OnShow`,凭据经 DPAPI(当前用户)加密
- 程序仅在用户配置账户后,向 DeepSeek / Z.ai / 智谱 / 讯飞 **官方接口**发起只读额度查询,不经任何中转服务器
- 反馈仅在用户主动提交时上传,内容限于所填文字、所选截图与版本信息,永不上传密钥与配置文件

## 许可证

[MIT](LICENSE)
