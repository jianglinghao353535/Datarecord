using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Windows.Input;
using System.Windows.Threading;
using ClosedXML.Excel;
using Datarecord.Models;
using Datarecord.Services;

namespace Datarecord.ViewModels
{
    public enum ReportBranchType
    {
        Root,
        DailyDetail,
        DailyTotal,
        MonthlyTotal,
        YearlyTotal
    }

    public sealed class ReportTreeNodeViewModel
    {
        public string Name { get; set; } = string.Empty;

        public Guid MachineId { get; set; }

        public ReportBranchType BranchType { get; set; }

        public ObservableCollection<ReportTreeNodeViewModel> Children { get; } = [];
    }

    public sealed class ReportWindowViewModel : ViewModelBase, IDisposable
    {
        private readonly ObservableCollection<MachineItemViewModel> _machines;
        private readonly IProductionReportService _productionReportService;
        private readonly DispatcherTimer _autoRefreshTimer;
        private ReportTreeNodeViewModel? _selectedNode;
        private DateTime _dailyDetailDate = DateTime.Today;
        private DateTime _dailyTotalDate = DateTime.Today;
        private int _monthlyTotalMonth = DateTime.Today.Month;
        private int _monthlyTotalYear = DateTime.Today.Year;
        private int _yearlyTotalYear = DateTime.Today.Year;
        private double _totalLength;
        private double _totalWeight;
        private string _summaryTitle = "Please select a report item.";

        public ReportWindowViewModel(ObservableCollection<MachineItemViewModel> machines, IProductionReportService productionReportService)
        {
            _machines = machines;
            _productionReportService = productionReportService;
            _machines.CollectionChanged += MachinesOnCollectionChanged;

            MachineNodes = [];
            ReportRecords = [];
            MonthOptions = new ObservableCollection<int>(Enumerable.Range(1, 12));
            YearOptions = new ObservableCollection<int>(Enumerable.Range(DateTime.Today.Year - 5, 11));
            QueryCommand = new RelayCommand(RefreshCurrentSelection);

            _autoRefreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _autoRefreshTimer.Tick += AutoRefreshTimerOnTick;
            _autoRefreshTimer.Start();

            RebuildMachineTree();
        }

        public void Dispose()
        {
            _autoRefreshTimer.Stop();
            _autoRefreshTimer.Tick -= AutoRefreshTimerOnTick;
            _machines.CollectionChanged -= MachinesOnCollectionChanged;
        }

        public ObservableCollection<ReportTreeNodeViewModel> MachineNodes { get; }

        public ObservableCollection<ProductionReportRecordModel> ReportRecords { get; }

        public ObservableCollection<int> MonthOptions { get; }

        public ObservableCollection<int> YearOptions { get; }

        public ICommand QueryCommand { get; }

        public ReportTreeNodeViewModel? SelectedNode
        {
            get => _selectedNode;
            set
            {
                if (SetProperty(ref _selectedNode, value))
                {
                    RaiseSelectionPropertiesChanged();
                    RefreshCurrentSelection();
                }
            }
        }

        public DateTime DailyDetailDate
        {
            get => _dailyDetailDate;
            set => SetProperty(ref _dailyDetailDate, value.Date);
        }

        public DateTime DailyTotalDate
        {
            get => _dailyTotalDate;
            set => SetProperty(ref _dailyTotalDate, value.Date);
        }

        public int MonthlyTotalMonth
        {
            get => _monthlyTotalMonth;
            set => SetProperty(ref _monthlyTotalMonth, Math.Clamp(value, 1, 12));
        }

        public int MonthlyTotalYear
        {
            get => _monthlyTotalYear;
            set => SetProperty(ref _monthlyTotalYear, value);
        }

        public int YearlyTotalYear
        {
            get => _yearlyTotalYear;
            set => SetProperty(ref _yearlyTotalYear, value);
        }

        public double TotalLength
        {
            get => _totalLength;
            private set => SetProperty(ref _totalLength, value);
        }

        public double TotalWeight
        {
            get => _totalWeight;
            private set => SetProperty(ref _totalWeight, value);
        }

        public string SummaryTitle
        {
            get => _summaryTitle;
            private set => SetProperty(ref _summaryTitle, value);
        }

        public bool HasReportSelection => _selectedNode is not null && _selectedNode.BranchType != ReportBranchType.Root;

        public bool IsDailyDetailSelected => _selectedNode?.BranchType == ReportBranchType.DailyDetail;

        public bool IsDailyTotalSelected => _selectedNode?.BranchType == ReportBranchType.DailyTotal;

        public bool IsMonthlyTotalSelected => _selectedNode?.BranchType == ReportBranchType.MonthlyTotal;

        public bool IsYearlyTotalSelected => _selectedNode?.BranchType == ReportBranchType.YearlyTotal;

        public bool IsTotalOutputSelected => IsDailyTotalSelected || IsMonthlyTotalSelected || IsYearlyTotalSelected;

        private void MachinesOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            RebuildMachineTree();
        }

        private void RebuildMachineTree()
        {
            MachineNodes.Clear();

            foreach (var machine in _machines
                         .OrderBy(x => x.Y)
                         .ThenBy(x => x.X)
                         .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            {
                var root = new ReportTreeNodeViewModel
                {
                    Name = machine.Name,
                    MachineId = machine.Id,
                    BranchType = ReportBranchType.Root
                };

                root.Children.Add(new ReportTreeNodeViewModel
                {
                    Name = "Daily Report",
                    MachineId = machine.Id,
                    BranchType = ReportBranchType.DailyDetail
                });
                root.Children.Add(new ReportTreeNodeViewModel
                {
                    Name = "Daily Total Output",
                    MachineId = machine.Id,
                    BranchType = ReportBranchType.DailyTotal
                });
                root.Children.Add(new ReportTreeNodeViewModel
                {
                    Name = "Monthly Total Output",
                    MachineId = machine.Id,
                    BranchType = ReportBranchType.MonthlyTotal
                });
                root.Children.Add(new ReportTreeNodeViewModel
                {
                    Name = "Yearly Total Output",
                    MachineId = machine.Id,
                    BranchType = ReportBranchType.YearlyTotal
                });

                MachineNodes.Add(root);
            }
        }

        private void RefreshCurrentSelection()
        {
            ReportRecords.Clear();
            TotalLength = 0;
            TotalWeight = 0;

            if (_selectedNode is null || _selectedNode.BranchType == ReportBranchType.Root)
            {
                SummaryTitle = "Please select a machine report type on the left.";
                return;
            }

            switch (_selectedNode.BranchType)
            {
                case ReportBranchType.DailyDetail:
                    RefreshDailyDetail();
                    break;
                case ReportBranchType.DailyTotal:
                    RefreshDailyTotal();
                    break;
                case ReportBranchType.MonthlyTotal:
                    RefreshMonthlyTotal();
                    break;
                case ReportBranchType.YearlyTotal:
                    RefreshYearlyTotal();
                    break;
                default:
                    SummaryTitle = "Please select a machine report type on the left.";
                    break;
            }
        }

        private void RefreshDailyDetail()
        {
            var start = DailyDetailDate.Date;
            var end = start.AddDays(1);
            var items = _productionReportService.QueryReports(_selectedNode!.MachineId, start, end);

            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                item.SerialNumber = i + 1;
                ReportRecords.Add(item);
            }

            TotalLength = items.Sum(x => x.Length);
            TotalWeight = items.Sum(x => x.Weight);
            SummaryTitle = $"{_selectedNode.Name} - {start:yyyy-MM-dd}";
        }

        private void RefreshDailyTotal()
        {
            var start = DailyTotalDate.Date;
            var end = start.AddDays(1);
            var totals = _productionReportService.QueryTotals(_selectedNode!.MachineId, start, end);
            TotalLength = totals.TotalLength;
            TotalWeight = totals.TotalWeight;
            ReportRecords.Add(new ProductionReportRecordModel
            {
                SerialNumber = 1,
                Length = totals.TotalLength,
                Weight = totals.TotalWeight
            });
            SummaryTitle = $"{_selectedNode.Name} - {start:yyyy-MM-dd}";
        }

        private void RefreshMonthlyTotal()
        {
            var start = new DateTime(MonthlyTotalYear, MonthlyTotalMonth, 1);
            var end = start.AddMonths(1);
            var totals = _productionReportService.QueryTotals(_selectedNode!.MachineId, start, end);
            TotalLength = totals.TotalLength;
            TotalWeight = totals.TotalWeight;
            ReportRecords.Add(new ProductionReportRecordModel
            {
                SerialNumber = 1,
                Length = totals.TotalLength,
                Weight = totals.TotalWeight
            });
            SummaryTitle = $"{_selectedNode.Name} - {MonthlyTotalYear}-{MonthlyTotalMonth:00}";
        }

        private void RefreshYearlyTotal()
        {
            var start = new DateTime(YearlyTotalYear, 1, 1);
            var end = start.AddYears(1);
            var totals = _productionReportService.QueryTotals(_selectedNode!.MachineId, start, end);
            TotalLength = totals.TotalLength;
            TotalWeight = totals.TotalWeight;
            ReportRecords.Add(new ProductionReportRecordModel
            {
                SerialNumber = 1,
                Length = totals.TotalLength,
                Weight = totals.TotalWeight
            });
            SummaryTitle = $"{_selectedNode.Name} - {YearlyTotalYear}";
        }

        public (bool Success, string Message) ExportCurrentSelectionToExcel(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return (false, "Invalid export path.");
            }

            if (_selectedNode is null || _selectedNode.BranchType == ReportBranchType.Root)
            {
                return (false, "Please select a report type before export.");
            }

            if (!filePath.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                filePath = Path.ChangeExtension(filePath, ".xlsx");
            }

            try
            {
                var (start, end) = GetCurrentRange();
                var details = _productionReportService.QueryReports(_selectedNode.MachineId, start, end);
                var totals = _productionReportService.QueryTotals(_selectedNode.MachineId, start, end);

                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using var workbook = new XLWorkbook();

                var sheet = workbook.Worksheets.Add("Report");
                sheet.Cell(1, 1).Value = "Time";
                sheet.Cell(1, 2).Value = GetExportRangeText();
                sheet.Cell(2, 1).Value = "Machine";
                sheet.Cell(2, 2).Value = GetSelectedMachineName();
                sheet.Cell(3, 1).Value = "Report Type";
                sheet.Cell(3, 2).Value = GetReportTypeText(_selectedNode.BranchType);

                if (_selectedNode.BranchType == ReportBranchType.DailyDetail)
                {
                    sheet.Cell(5, 1).Value = "#";
                    sheet.Cell(5, 2).Value = "Start Time";
                    sheet.Cell(5, 3).Value = "End Time";
                    sheet.Cell(5, 4).Value = "Length";
                    sheet.Cell(5, 5).Value = "Weight";
                    sheet.Cell(5, 6).Value = "Average Speed";

                    for (var i = 0; i < details.Count; i++)
                    {
                        var row = i + 6;
                        sheet.Cell(row, 1).Value = i + 1;
                        sheet.Cell(row, 2).Value = details[i].StartTime;
                        sheet.Cell(row, 2).Style.DateFormat.Format = "yyyy-MM-dd HH:mm:ss";
                        sheet.Cell(row, 3).Value = details[i].EndTime;
                        sheet.Cell(row, 3).Style.DateFormat.Format = "yyyy-MM-dd HH:mm:ss";
                        sheet.Cell(row, 4).Value = details[i].Length;
                        sheet.Cell(row, 5).Value = details[i].Weight;
                        sheet.Cell(row, 6).Value = details[i].AverageSpeed;
                    }

                    if (details.Count == 0)
                    {
                        sheet.Cell(6, 1).Value = "No records in selected range.";
                    }
                }
                else
                {
                    sheet.Cell(5, 1).Value = "#";
                    sheet.Cell(5, 2).Value = "Total Length";
                    sheet.Cell(5, 3).Value = "Total Weight";
                    sheet.Cell(6, 1).Value = 1;
                    sheet.Cell(6, 2).Value = totals.TotalLength;
                    sheet.Cell(6, 3).Value = totals.TotalWeight;
                }

                sheet.Columns().AdjustToContents();
                workbook.SaveAs(filePath);

                return (true, $"Excel export succeeded: {filePath}");
            }
            catch (Exception ex)
            {
                return (false, $"Export failed: {ex.Message}");
            }
        }

        private (DateTime Start, DateTime End) GetCurrentRange()
        {
            return _selectedNode!.BranchType switch
            {
                ReportBranchType.DailyDetail => (DailyDetailDate.Date, DailyDetailDate.Date.AddDays(1)),
                ReportBranchType.DailyTotal => (DailyTotalDate.Date, DailyTotalDate.Date.AddDays(1)),
                ReportBranchType.MonthlyTotal =>
                (
                    new DateTime(MonthlyTotalYear, MonthlyTotalMonth, 1),
                    new DateTime(MonthlyTotalYear, MonthlyTotalMonth, 1).AddMonths(1)
                ),
                ReportBranchType.YearlyTotal =>
                (
                    new DateTime(YearlyTotalYear, 1, 1),
                    new DateTime(YearlyTotalYear, 1, 1).AddYears(1)
                ),
                _ => (DateTime.Today, DateTime.Today.AddDays(1))
            };
        }

        private static string GetReportTypeText(ReportBranchType branchType)
        {
            return branchType switch
            {
                ReportBranchType.DailyDetail => "Daily Report",
                ReportBranchType.DailyTotal => "Daily Total Output",
                ReportBranchType.MonthlyTotal => "Monthly Total Output",
                ReportBranchType.YearlyTotal => "Yearly Total Output",
                _ => "Unknown"
            };
        }

        private void RaiseSelectionPropertiesChanged()
        {
            OnPropertyChanged(nameof(HasReportSelection));
            OnPropertyChanged(nameof(IsDailyDetailSelected));
            OnPropertyChanged(nameof(IsDailyTotalSelected));
            OnPropertyChanged(nameof(IsMonthlyTotalSelected));
            OnPropertyChanged(nameof(IsYearlyTotalSelected));
            OnPropertyChanged(nameof(IsTotalOutputSelected));
        }

        private string GetExportRangeText()
        {
            return _selectedNode?.BranchType switch
            {
                ReportBranchType.DailyDetail => DailyDetailDate.ToString("yyyy-MM-dd"),
                ReportBranchType.DailyTotal => DailyTotalDate.ToString("yyyy-MM-dd"),
                ReportBranchType.MonthlyTotal => $"{MonthlyTotalYear}-{MonthlyTotalMonth:00}",
                ReportBranchType.YearlyTotal => YearlyTotalYear.ToString(),
                _ => string.Empty
            };
        }

        private string GetSelectedMachineName()
        {
            if (_selectedNode is null)
            {
                return string.Empty;
            }

            return _machines.FirstOrDefault(x => x.Id == _selectedNode.MachineId)?.Name ?? _selectedNode.Name;
        }

        private void AutoRefreshTimerOnTick(object? sender, EventArgs e)
        {
            if (!HasReportSelection)
            {
                return;
            }

            RefreshCurrentSelection();
        }
    }
}
