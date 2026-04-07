using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Datarecord.Services;
using Datarecord.ViewModels;

namespace Datarecord
{
    public partial class MainWindow : Window
    {
        private const string MachineDragFormat = "machine-template";

        private readonly DispatcherTimer _arrangeTimer;
        private readonly MySqlSettingsService _mySqlSettingsService;
        private readonly IPlcIntegrationService _plcIntegrationService;
        private readonly IMachineMonitoringService _machineMonitoringService;
        private readonly IMachineStorageService _machineStorageService;
        private double _lastValidViewportWidth;

        public MainWindow()
        {
            InitializeComponent();

            var jsonStorageService = new JsonLayoutStorageService();
            _mySqlSettingsService = new MySqlSettingsService();
            _machineStorageService = new MySqlMachineStorageService(_mySqlSettingsService, jsonStorageService);
            _plcIntegrationService = new PlcIntegrationService();
            _machineMonitoringService = new MachineMonitoringService(_plcIntegrationService, _machineStorageService, Dispatcher);
            DataContext = new MainWindowViewModel(_machineStorageService);
            _machineMonitoringService.Attach(ViewModel.Machines);

            _arrangeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(120)
            };
            _arrangeTimer.Tick += ArrangeTimer_OnTick;

            Loaded += Window_OnLoaded;
            StateChanged += Window_OnStateChanged;
        }

        private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext;

        private void ToolboxCard_OnMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            DragDrop.DoDragDrop(this, MachineDragFormat, DragDropEffects.Copy);
        }

        private void DesignSurface_OnDragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.StringFormat) &&
                        Equals(e.Data.GetData(DataFormats.StringFormat), MachineDragFormat)
                ? DragDropEffects.Copy
                : DragDropEffects.None;

            e.Handled = true;
        }

        private void DesignSurface_OnDrop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.StringFormat) ||
                !Equals(e.Data.GetData(DataFormats.StringFormat), MachineDragFormat))
            {
                return;
            }

            ViewModel.AddMachine(GetSurfaceViewportWidth());
            e.Handled = true;
        }

        private void MachineThumb_OnDragDelta(object sender, DragDeltaEventArgs e)
        {
            if (sender is not Thumb { DataContext: MachineItemViewModel machine })
            {
                return;
            }

            ViewModel.MoveMachine(
                machine,
                e.HorizontalChange,
                e.VerticalChange,
                ViewModel.DesignSurfaceWidth,
                ViewModel.DesignSurfaceHeight);
        }

        private void MachineThumb_OnDragCompleted(object sender, DragCompletedEventArgs e)
        {
            if (sender is not Thumb { DataContext: MachineItemViewModel machine })
            {
                return;
            }

            ViewModel.CommitMachineMove(machine, GetSurfaceViewportWidth());
        }

        private void MachineThumb_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Thumb { DataContext: MachineItemViewModel machine })
            {
                return;
            }

            ViewModel.SelectMachine(machine);
        }

        private void MachineThumb_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Thumb { DataContext: MachineItemViewModel machine })
            {
                return;
            }

            OpenMachineDetail(machine);
            e.Handled = true;
        }

        private void MachineDetailButton_OnClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { DataContext: MachineItemViewModel machine })
            {
                return;
            }

            OpenMachineDetail(machine);
            e.Handled = true;
        }

        private void DatabaseSettingsButton_OnClick(object sender, RoutedEventArgs e)
        {
            var window = new MySqlSettingsWindow(_mySqlSettingsService)
            {
                Owner = this
            };

            if (window.ShowDialog() == true)
            {
                ViewModel.StatusText = "資料庫設定已更新。";
            }
        }

        private void DesignSurface_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource == DesignSurface)
            {
                ViewModel.SelectMachine(null);
            }
        }

        private void DesignSurfaceHost_OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            RequestArrangeDesignSurface();
        }

        private void Window_OnLoaded(object sender, RoutedEventArgs e)
        {
            RequestArrangeDesignSurface();
        }

        private void Window_OnStateChanged(object? sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                return;
            }

            RequestArrangeDesignSurface();
        }

        private void ArrangeTimer_OnTick(object? sender, EventArgs e)
        {
            _arrangeTimer.Stop();
            ArrangeDesignSurface();
        }

        private void RequestArrangeDesignSurface()
        {
            if (!IsLoaded || WindowState == WindowState.Minimized)
            {
                return;
            }

            _arrangeTimer.Stop();
            _arrangeTimer.Start();
        }

        private void ArrangeDesignSurface()
        {
            var viewportWidth = GetSurfaceViewportWidth();
            if (viewportWidth <= 0)
            {
                return;
            }

            ViewModel.ArrangeMachinesInCascade(viewportWidth);
        }

        private void Window_OnClosing(object sender, CancelEventArgs e)
        {
            ViewModel.SaveLayout();
            _machineMonitoringService.Dispose();
        }

        private void OpenMachineDetail(MachineItemViewModel machine)
        {
            ViewModel.SelectMachine(machine);

            var detailWindow = new MachineDetailWindow(machine, _machineMonitoringService)
            {
                Owner = this
            };

            detailWindow.ShowDialog();
            SaveLayoutInBackground("已在背景儲存機台設定。");
        }

        private void SaveLayoutInBackground(string successStatusText)
        {
            var snapshot = ViewModel.Machines.Select(x => x.ToModel()).ToList();
            ViewModel.StatusText = "正在背景儲存機台設定…";

            _ = Task.Run(() =>
            {
                try
                {
                    _machineStorageService.Save(snapshot);
                    _ = Dispatcher.InvokeAsync(() => ViewModel.StatusText = successStatusText);
                }
                catch
                {
                    _ = Dispatcher.InvokeAsync(() => ViewModel.StatusText = "背景儲存失敗，請稍後再試。");
                }
            });
        }

        private async void ReconnectSelectedMachineButton_OnClick(object sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedMachine is null)
            {
                return;
            }

            await _machineMonitoringService.ReconnectAsync(ViewModel.SelectedMachine);
        }

        private double GetSurfaceViewportWidth()
        {
            var viewportWidth = DesignSurfaceHost.ViewportWidth;

            if (double.IsNaN(viewportWidth) || viewportWidth <= 1)
            {
                viewportWidth = DesignSurfaceHost.ActualWidth;
            }

            if (!double.IsNaN(viewportWidth) && viewportWidth > 1)
            {
                _lastValidViewportWidth = viewportWidth;
                return viewportWidth;
            }

            return Math.Max(_lastValidViewportWidth, ViewModel.DesignSurfaceWidth);
        }
    }
}