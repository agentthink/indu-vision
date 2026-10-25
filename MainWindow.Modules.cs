using System.Collections.ObjectModel;
using System.Windows;
using DigitalTwinIndustrialAutomationPlatform.Platform;

namespace DigitalTwinIndustrialAutomationPlatform;

public partial class MainWindow
{
    private readonly ObservableCollection<ModuleEntry> _modules = new();
    private readonly Dictionary<string, FrameworkElement> _moduleViews = new(StringComparer.Ordinal);

    private void InitializeModules()
    {
        ModuleNavigation.ItemsSource = _modules;
        HomeModules.ItemsSource = _modules;
        RegisterModule(PlatformInfo.RecognitionSorting, WorkspacePage);
        // 新模块：在此注册名称、稳定 ID、简介、图标和自己的 UserControl。
        // RegisterModule(new ModuleDefinition("module-id", "模块名称", "模块简介", "\uE713"), new MyModuleView());
    }

    private void RegisterModule(ModuleDefinition definition, FrameworkElement view)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(view);
        if (string.IsNullOrWhiteSpace(definition.Id) || string.IsNullOrWhiteSpace(definition.Name))
            throw new ArgumentException("模块 ID 和名称不能为空。");
        if (_moduleViews.ContainsKey(definition.Id)) throw new ArgumentException($"模块 ID 已注册：{definition.Id}");
        if (view.Parent is not null && !ReferenceEquals(view.Parent, PageHost))
            throw new ArgumentException("模块视图已属于其他容器。");
        if (_moduleViews.Values.Contains(view)) throw new ArgumentException("同一视图不能重复注册为多个模块。");
        view.Visibility = Visibility.Collapsed;
        if (view.Parent is null) PageHost.Children.Add(view);
        _moduleViews.Add(definition.Id, view);
        _modules.Add(new ModuleEntry(definition));
    }

    private bool IsRecognitionModule => _page == AppPage.Module && _activeModuleId == PlatformInfo.RecognitionSortingId;
}
