using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using OnShow.Cards.Token;
using OnShow.Core;

namespace OnShow.App.Views;

/// <summary>Token 账户新增 / 编辑:凭据经 DPAPI 存储,UI 不回显明文。</summary>
public partial class AccountEditWindow : Window
{
    readonly TokenCard _card;
    readonly TokenAccount? _editing;
    readonly List<(CredField Field, Control Box)> _credInputs = new();

    public AccountEditWindow(TokenCard card, TokenAccount? editing)
    {
        InitializeComponent();
        _card = card;
        _editing = editing;

        ProviderBox.ItemsSource = ProviderRegistry.All;

        if (editing is not null)
        {
            Title = "编辑 Token 账户";
            HeaderText.Text = "编辑 Token 账户";
            NameBox.Text = editing.Name;
            BaseUrlBox.Text = editing.BaseUrl;
            ConsoleUrlBox.Text = editing.ConsoleUrl;
            UsedBox.Text = editing.ManualUsed?.ToString(CultureInfo.InvariantCulture) ?? "";
            LimitBox.Text = editing.ManualLimit?.ToString(CultureInfo.InvariantCulture) ?? "";
            ProviderBox.SelectedItem = ProviderRegistry.Get(editing.ProviderId);
            var saved = SecretStore.Get(editing.Id);
            foreach (var kv in saved) _editingCreds[kv.Key] = kv.Value;
        }
        else
        {
            Title = "添加 Token 账户";
            HeaderText.Text = "添加 Token 账户";
            ProviderBox.SelectedIndex = 0;
        }

        Loaded += (_, _) => NameBox.Focus();
    }

    readonly Dictionary<string, string> _editingCreds = new();

    ITokenProvider SelectedProvider => (ProviderBox.SelectedItem as ITokenProvider) ?? ProviderRegistry.All[0];

    void OnProviderChanged(object sender, SelectionChangedEventArgs e) => BuildCredPanel();

    void BuildCredPanel()
    {
        _credInputs.Clear();
        CredPanel.Children.Clear();
        var provider = SelectedProvider;

        BaseUrlHint.Text = provider.DefaultBaseUrl.Length == 0 ? "该类型无需 Base URL" : $"默认:{provider.DefaultBaseUrl}";

        ManualPanel.Visibility = provider.Id == "manual" ? Visibility.Visible : Visibility.Collapsed;
        BaseUrlBox.IsEnabled = provider.DefaultBaseUrl.Length > 0;

        foreach (var field in provider.CredentialFields)
        {
            CredPanel.Children.Add(new Label { Content = field.Label });
            Control box;
            if (field.Secret)
            {
                var pb = new PasswordBox { MinHeight = 34, VerticalContentAlignment = VerticalAlignment.Center };
                if (_editingCreds.TryGetValue(field.Key, out var v)) pb.Password = v;
                box = pb;
            }
            else
            {
                var tb = new TextBox { MinHeight = 34, VerticalContentAlignment = VerticalAlignment.Center };
                if (_editingCreds.TryGetValue(field.Key, out var v)) tb.Text = v;
                box = tb;
            }
            CredPanel.Children.Add(box);
            CredPanel.Children.Add(new Border { Height = 10 });
            _credInputs.Add((field, box));
        }
    }

    Dictionary<string, string> CollectCredentials()
    {
        var dict = new Dictionary<string, string>();
        foreach (var (field, box) in _credInputs)
        {
            var value = box switch
            {
                PasswordBox pb => pb.Password,
                TextBox tb => tb.Text,
                _ => "",
            };
            if (!string.IsNullOrWhiteSpace(value)) dict[field.Key] = value.Trim();
        }
        return dict;
    }

    TokenAccount BuildAccount()
    {
        var provider = SelectedProvider;
        return new TokenAccount
        {
            Id = _editing?.Id ?? Guid.NewGuid().ToString("N"),
            Name = NameBox.Text.Trim(),
            ProviderId = provider.Id,
            BaseUrl = BaseUrlBox.IsEnabled ? BaseUrlBox.Text.Trim() : "",
            ConsoleUrl = ConsoleUrlBox.Text.Trim(),
            ManualUsed = decimal.TryParse(UsedBox.Text, out var u) ? u : null,
            ManualLimit = decimal.TryParse(LimitBox.Text, out var l) ? l : null,
            Enabled = true,
        };
    }

    async void OnTest(object sender, RoutedEventArgs e)
    {
        var provider = SelectedProvider;
        var account = BuildAccount();
        var creds = CollectCredentials();

        TestBtn.IsEnabled = false;
        TestBtn.Content = "测试中…";
        try
        {
            var snap = await provider.FetchAsync(account, creds, CancellationToken.None);
            if (snap.Error is not null)
                Dialog.Info(this, $"测试 {provider.DisplayName}", $"失败:{snap.Error}", "error");
            else
                Dialog.Info(this, $"测试 {provider.DisplayName}", $"成功:{Describe(snap)}", "success");
        }
        catch (Exception ex)
        {
            Dialog.Info(this, $"测试 {provider.DisplayName}", $"失败:{ex.Message}", "error");
        }
        finally
        {
            TestBtn.IsEnabled = true;
            TestBtn.Content = "测试连接";
        }
    }

    static string Describe(TokenSnapshot s) => s.Kind switch
    {
        SnapshotKind.Balance => $"{s.Currency} {s.Balance:N2}",
        SnapshotKind.Quota when s.Limit is { } lim && s.Used is { } used => $"{used:N2} / {lim:N2}",
        SnapshotKind.Quota when s.Used is { } u2 => $"{u2:N2}",
        SnapshotKind.UsageTokens => $"{s.UsedTokens:N0} tokens ({s.Period})",
        _ => "已返回快照",
    };

    void OnSave(object sender, RoutedEventArgs e)
    {
        var account = BuildAccount();
        var creds = CollectCredentials();
        if (SelectedProvider.CredentialFields.Count > 0 && creds.Count == 0)
        {
            Dialog.Warn(this, "无法保存", "请填写凭据（API Key 等）");
            return;
        }
        _card.Upsert(account, creds);
        DialogResult = true;
    }
}
