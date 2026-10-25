# 添加新的平台模块

平台名称为“数字孪生工业自动化控制平台”，当前已接入“识别与分拣系统”。

## 入口结构

- `Platform/PlatformInfo.cs`：统一平台名称及现有模块元数据。
- `Platform/ModuleDefinition.cs`：模块 ID、名称、简介和图标；模块导航选中状态。
- `MainWindow.Modules.cs`：注册模块和保留各模块视图实例。
- `MainWindow.Navigation.cs`：首页、模块页面、设置、关于之间的导航。
- `MainWindow.xaml`：平台外壳、共享首页、数据驱动的模块卡片和侧边栏。

首页卡片和侧边栏共享同一份 `_modules` 集合，不需要分别手写新模块的两个入口。

## 接入步骤

1. 为新模块创建独立 WPF `UserControl`，例如 `Modules/EquipmentMonitoring/EquipmentMonitoringView.xaml`，使用自己的命令与状态。
2. 在 `MainWindow.Modules.cs` 的 `InitializeModules()` 中添加一次注册：

```csharp
RegisterModule(
    new ModuleDefinition(
        "equipment-monitoring",     // 稳定且唯一的 ID
        "设备监控",                 // 显示在导航和首页
        "设备状态采集与运行信息展示。",
        "\uE713"),                 // Segoe MDL2 Assets 图标
    new Modules.EquipmentMonitoring.EquipmentMonitoringView());
```

上面是未来模块的接入示例，项目中未创建或宣称实现该设备监控模块。

3. 编译后，新模块自动出现在侧边栏“应用模块”分组和首页“应用模块”区域；点击任一入口会进入注册的视图。

无需为每个模块新增 `AppPage` 枚举值或在首页重写卡片。注册会检查 ID/名称是否为空、ID 是否重复、视图是否已挂载到其他容器或重复注册。

## 状态与功能边界

- 模块视图按实例保留，通过 `Visibility` 切换，回到模块时不会重新创建视图。
- `IsRecognitionModule` 限定现有视觉处理命令仅在识别与分拣模块中生效；新模块应使用自己的命令。
- 离开识别模块时先停止自动采样与产品类型发送，再停止手动预览，保留最后一帧。机器人连接可保持，直到用户断开或关闭平台。
- 新模块涉及设备采集时，应自行处理可见性变化和平台关闭时的资源释放。本版未提供对任意新设备的自动生命周期管理。
- 平台设置当前只包含识别与分拣模块参数；新模块的设置页内容需要按其实际配置模型继续扩展。
- 识别模块配置沿用 `%LOCALAPPDATA%\ProductIdentificationSorting\settings.json`，保留已有用户参数。

本版为编译期模块注册，不是动态 DLL 插件加载框架。数字孪生三维场景、机器人控制、PLC 控制等模块均需后续实际开发接入。
