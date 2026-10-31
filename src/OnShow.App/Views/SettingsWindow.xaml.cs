using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OnShow.App.Services;
using OnShow.Cards.Ddl;
using OnShow.Cards.Token;
using OnShow.Core;

namespace OnShow.App.Views;

/// <summary>设置窗口:改动即时生效,窗口关闭时统一落盘。</summary>
public partial class SettingsWindow : Window
{
    readonly AppConfig _config;

    /// <summary>外部跳转的初始页(0 通用 / 1 DDL / 2 Token / 3 关于)。</summary>
    public int InitialPage { get; set; }

    public SettingsWindow()
    {
        InitializeComponent();
        _config = App.Instance.Config;

        ModeBox.SelectedIndex = _config.Window.Mode == "AlwaysTop" ? 1 : 0;
        AutoStartBox.IsChecked = _config.AutoStart;
        LaunchMinBox.IsChecked = _config.LaunchMinimized;
        LockBox.IsChecked = _config.Window.Locked;
        FlyoutBox.IsChecked = _config.Window.FlyoutEnabled;
        OpacitySlider.Value = Math.Clamp(_config.Window.Opacity, 0.5, 1);
        OpacityValue.Text = $"{OpacitySlider.Value * 100:N0}%";
        SelectPoll(_config.TokenPollMinutes);

        NavVersion.Text = "v" + AppMeta.CurrentVersionString;
        AboutTitle.Text = $"OnShow v{AppMeta.CurrentVersionString}";
        AutoUpdateBox.IsChecked = _config.AutoCheckUpdate;
        MirrorBox.Text = _config.DownloadMirrorPrefix ?? "";
        BuildBgSwatches();

        RefreshDdlList();
        RefreshAccountList();
        ApplyPage(InitialPage);
        NavList.SelectedIndex = InitialPage;
        Closed += (_, _) => App.Instance.SaveConfig();
    }

    // ---------- 导航 ----------

    void OnNavChanged(object sender, SelectionChangedEventArgs e)
    {
        // XAML 解析期间 IsSelected="True" 会提前触发本事件,页面控件尚未初始化
        if (!IsLoaded) return;
        if (NavList.SelectedItem is ListBoxItem item)
            ApplyPage(NavList.ItemContainerGenerator.IndexFromContainer(item));
    }

    void ApplyPage(int idx)
    {
        PageGeneral.Visibility = idx == 0 ? Visibility.Visible : Visibility.Collapsed;
        PageDdl.Visibility = idx == 1 ? Visibility.Visible : Visibility.Collapsed;
        PageToken.Visibility = idx == 2 ? Visibility.Visible : Visibility.Collapsed;
        PageAbout.Visibility = idx == 3 ? Visibility.Visible : Visibility.Collapsed;
        if (idx == 1) RefreshDdlList();
        if (idx == 2) RefreshAccountList();
    }

    /// <summary>外部跳页(设置窗口已打开时)。</summary>
    public void ShowPage(int index)
    {
        if (index is < 0 or > 3) return;
        ApplyPage(index);
        NavList.SelectedIndex = index;
    }

    // ---------- 通用 ----------

    void OnModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_config is null || ModeBox is null || ModeBox.SelectedIndex < 0) return;
        _config.Window.Mode = ModeBox.SelectedIndex == 1 ? "AlwaysTop" : "DesktopTop";
        App.Instance.Widget?.ApplyModeChange();
    }

    void OnAutoStartChanged(object sender, RoutedEventArgs e)
    {
        if (_config is null || AutoStartBox.IsChecked is null) return;
        var want = AutoStartBox.IsChecked.Value;
        try
        {
            AutoStartService.SetEnabled(want);
            _config.AutoStart = want;
        }
        catch (Exception ex)
        {
            Log.Error("设置开机自启动失败", ex);
            AutoStartBox.IsChecked = !want;
            Dialog.Info(this, "OnShow", "开机自启动设置失败,详见日志", "error");
        }
    }

    void OnFlagChanged(object sender, RoutedEventArgs e)
    {
        if (_config is null) return;
        if (LaunchMinBox.IsChecked is { } lm) _config.LaunchMinimized = lm;
        if (LockBox.IsChecked is { } lk) _config.Window.Locked = lk;
        if (FlyoutBox.IsChecked is { } fl) _config.Window.FlyoutEnabled = fl;
    }

    void OnThumbChanged(object sender, RoutedEventArgs e)
    {
        // 任务栏悬停缩略图功能已移除;保留空处理器以防旧 XAML 残留引用
    }

    void OnOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // XAML 解析 Minimum/Maximum 时会触发 Value 强制重算 → 本事件;此刻 _config 尚未赋值
        if (_config is null || OpacitySlider is null) return;
        _config.Window.Opacity = e.NewValue;
        if (OpacityValue is not null)
            OpacityValue.Text = $"{e.NewValue * 100:N0}%";
        App.Instance.Widget?.ApplyOpacity();
    }

    void OnPollChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_config is null) return;
        if (PollBox.SelectedItem is not ComboBoxItem item || item.Tag is not string tag) return;
        if (!int.TryParse(tag, out var minutes)) return;
        _config.TokenPollMinutes = minutes;
        App.Instance.TokenCard?.Reschedule(minutes);
    }

    void SelectPoll(int minutes)
    {
        foreach (ComboBoxItem item in PollBox.Items)
            if (item.Tag is string t && int.TryParse(t, out var v) && v == minutes)
            { item.IsSelected = true; return; }
        PollBox.SelectedIndex = 1;
    }

    // ---------- DDL 管理 ----------

    void RefreshDdlList() => DdlManageList.ItemsSource = App.Instance.DdlCard.Items;

    DdlItemVm? SelectedDdl => DdlManageList.SelectedItem as DdlItemVm;

    void OnDdlAdd(object sender, RoutedEventArgs e)
    {
        new DdlEditWindow(App.Instance.DdlCard, null) { Owner = this }.ShowDialog();
        RefreshDdlList();
    }

    void OnDdlEdit(object sender, RoutedEventArgs e)
    {
        var vm = SelectedDdl;
        if (vm is null) return;
        var item = App.Instance.DdlCard.GetItem(vm.Id);
        if (item is null) return;
        new DdlEditWindow(App.Instance.DdlCard, item) { Owner = this }.ShowDialog();
        RefreshDdlList();
    }

    void OnDdlToggleDone(object sender, RoutedEventArgs e)
    {
        var vm = SelectedDdl;
        if (vm is null) return;
        App.Instance.DdlCard.SetDone(vm.Id, !vm.Done);
        RefreshDdlList();
    }

    void OnDdlDelete(object sender, RoutedEventArgs e)
    {
        var vm = SelectedDdl;
        if (vm is null) return;
        if (!Dialog.Confirm(this, "删除 DDL", $"确定删除「{vm.Title}」?")) return;
        App.Instance.DdlCard.Remove(vm.Id);
        RefreshDdlList();
    }

    // ---------- Token 账户 ----------

    void RefreshAccountList() => AccountManageList.ItemsSource = App.Instance.TokenCard.Rows;

    TokenRowVm? SelectedAccount => AccountManageList.SelectedItem as TokenRowVm;

    void OnAccountAdd(object sender, RoutedEventArgs e)
    {
        new AccountEditWindow(App.Instance.TokenCard, null) { Owner = this }.ShowDialog();
        RefreshAccountList();
    }

    void OnAccountEdit(object sender, RoutedEventArgs e)
    {
        var row = SelectedAccount;
        if (row is null) return;
        new AccountEditWindow(App.Instance.TokenCard, row.Account) { Owner = this }.ShowDialog();
        RefreshAccountList();
    }

    void OnAccountDelete(object sender, RoutedEventArgs e)
    {
        var row = SelectedAccount;
        if (row is null) return;
        if (!Dialog.Confirm(this, "删除账户", $"确定删除账户「{row.Name}」?")) return;
        App.Instance.TokenCard.Remove(row.Account.Id);
        RefreshAccountList();
    }

    void OnTokenRefresh(object sender, RoutedEventArgs e)
        => _ = App.Instance.TokenCard.RefreshAsync();

    // ---------- 关于 ----------

    readonly List<Border> _swatches = new();

    void BuildBgSwatches()
    {
        foreach (var preset in WidgetTheme.Presets)
        {
            var border = new Border
            {
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(13),
                Background = new SolidColorBrush(preset.Panel),
                BorderThickness = new Thickness(2),
                BorderBrush = (Brush)FindResource("BrushBorderStrong"),
                Margin = new Thickness(0, 0, 8, 4),
                Cursor = Cursors.Hand,
                ToolTip = preset.Name,
                Tag = preset.Key,
            };
            border.MouseLeftButtonUp += (_, _) =>
            {
                _config.WidgetBackground = preset.Key;
                UpdateSwatchSelection();
                App.Instance.Widget?.ApplyBackground();
                App.Instance.SaveConfig();
                Log.Info($"面板背景 -> {preset.Key}({preset.Name})");
            };
            _swatches.Add(border);
            BgSwatches.Children.Add(border);
        }
        UpdateSwatchSelection();
    }

    void UpdateSwatchSelection()
    {
        var accent = (Brush)FindResource("BrushAccent");
        var normal = (Brush)FindResource("BrushBorderStrong");
        foreach (var s in _swatches)
        {
            var selected = (string)s.Tag == _config.WidgetBackground;
            s.BorderBrush = selected ? accent : normal;
            s.Opacity = selected ? 1 : 0.75;
        }
    }

    // ---------- 列表拖拽排序 ----------

    Point _dragStart;
    object? _dragPayload;
    ListBox? _dragList;

    void OnListMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBox lb) return;
        _dragList = lb;
        _dragStart = e.GetPosition(lb);
        var container = ItemsControl.ContainerFromElement(lb, e.OriginalSource as DependencyObject) as ListBoxItem;
        _dragPayload = container?.DataContext;
    }

    void OnListMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragPayload is null || _dragList is null || e.LeftButton != MouseButtonState.Pressed) return;
        var pos = e.GetPosition(_dragList);
        if (Math.Abs(pos.X - _dragStart.X) < 6 && Math.Abs(pos.Y - _dragStart.Y) < 6) return;
        try { DragDrop.DoDragDrop(_dragList, _dragPayload, DragDropEffects.Move); }
        finally { _dragPayload = null; }
    }

    void OnListDrop(object sender, DragEventArgs e)
    {
        if (sender is not ListBox lb) return;
        var container = ItemsControl.ContainerFromElement(lb, e.OriginalSource as DependencyObject) as ListBoxItem;

        if (ReferenceEquals(lb, DdlManageList))
        {
            if (e.Data.GetData(typeof(DdlItemVm)) is not DdlItemVm drag) return;
            var ids = App.Instance.DdlCard.Items.Select(v => v.Id).ToList();
            var to = container is null ? ids.Count : ids.IndexOf(((DdlItemVm)container.DataContext).Id);
            ids.Remove(drag.Id);
            to = Math.Clamp(to, 0, ids.Count);
            ids.Insert(to, drag.Id);
            App.Instance.DdlCard.Reorder(ids);
            RefreshDdlList();
        }
        else if (ReferenceEquals(lb, AccountManageList))
        {
            if (e.Data.GetData(typeof(TokenRowVm)) is not TokenRowVm drag) return;
            var ids = App.Instance.TokenCard.Rows.Select(v => v.Account.Id).ToList();
            var to = container is null ? ids.Count : ids.IndexOf(((TokenRowVm)container.DataContext).Account.Id);
            ids.Remove(drag.Account.Id);
            to = Math.Clamp(to, 0, ids.Count);
            ids.Insert(to, drag.Account.Id);
            App.Instance.TokenCard.Reorder(ids);
            RefreshAccountList();
        }
    }

    void OnAutoUpdateChanged(object sender, RoutedEventArgs e)
    {
        if (_config is null || AutoUpdateBox.IsChecked is null) return;
        _config.AutoCheckUpdate = AutoUpdateBox.IsChecked.Value;
    }

    void OnMirrorChanged(object sender, RoutedEventArgs e)
    {
        if (_config is null || MirrorBox is null) return;
        var text = MirrorBox.Text.Trim();
        _config.DownloadMirrorPrefix = text.Length == 0 ? null : text;
    }

    void OnCheckUpdate(object sender, RoutedEventArgs e) => App.Instance.CheckForUpdateInteractive(this);

    void OnFeedback(object sender, RoutedEventArgs e) => App.Instance.OpenFeedback(this);

    void OnOpenAbout(object sender, RoutedEventArgs e) => App.Instance.OpenAbout();

    void OnOpenDataFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = AppPaths.Root,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Log.Error("打开数据目录失败", ex);
        }
    }
}
