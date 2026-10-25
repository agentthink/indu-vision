using System.Windows.Input;

namespace DigitalTwinIndustrialAutomationPlatform;

/// <summary>菜单、工具栏及快捷键共享命令，保证可用状态和操作入口一致。</summary>
public static class UiCommands
{
    private static RoutedUICommand Create(string name, string text, Key key = Key.None, ModifierKeys modifiers = ModifierKeys.None)
    {
        var gestures = new InputGestureCollection();
        if (key != Key.None) gestures.Add(new KeyGesture(key, modifiers));
        return new RoutedUICommand(text, name, typeof(UiCommands), gestures);
    }
    public static RoutedUICommand Open { get; } = Create(nameof(Open), "打开图片", Key.O, ModifierKeys.Control);
    public static RoutedUICommand Folder { get; } = Create(nameof(Folder), "导入文件夹", Key.O, ModifierKeys.Control | ModifierKeys.Shift);
    public static RoutedUICommand Save { get; } = Create(nameof(Save), "保存结果", Key.S, ModifierKeys.Control);
    public static RoutedUICommand CloseImage { get; } = Create(nameof(CloseImage), "关闭图像", Key.W, ModifierKeys.Control);
    public static RoutedUICommand ClearFiles { get; } = Create(nameof(ClearFiles), "清空文件列表");
    public static RoutedUICommand Exit { get; } = Create(nameof(Exit), "退出");
    public static RoutedUICommand Start { get; } = Create(nameof(Start), "开始预览", Key.F5);
    public static RoutedUICommand Stop { get; } = Create(nameof(Stop), "停止预览", Key.F5, ModifierKeys.Shift);
    public static RoutedUICommand Sample { get; } = Create(nameof(Sample), "采样当前帧", Key.F6);
    public static RoutedUICommand Detect { get; } = Create(nameof(Detect), "运行检测", Key.D, ModifierKeys.Control);
    public static RoutedUICommand Grayscale { get; } = Create(nameof(Grayscale), "灰度图");
    public static RoutedUICommand Filter { get; } = Create(nameof(Filter), "图像滤波");
    public static RoutedUICommand Edges { get; } = Create(nameof(Edges), "边缘检测");
    public static RoutedUICommand Contours { get; } = Create(nameof(Contours), "轮廓提取");
    public static RoutedUICommand Morphology { get; } = Create(nameof(Morphology), "形态学操作");
    public static RoutedUICommand Histogram { get; } = Create(nameof(Histogram), "灰度直方图");
    public static RoutedUICommand Match { get; } = Create(nameof(Match), "模板匹配");
    public static RoutedUICommand TemplateFile { get; } = Create(nameof(TemplateFile), "选择匹配模板");
    public static RoutedUICommand Reset { get; } = Create(nameof(Reset), "恢复原图", Key.R, ModifierKeys.Control);
    public static RoutedUICommand Fit { get; } = Create(nameof(Fit), "适应窗口", Key.D0, ModifierKeys.Control);
    public static RoutedUICommand ActualSize { get; } = Create(nameof(ActualSize), "实际尺寸", Key.D1, ModifierKeys.Control);
    public static RoutedUICommand Properties { get; } = Create(nameof(Properties), "图像属性");
    public static RoutedUICommand FilterSettings { get; } = Create(nameof(FilterSettings), "滤波设置");
    public static RoutedUICommand EdgeSettings { get; } = Create(nameof(EdgeSettings), "边缘设置");
    public static RoutedUICommand ContourSettings { get; } = Create(nameof(ContourSettings), "轮廓设置");
    public static RoutedUICommand MorphologySettings { get; } = Create(nameof(MorphologySettings), "形态学设置");
    public static RoutedUICommand MatchSettings { get; } = Create(nameof(MatchSettings), "模板匹配设置");
    public static RoutedUICommand Settings { get; } = Create(nameof(Settings), "参数设置");
    public static RoutedUICommand Statistics { get; } = Create(nameof(Statistics), "检测统计");
    public static RoutedUICommand Export { get; } = Create(nameof(Export), "导出检测记录", Key.E, ModifierKeys.Control | ModifierKeys.Shift);
    public static RoutedUICommand Search { get; } = Create(nameof(Search), "搜索文件");
    public static RoutedUICommand FocusSearch { get; } = Create(nameof(FocusSearch), "查找文件", Key.L, ModifierKeys.Control);
    public static RoutedUICommand Help { get; } = Create(nameof(Help), "使用说明", Key.F1);
    public static RoutedUICommand CaptureProduct { get; } = Create(nameof(CaptureProduct), "拍照建档");
    public static RoutedUICommand ImportProductImage { get; } = Create(nameof(ImportProductImage), "从本地图像建档", Key.I, ModifierKeys.Control | ModifierKeys.Shift);
    public static RoutedUICommand MeasureProduct { get; } = Create(nameof(MeasureProduct), "轮廓面积周长测量");
    public static RoutedUICommand SaveProduct { get; } = Create(nameof(SaveProduct), "保存产品类");
    public static RoutedUICommand LoadProducts { get; } = Create(nameof(LoadProducts), "加载产品库");
    public static RoutedUICommand ChooseProductDirectory { get; } = Create(nameof(ChooseProductDirectory), "选择产品目录");
    public static RoutedUICommand UseProduct { get; } = Create(nameof(UseProduct), "载入产品样本");
    public static RoutedUICommand RemoveProduct { get; } = Create(nameof(RemoveProduct), "移出产品库");
    public static RoutedUICommand ConnectRobot { get; } = Create(nameof(ConnectRobot), "连接机器人");
    public static RoutedUICommand DisconnectRobot { get; } = Create(nameof(DisconnectRobot), "断开机器人");
    public static RoutedUICommand AutoStart { get; } = Create(nameof(AutoStart), "开始自动识别");
    public static RoutedUICommand AutoStop { get; } = Create(nameof(AutoStop), "停止自动识别");
    public static RoutedUICommand ExportRunLog { get; } = Create(nameof(ExportRunLog), "导出运行日志");
    public static RoutedUICommand ConnectCamera { get; } = Create(nameof(ConnectCamera), "连接相机");
    public static RoutedUICommand DisconnectCamera { get; } = Create(nameof(DisconnectCamera), "断开相机");
}
