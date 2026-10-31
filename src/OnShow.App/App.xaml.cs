using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using OnShow.App.Interop;
using OnShow.App.Services;
using OnShow.App.Views;
using OnShow.Cards.Ddl;
using OnShow.Cards.Token;
using OnShow.Core;

namespace OnShow.App;

public partial class App : Application
{
    public static App Instance => (App)Current;

    public AppConfig Config { get; private set; } = new();
    public CardStateStore StateStore { get; private set; } = new();
    public DdlCard DdlCard { get; private set; } = null!;
    public TokenCard TokenCard { get; private set; } = null!;
    public WidgetWindow? Widget { get; private set; }
    internal TrayIconService? Tray { get; private set; }

    Mutex? _mutex;
    FlyoutWindow? _flyout;
    SettingsWindow? _settings;
    AboutWindow? _about;
    FeedbackWindow? _feedback;
    Services.CardHost? _host;
    ContextMenu? _trayMenu;
    bool _checkingUpdate;

    /// <summary>托盘右键菜单是否处于打开状态(hover 快览需让位)。</summary>
    public bool TrayMenuOpen => _trayMenu is { IsOpen: true };

    public void CloseTrayMenu()
    {
        try { if (_trayMenu is { IsOpen: true }) _trayMenu.IsOpen = false; }
        catch { /* 尽力而为 */ }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 更新安装器:新版本 exe 以 --apply-update <目标> 启动(此刻旧实例仍持有单实例互斥锁),
        // 必须在获取互斥锁之前接管,完成等待-覆盖-重启后直接退出。
        for (var i = 0; i < e.Args.Length - 1; i++)
        {
            if (e.Args[i].Equals("--apply-update", StringComparison.OrdinalIgnoreCase))
            {
                ApplyUpdateRunner.Run(e.Args[i + 1]);
                Shutdown(0);
                return;
            }
        }

        _mutex = new Mutex(true, @"Local\OnShow.SingleInstance", out var fresh);
        if (!fresh)
        {
            Shutdown(0);
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("UI 线程未处理异常", args.Exception);
            TryBalloon("OnShow 出现错误", args.Exception.Message);
            args.Handled = true;
        };
        System.AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error("未处理异常", args.ExceptionObject as Exception);

        AppPaths.EnsureAll();
        Log.Info($"OnShow 启动 args=[{string.Join(' ', e.Args)}]");

        Config = Json.Load<AppConfig>(AppPaths.ConfigFile);

        // 首次启动 / 协议文本版本升级:先取得同意,拒绝则直接退出
        if (Config.AgreementAcceptedVersion < AppMeta.AgreementVersion)
        {
            Log.Info("展示用户协议窗口");
            var eula = new EulaWindow();
            if (eula.ShowDialog() != true)
            {
                Log.Info("未同意用户协议,退出");
                Shutdown(0);
                return;
            }
            Config.AgreementAcceptedVersion = AppMeta.AgreementVersion;
            SaveConfig();
        }

        StateStore = new CardStateStore();
        _host = new Services.CardHost(this);

        DdlCard = new DdlCard();
        DdlCard.OnLoaded(_host);
        TokenCard = new TokenCard();
        TokenCard.OnLoaded(_host);

        // 以注册表实际状态为准(避免手动删过注册表后 UI 显示不一致)
        Config.AutoStart = AutoStartService.IsEnabled();

        Tray = new TrayIconService();
        Tray.LeftClicked += ToggleWidget;
        Tray.DoubleClicked += () => OpenSettings();
        Tray.MenuRequested += ShowTrayMenu;
        Tray.HoverStarted += ShowFlyout;

        Widget = new WidgetWindow(DdlCard, TokenCard, Config, OpenSettings);
        var minimized = e.Args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));
        Widget.StartHidden = minimized || !Config.Window.Visible;
        Widget.Show();          // SourceInitialized 期间应用模式/位置;StartHidden 则直接进托盘

        // 自测入口:--settings 启动即打开设置窗口(用于自动化冒烟验证)
        if (e.Args.Any(a => a.Equals("--settings", StringComparison.OrdinalIgnoreCase)))
            OpenSettings();

        MaybeAutoCheckUpdate();
    }

    // ---------- 更新 / 反馈 / 关于 ----------

    /// <summary>定时自动检查更新:24 小时节流,静默失败,命中「跳过此版本」不弹窗。</summary>
    void MaybeAutoCheckUpdate()
    {
        if (!Config.AutoCheckUpdate) return;
        if (DateTime.TryParse(Config.LastUpdateCheckUtc, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var last)
            && DateTime.UtcNow - last < TimeSpan.FromHours(24)) return;

        var skipped = Config.SkippedVersion;
        _ = Task.Run(async () =>
        {
            GitHubReleaseInfo? release = null;
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30));   // 启动后错峰,不与初始化抢网络
                release = await UpdateCheck.CheckLatestAsync(AppMeta.LatestReleaseApiUrl);
            }
            catch (Exception ex)
            {
                Log.Warn("后台检查更新失败(静默):" + ex.Message);
            }

            Config.LastUpdateCheckUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            _ = Dispatcher.BeginInvoke(SaveConfig);

            if (release is null || !UpdateHelper.IsNewer(release.TagName, AppMeta.CurrentVersion)) return;
            if (release.TagName.TrimStart('v', 'V') == skipped) return;

            _ = Dispatcher.BeginInvoke(() => new UpdateWindow(release).Show());
        });
    }

    /// <summary>手动检查更新(托盘 / 设置 / 关于入口),结果一律有反馈。</summary>
    public async void CheckForUpdateInteractive(Window? owner)
    {
        if (_checkingUpdate) return;
        _checkingUpdate = true;
        try
        {
            var release = await UpdateCheck.CheckLatestAsync(AppMeta.LatestReleaseApiUrl);
            Config.LastUpdateCheckUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            SaveConfig();
            if (release is null || !UpdateHelper.IsNewer(release.TagName, AppMeta.CurrentVersion))
            {
                Dialog.Info(owner, "检查更新", $"当前已是最新版本 v{AppMeta.CurrentVersionString}。", "success");
            }
            else
            {
                new UpdateWindow(release) { Owner = owner }.Show();
            }
        }
        catch (Exception ex)
        {
            Log.Error("检查更新失败", ex);
            Dialog.Warn(owner, "检查更新失败",
                "无法连接 GitHub:" + ex.Message +
                "\n\n提示:国内网络访问 GitHub 可能不稳定,请稍后重试,或到项目主页手动下载。");
        }
        finally
        {
            _checkingUpdate = false;
        }
    }

    public void OpenFeedback(Window? owner = null)
    {
        if (_feedback is { IsLoaded: true }) { _feedback.Activate(); return; }
        _feedback = new FeedbackWindow();
        if (owner is not null) _feedback.Owner = owner;
        _feedback.Show();
    }

    public void OpenAbout()
    {
        if (_about is { IsLoaded: true }) { _about.Activate(); return; }
        _about = new AboutWindow();
        _about.Show();
    }

    // ---------- Widget / 托盘动作 ----------

    public void ToggleWidget()
    {
        if (Widget is null) return;
        if (Widget.UserHidden || !Widget.IsVisible) Widget.ShowWidget();
        else Widget.HideToTray();
    }

    public void OpenSettings(string? page = null)
    {
        if (_settings is { IsLoaded: true })
        {
            if (page is not null) _settings.ShowPage(PageIndexOf(page));
            _settings.Activate();
            return;
        }
        _settings = new SettingsWindow();
        if (page is not null) _settings.InitialPage = PageIndexOf(page);
        _settings.Show();
        _settings.Activate();
    }

    static int PageIndexOf(string page) => page switch
    {
        "ddl" => 1,
        "token" => 2,
        "about" => 3,
        _ => 0,
    };

    void ShowTrayMenu()
    {
        if (TrayMenuOpen) { CloseTrayMenu(); return; }   // 再右键 = 关闭
        CloseTrayMenu();
        _flyout?.Dismiss();                              // 打开菜单先收起快览

        var menu = new ContextMenu();

        var toggle = new MenuItem { Header = (Widget?.UserHidden ?? true) ? "显示 Widget" : "隐藏 Widget" };
        toggle.Click += (_, _) => ToggleWidget();
        menu.Items.Add(toggle);
        menu.Items.Add(new Separator());

        var mDesktop = new MenuItem { Header = "桌面前置", IsCheckable = true, IsChecked = Config.Window.Mode != "AlwaysTop" };
        mDesktop.Click += (_, _) => SetMode("DesktopTop");
        var mAlways = new MenuItem { Header = "始终置顶", IsCheckable = true, IsChecked = Config.Window.Mode == "AlwaysTop" };
        mAlways.Click += (_, _) => SetMode("AlwaysTop");
        var mLock = new MenuItem { Header = "锁定位置", IsCheckable = true, IsChecked = Config.Window.Locked };
        mLock.Click += (_, _) => { Config.Window.Locked = mLock.IsChecked; SaveConfig(); };
        menu.Items.Add(mDesktop);
        menu.Items.Add(mAlways);
        menu.Items.Add(mLock);
        menu.Items.Add(new Separator());

        var mSettings = new MenuItem { Header = "设置…" };
        mSettings.Click += (_, _) => OpenSettings();
        menu.Items.Add(mSettings);

        menu.Items.Add(new Separator());

        var mUpdate = new MenuItem { Header = "检查更新…" };
        mUpdate.Click += (_, _) => CheckForUpdateInteractive(null);
        menu.Items.Add(mUpdate);

        var mFeedback = new MenuItem { Header = "反馈…" };
        mFeedback.Click += (_, _) => OpenFeedback();
        menu.Items.Add(mFeedback);

        var mAbout = new MenuItem { Header = "关于 OnShow…" };
        mAbout.Click += (_, _) => OpenAbout();
        menu.Items.Add(mAbout);

        var mAuto = new MenuItem { Header = "开机自启动", IsCheckable = true, IsChecked = Config.AutoStart };
        mAuto.Click += (_, _) => SetAutoStart(mAuto.IsChecked);
        menu.Items.Add(mAuto);
        menu.Items.Add(new Separator());

        var mExit = new MenuItem { Header = "退出" };
        mExit.Click += (_, _) => ExitApp();
        menu.Items.Add(mExit);

        menu.PlacementTarget = Widget;
        menu.Placement = PlacementMode.MousePoint;

        // 托盘图标属于 Explorer:菜单若不取得前台焦点,点击其他位置不会使其关闭。
        // 临时允许 Widget 激活并抢前台,菜单关闭后恢复 NOACTIVATE 与贴底。
        PinService.SetNoActivate(Widget, false);
        PinService.ForceForeground(Widget);
        _trayMenu = menu;
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_trayMenu, menu)) _trayMenu = null;
            if (Widget is not null)
            {
                PinService.SetNoActivate(Widget, true);
                if (Config.Window.Mode != "AlwaysTop") PinService.PushToBottom(Widget);
            }
        };
        menu.IsOpen = true;
    }

    public void SetMode(string mode)
    {
        Config.Window.Mode = mode;
        Widget?.ApplyModeChange();
        SaveConfig();
        Log.Info($"桌面模式 -> {mode}");
    }

    public void SetAutoStart(bool on)
    {
        try
        {
            AutoStartService.SetEnabled(on);
            Config.AutoStart = on;
        }
        catch (Exception ex)
        {
            Log.Error("设置开机自启动失败", ex);
            TryBalloon("OnShow", "开机自启动设置失败,详见日志");
        }
        SaveConfig();
    }

    void ShowFlyout()
    {
        if (!Config.Window.FlyoutEnabled || Tray is null || TrayMenuOpen) return;
        _flyout ??= new FlyoutWindow();
        _flyout.ShowNear(Tray.IconRectOrCursor());
    }

    public void SaveConfig() => Json.Save(AppPaths.ConfigFile, Config);

    void TryBalloon(string title, string message)
    {
        try { Tray?.ShowInfo(title, message); } catch { /* 尽力而为 */ }
    }

    public void ExitApp()
    {
        try
        {
            StateStore.Flush();
            SaveConfig();
            DdlCard?.Dispose();
            TokenCard?.Dispose();
            Tray?.Dispose();
            Widget?.ForceClose();
        }
        catch (Exception ex)
        {
            Log.Error("退出清理异常", ex);
        }
        finally
        {
            Shutdown(0);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SaveConfig();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
