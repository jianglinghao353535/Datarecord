using System;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using Datarecord.Services;
using Datarecord.ViewModels;
using LiveChartsCore.SkiaSharpView.WPF;

namespace Datarecord
{
    public partial class MachineDetailWindow : Window
    {
        private readonly MachineDetailViewModel _viewModel;
        private readonly DispatcherTimer _chartMouseIdleTimer;

        public MachineDetailWindow(
            MachineItemViewModel machine,
            IMachineMonitoringService machineMonitoringService,
            IMachineStorageService machineStorageService,
            IProductionReportService productionReportService)
        {
            InitializeComponent();
            _viewModel = new MachineDetailViewModel(machine, machineMonitoringService, machineStorageService, productionReportService);
            DataContext = _viewModel;
            TrendChartHost.Content = CreateTrendChart();
            _chartMouseIdleTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(600)
            };
            _chartMouseIdleTimer.Tick += ChartMouseIdleTimer_OnTick;
            Closed += Window_OnClosed;
        }

        private void CloseButton_OnClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void PlcAddressConfigButton_OnClick(object sender, RoutedEventArgs e)
        {
            var dialog = new PlcAddressConfigWindow(DataContext)
            {
                Owner = this
            };

            dialog.ShowDialog();
        }

        private void ExportCsvButton_OnClick(object sender, RoutedEventArgs e)
        {
            var saveDialog = new SaveFileDialog
            {
                Filter = "Excel File (*.xlsx)|*.xlsx",
                FileName = $"{_viewModel.MachineName}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx",
                AddExtension = true,
                DefaultExt = "xlsx"
            };

            if (saveDialog.ShowDialog(this) != true)
            {
                return;
            }

            var result = _viewModel.ExportQueryDataToExcel(saveDialog.FileName);
            MessageBox.Show(
                this,
                result.Message,
                result.Success ? "Export Succeeded" : "Export Failed",
                MessageBoxButton.OK,
                result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }

        private void ExportLimitsButton_OnClick(object sender, RoutedEventArgs e)
        {
            var dialog = new ExportLimitSettingsWindow(_viewModel)
            {
                Owner = this
            };

            dialog.ShowDialog();
        }

        private void Window_OnClosed(object? sender, EventArgs e)
        {
            _chartMouseIdleTimer.Stop();
            _chartMouseIdleTimer.Tick -= ChartMouseIdleTimer_OnTick;
            _viewModel.Dispose();
            Closed -= Window_OnClosed;
        }

        private void ChartCanvas_OnMouseMove(object sender, MouseEventArgs e)
        {
            _viewModel.NotifyChartMouseMove();
            _chartMouseIdleTimer.Stop();
            _chartMouseIdleTimer.Start();
        }

        private void ChartCanvas_OnMouseLeave(object sender, MouseEventArgs e)
        {
            _chartMouseIdleTimer.Stop();
            _viewModel.NotifyChartMouseIdle();
        }

        private void ChartMouseIdleTimer_OnTick(object? sender, EventArgs e)
        {
            _chartMouseIdleTimer.Stop();
            _viewModel.NotifyChartMouseIdle();
        }

        private CartesianChart CreateTrendChart()
        {
            var chart = new CartesianChart();
            chart.AnimationsSpeed = TimeSpan.Zero;
            chart.Background = System.Windows.Media.Brushes.Transparent;
            chart.SetBinding(CartesianChart.SeriesProperty, new Binding(nameof(MachineDetailViewModel.LiveSeries)));
            chart.SetBinding(CartesianChart.XAxesProperty, new Binding(nameof(MachineDetailViewModel.LiveXAxes)));
            chart.SetBinding(CartesianChart.YAxesProperty, new Binding(nameof(MachineDetailViewModel.LiveYAxes)));
            return chart;
        }

    }
}
