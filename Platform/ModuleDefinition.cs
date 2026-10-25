using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DigitalTwinIndustrialAutomationPlatform.Platform;

/// <summary>一个已接入模块的入口信息；注册后自动出现在侧边栏与首页。</summary>
public sealed record ModuleDefinition(string Id, string Name, string Description, string Icon);

public sealed class ModuleEntry(ModuleDefinition definition) : INotifyPropertyChanged
{
    public ModuleDefinition Definition { get; } = definition;
    public string Id => Definition.Id;
    public string Name => Definition.Name;
    public string Description => Definition.Description;
    public string Icon => Definition.Icon;
    public bool CanOpenProductImages => Id == PlatformInfo.RecognitionSortingId;
    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
