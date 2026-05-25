using System.Windows;

using System.Collections.ObjectModel;
using Datarecord.Services;
using Datarecord.ViewModels;
using Microsoft.Win32;

namespace Datarecord
{
    public partial class ReportWindow : System.Windows.Window
    {
        public ReportWindow(ObservableCollection<MachineItemViewModel> machines, IProductionReportService productionReportService)
        {
            InitializeComponent();
            DataContext = new ReportWindowViewModel(machines, productionReportService);
            Closed += ReportWindow_OnClosed;
        }

        private void CloseButton_OnClick(object sender, System.Windows.RoutedEventArgs e)
        {
            Close();
        }

        private void TreeView_OnSelectedItemChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<object> e)
        {
            if (DataContext is not ReportWindowViewModel viewModel)
            {
                return;
            }

            if (e.NewValue is ReportTreeNodeViewModel node)
            {
                viewModel.SelectedNode = node;
            }
        }

        private void ExportExcelButton_OnClick(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is not ReportWindowViewModel viewModel)
            {
                return;
            }

            var saveDialog = new SaveFileDialog
            {
                Filter = "Excel File (*.xlsx)|*.xlsx",
                FileName = $"report_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx",
                AddExtension = true,
                DefaultExt = "xlsx"
            };

            if (saveDialog.ShowDialog(this) != true)
            {
                return;
            }

            var result = viewModel.ExportCurrentSelectionToExcel(saveDialog.FileName);
            System.Windows.MessageBox.Show(
                this,
                result.Message,
                result.Success ? "Export Succeeded" : "Export Failed",
                System.Windows.MessageBoxButton.OK,
                result.Success ? System.Windows.MessageBoxImage.Information : System.Windows.MessageBoxImage.Warning);
        }

        private void ReportWindow_OnClosed(object? sender, EventArgs e)
        {
            if (DataContext is IDisposable disposable)
            {
                disposable.Dispose();
            }

            Closed -= ReportWindow_OnClosed;
        }
    }
}
