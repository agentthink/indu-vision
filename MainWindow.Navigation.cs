using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DigitalTwinIndustrialAutomationPlatform.Platform;

namespace DigitalTwinIndustrialAutomationPlatform;

public partial class MainWindow
{
    private enum AppPage { Home, Module, Settings, About }
    private AppPage _page = AppPage.Home;
    private string? _activeModuleId;
    private bool _navigating;
    private bool _refreshRecognitionResult;

    private void InitializeNavigation()
    {
        InitializeModules();
        ShowPage(AppPage.Home);
        StatusText.Text = $"欢迎使用{PlatformInfo.Name} · 请从首页选择应用模块。";
    }

    private void ShowPage(AppPage page, string? moduleId = null)
    {
        _page = page;
        _activeModuleId = page == AppPage.Module ? moduleId : null;
        HomePage.Visibility = page == AppPage.Home ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = page == AppPage.Settings ? Visibility.Visible : Visibility.Collapsed;
        AboutPage.Visibility = page == AppPage.About ? Visibility.Visible : Visibility.Collapsed;
        foreach (var (id, view) in _moduleViews)
            view.Visibility = page == AppPage.Module && id == moduleId ? Visibility.Visible : Visibility.Collapsed;
        SyncNavigation();
        CommandManager.InvalidateRequerySuggested();
    }

    private void SyncNavigation()
    {
        HomeNavigation.IsChecked = _page == AppPage.Home;
        SettingsNavigation.IsChecked = _page == AppPage.Settings;
        AboutNavigation.IsChecked = _page == AppPage.About;
        foreach (var module in _modules)
            module.IsSelected = _page == AppPage.Module && module.Id == _activeModuleId;
    }

    private async Task NavigateAsync(AppPage page, string? moduleId = null)
    {
        if (_closed || _closing || _busy || _navigating) { SyncNavigation(); return; }
        if (page == AppPage.Module)
        {
            moduleId ??= PlatformInfo.RecognitionSortingId;
            if (!_moduleViews.ContainsKey(moduleId))
            {
                SyncNavigation(); ShowError(new ArgumentException("该应用模块尚未注册。")); return;
            }
        }
        if (_page == page && (page != AppPage.Module || _activeModuleId == moduleId)) { SyncNavigation(); return; }
        if (IsRecognitionModule) CommitThreshold();
        _thresholdTimer.Stop();
        _navigating = true; UpdateControls();
        try
        {
            if (IsRecognitionModule && (page != AppPage.Module || moduleId != PlatformInfo.RecognitionSortingId))
            {
                if (_cameraTask is not null) StatusText.Text = "正在停止识别模块摄像头并切换页面……";
                await StopAutomaticAsync();
                await StopCameraAsync();
            }
            if (_closed) return;
            if (page == AppPage.Settings)
            {
                SettingsSaveStatus.Text = string.Empty;
            }
            ShowPage(page, moduleId);
            StatusText.Text = page switch
            {
                AppPage.Home => $"平台首页 · 已接入 {_modules.Count} 个应用模块。",
                AppPage.Module => $"{_modules.First(module => module.Id == moduleId).Name} · 已进入模块工作区。",
                AppPage.Settings => "平台设置 · 当前配置范围为识别与分拣系统模块。",
                _ => $"关于 · {PlatformInfo.Name}。"
            };
        }
        catch (Exception ex) { if (!_closed) { SyncNavigation(); ShowError(ex); } }
        finally { _navigating = false; if (!_closed) UpdateControls(); }

        if (!_closed && IsRecognitionModule && _refreshRecognitionResult)
        {
            _refreshRecognitionResult = false;
            if (_source is not null && _operation != Models.ImageOperation.Original)
                await ProcessAsync(_operation, false);
        }
    }

    private async void Navigation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag } && Enum.TryParse<AppPage>(tag, out var page))
            await NavigateAsync(page);
    }

    private async void ModuleNavigation_Click(object sender, RoutedEventArgs e)
    {
        var id = sender switch
        {
            RadioButton item => item.Tag as string,
            Button button => button.CommandParameter as string,
            _ => null
        };
        if (!string.IsNullOrWhiteSpace(id)) await NavigateAsync(AppPage.Module, id);
    }

    private async void OpenFromHome_Click(object sender, RoutedEventArgs e)
    {
        await NavigateAsync(AppPage.Module, PlatformInfo.RecognitionSortingId);
        if (!_closed && !_navigating && IsRecognitionModule) Open_Click(sender, e);
    }

}
