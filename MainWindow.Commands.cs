using System.Windows;
using System.Windows.Input;

namespace DigitalTwinIndustrialAutomationPlatform;

public partial class MainWindow
{
    private void InitializeCommands()
    {
        bool ModuleAvailable() => !_closed && !_closing && !_busy && !_navigating && IsRecognitionModule;
        bool GlobalIdle() => !_closed && !_closing && !_busy && !_navigating && _automaticTask is null && !_stoppingAutomatic;
        bool Idle() => GlobalIdle() && IsRecognitionModule;
        bool Paused() => Idle() && _cameraTask is null;
        bool HasImage() => Paused() && _source is not null;
        void Bind(RoutedUICommand command, RoutedEventHandler handler, Func<bool> enabled)
        {
            CommandBindings.Add(new CommandBinding(command,
                (sender, e) => { handler(sender, e); e.Handled = true; },
                (_, e) => { e.CanExecute = enabled(); e.Handled = true; }));
            foreach (InputGesture gesture in command.InputGestures)
                if (gesture is KeyGesture key) InputBindings.Add(new KeyBinding(command, key));
        }
        Bind(UiCommands.Open, Open_Click, Paused);
        Bind(UiCommands.Folder, Folder_Click, Paused);
        Bind(UiCommands.Save, Save_Click, HasImage);
        Bind(UiCommands.CloseImage, CloseImage_Click, () => Idle() && (_source is not null || _cameraTask is not null));
        Bind(UiCommands.ClearFiles, ClearFiles_Click, () => Paused() && _files.Count > 0);
        Bind(UiCommands.Exit, Exit_Click, () => !_closed);
        Bind(UiCommands.Start, Start_Click, Paused);
        Bind(UiCommands.Stop, Stop_Click, () => Idle() && _cameraTask is not null);
        Bind(UiCommands.Sample, Sample_Click, () => Idle() && _cameraTask is not null && _cameraHasFrame);
        Bind(UiCommands.Detect, Detect_Click, HasImage);
        Bind(UiCommands.Grayscale, Grayscale_Click, HasImage);
        Bind(UiCommands.Filter, Filter_Click, HasImage);
        Bind(UiCommands.Edges, Edges_Click, HasImage);
        Bind(UiCommands.Contours, Contours_Click, HasImage);
        Bind(UiCommands.Morphology, Morphology_Click, HasImage);
        Bind(UiCommands.Histogram, Histogram_Click, HasImage);
        Bind(UiCommands.Match, Match_Click, HasImage);
        Bind(UiCommands.TemplateFile, TemplateFile_Click, Paused);
        Bind(UiCommands.Reset, Reset_Click, HasImage);
        Bind(UiCommands.Fit, Fit_Click, () => Idle() && _source is not null);
        Bind(UiCommands.ActualSize, ActualSize_Click, () => Idle() && _source is not null);
        Bind(UiCommands.Properties, Properties_Click, () => Idle() && _source is not null);
        Bind(UiCommands.Settings, Settings_Click, GlobalIdle);
        Bind(UiCommands.FilterSettings, FilterSettings_Click, GlobalIdle);
        Bind(UiCommands.EdgeSettings, EdgeSettings_Click, GlobalIdle);
        Bind(UiCommands.ContourSettings, ContourSettings_Click, GlobalIdle);
        Bind(UiCommands.MorphologySettings, MorphologySettings_Click, GlobalIdle);
        Bind(UiCommands.MatchSettings, MatchSettings_Click, GlobalIdle);
        Bind(UiCommands.Statistics, Statistics_Click, Idle);
        Bind(UiCommands.Export, Export_Click, () => Idle() && _records.Count > 0);
        Bind(UiCommands.Search, Search_Click, Idle);
        Bind(UiCommands.FocusSearch, (_, _) => { SearchBox.Focus(); SearchBox.SelectAll(); }, Idle);
        Bind(UiCommands.ConnectCamera, ConnectCamera_Click, Idle);
        Bind(UiCommands.DisconnectCamera, DisconnectCamera_Click, () => ModuleAvailable() && (_cameraTask is not null || _automaticTask is not null));
        Bind(UiCommands.CaptureProduct, CaptureProduct_Click, Idle);
        Bind(UiCommands.ImportProductImage, ImportProductImage_Click, Paused);
        Bind(UiCommands.MeasureProduct, MeasureProduct_Click, HasImage);
        Bind(UiCommands.SaveProduct, SaveProduct_Click, () => HasImage() && _trainingMeasurement?.IsSingleTarget == true);
        Bind(UiCommands.LoadProducts, LoadProducts_Click, Paused);
        Bind(UiCommands.ChooseProductDirectory, ChooseProductDirectory_Click, Paused);
        Bind(UiCommands.UseProduct, UseProduct_Click, () => Paused() && ProductsGrid.SelectedItem is Recognition.ProductEntry);
        Bind(UiCommands.RemoveProduct, RemoveProduct_Click, () => Paused() && ProductsGrid.SelectedItem is Recognition.ProductEntry);
        Bind(UiCommands.ConnectRobot, ConnectRobot_Click, () => Paused() && !_robot.IsConnected);
        Bind(UiCommands.DisconnectRobot, DisconnectRobot_Click, () => ModuleAvailable() && (_robot.IsConnected || _automaticTask is not null));
        Bind(UiCommands.AutoStart, AutoStart_Click, () => Idle() && _robot.IsConnected && _products.Count > 0);
        Bind(UiCommands.AutoStop, AutoStop_Click, () => ModuleAvailable() && _automaticTask is not null && !_stoppingAutomatic);
        Bind(UiCommands.ExportRunLog, ExportRunLog_Click, () => Paused() && _runLogPath is not null && System.IO.File.Exists(_runLogPath));
        Bind(UiCommands.Help, Help_Click, () => !_closed);
    }
}
