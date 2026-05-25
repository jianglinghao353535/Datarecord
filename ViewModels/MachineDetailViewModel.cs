using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ClosedXML.Excel;
using Datarecord.Models;
using Datarecord.Services;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace Datarecord.ViewModels
{
    public enum MachineChartType
    {
        Length,
        Diameter,
        Speed,
        Tension
    }

    public sealed class MachineDetailViewModel : ViewModelBase, IDisposable
    {
        private const int PreferredXAxisLabelCount = 11;
        private static readonly TimeSpan ChartAnimationDuration = TimeSpan.FromMilliseconds(350);
        private static readonly TimeSpan LiveWindowDuration = TimeSpan.FromMinutes(5);
        private readonly MachineItemViewModel _machine;
        private readonly IMachineMonitoringService _machineMonitoringService;
        private readonly IMachineStorageService _machineStorageService;
        private readonly IProductionReportService _productionReportService;
        private readonly Timer _analysisTimer;
        private readonly SemaphoreSlim _analysisSemaphore = new(1, 1);
        private readonly CancellationTokenSource _analysisCts = new();
        private readonly RelayCommand _refreshFromPlcCommand;
        private readonly RelayCommand _returnToLiveChartCommand;
        private bool _useTraditionalChinese;
        private DateTime _startDate;
        private DateTime _endDate;
        private string _selectedStartTime;
        private string _selectedEndTime;
        private string _selectedStartHour = "00";
        private string _selectedStartMinute = "00";
        private string _selectedStartSecond = "00";
        private string _selectedEndHour = "00";
        private string _selectedEndMinute = "00";
        private string _selectedEndSecond = "00";
        private MachineChartType _selectedChartType;
        private string _chartSummary = "No data";
        private string _chartTitle = "Speed Trend";
        private string _chartModeText = "Live Mode";
        private string _chartWindowText = "Last 5 Minutes";
        private string _valueRangeText = "--";
        private string _pointCountText = "0 points";
        private string _latestSampleText = "No data";
        private Brush _chartAccentBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#38BDF8")!);
        private Brush _chartAccentSoftBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2238BDF8")!);
        private Brush _chartAccentGlowBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1438BDF8")!);
        private string _chartMetricText = "Speed Records";
        private string _chartHintText = "Y-axis auto-scales when mouse is idle";
        private string _analysisResult = "AI analysis has not run yet.";
        private string _analysisStatusText = "Auto analysis runs every 5 minutes.";
        private bool _useManualYAxis;
        private double _lengthYAxisMin;
        private double _lengthYAxisMax = 10000;
        private double _diameterYAxisMin;
        private double _diameterYAxisMax = 5;
        private double _speedYAxisMin;
        private double _speedYAxisMax = 2000;
        private double _tensionYAxisMin;
        private double _tensionYAxisMax = 200;
        private bool _autoScaleWhenIdle = true;
        private double _exportLengthLowerLimit = 0;
        private double _exportLengthUpperLimit = 10000;
        private double _exportSpeedLowerLimit = 0;
        private double _exportSpeedUpperLimit = 2000;
        private double _exportDiameterLowerLimit = 0;
        private double _exportDiameterUpperLimit = 5;
        private double _exportTensionLowerLimit = 0;
        private double _exportTensionUpperLimit = 200;
        private bool _isChartMouseMoving;
        private bool _isLiveChartMode = true;
        private double _currentAxisMin;
        private double _currentAxisMax = 100;
        private ISeries[] _liveSeries = [];
        private Axis[] _liveXAxes = [];
        private Axis[] _liveYAxes = [];

        public MachineDetailViewModel(
            MachineItemViewModel machine,
            IMachineMonitoringService machineMonitoringService,
            IMachineStorageService machineStorageService,
            IProductionReportService productionReportService)
        {
            _machine = machine;
            _machineMonitoringService = machineMonitoringService;
            _machineStorageService = machineStorageService;
            _productionReportService = productionReportService;
            _machine.PropertyChanged += MachineOnPropertyChanged;
            _machine.TrendRecords.CollectionChanged += TrendRecordsOnCollectionChanged;

            var now = DateTime.Now;
            _startDate = now.Add(-LiveWindowDuration).Date;
            _endDate = now.Date;

            TimeOptions = new ObservableCollection<string>(
                Enumerable.Range(0, 24 * 60)
                    .Select(i => TimeSpan.FromMinutes(i).ToString(@"hh\:mm\:ss")));
            HourOptions = new ObservableCollection<string>(Enumerable.Range(0, 24).Select(i => i.ToString("00")));
            MinuteSecondOptions = new ObservableCollection<string>(Enumerable.Range(0, 60).Select(i => i.ToString("00")));

            _selectedStartTime = now.Add(-LiveWindowDuration).ToString("HH:mm:ss");
            _selectedEndTime = now.ToString("HH:mm:ss");

            _useManualYAxis = _machine.UseManualYAxis;
            _lengthYAxisMin = _machine.LengthYAxisMin;
            _lengthYAxisMax = _machine.LengthYAxisMax;
            _diameterYAxisMin = _machine.DiameterYAxisMin;
            _diameterYAxisMax = _machine.DiameterYAxisMax;
            _speedYAxisMin = _machine.SpeedYAxisMin;
            _speedYAxisMax = _machine.SpeedYAxisMax;
            _tensionYAxisMin = _machine.TensionYAxisMin;
            _tensionYAxisMax = _machine.TensionYAxisMax;
            EnsureMetricYAxisDefaults();

            if (!TimeOptions.Contains(_selectedStartTime))
            {
                _selectedStartTime = "00:00:00";
            }

            if (!TimeOptions.Contains(_selectedEndTime))
            {
                _selectedEndTime = "23:59:00";
            }

            SyncTimePartSelections(notify: false);

            ChartTypes = Enum.GetValues<MachineChartType>();

            _refreshFromPlcCommand = new RelayCommand(
                () => _ = ReconnectAsync(),
                () => _machine.IsEnabled && !_machine.IsManualReconnectInProgress);
            _returnToLiveChartCommand = new RelayCommand(ReturnToLiveChart);

            RefreshFromPlcCommand = _refreshFromPlcCommand;
            RefreshChartCommand = new RelayCommand(ExecuteManualQuery);
            RecordSampleCommand = new RelayCommand(RecordSample);
            ClearHistoryCommand = new RelayCommand(() => _ = ClearHistoryAsync());
            ReturnToLiveChartCommand = _returnToLiveChartCommand;
            ToggleLanguageCommand = new RelayCommand(ToggleLanguage);
            AnalyzeNowCommand = new RelayCommand(ExecuteManualAnalysis);

            SelectedChartType = MachineChartType.Speed;
            RefreshChart();

            _analysisTimer = new Timer(
                _ => _ = RunScheduledAnalysisAsync(),
                null,
                TimeSpan.FromMinutes(5),
                TimeSpan.FromMinutes(5));
        }

        public string MachineName => _machine.Name;

        public PlcType PlcType => _machine.PlcType;

        public double ProductionSpeed
        {
            get => _machine.ProductionSpeed;
            set
            {
                if (Math.Abs(_machine.ProductionSpeed - value) < double.Epsilon)
                {
                    return;
                }

                _machine.ProductionSpeed = value;
                OnPropertyChanged();
            }
        }

        public double ProductionLength
        {
            get => _machine.ProductionLength;
            set
            {
                if (Math.Abs(_machine.ProductionLength - value) < double.Epsilon)
                {
                    return;
                }

                _machine.ProductionLength = value;
                OnPropertyChanged();
            }
        }

        public double ProductionWeight
        {
            get => _machine.ProductionWeight;
            set
            {
                if (Math.Abs(_machine.ProductionWeight - value) < double.Epsilon)
                {
                    return;
                }

                _machine.ProductionWeight = value;
                OnPropertyChanged();
            }
        }

        public double CurrentDiameter
        {
            get => _machine.CurrentDiameter;
            set
            {
                if (Math.Abs(_machine.CurrentDiameter - value) < double.Epsilon)
                {
                    return;
                }
                _machine.CurrentDiameter = value;
                OnPropertyChanged();
            }
        }

        public string PlcAddressProductionSpeed
        {
            get => _machine.PlcAddressProductionSpeed;
            set
            {
                if (_machine.PlcAddressProductionSpeed == value)
                {
                    return;
                }

                _machine.PlcAddressProductionSpeed = value;
                OnPropertyChanged();
            }
        }

        public string PlcAddressProductionLength
        {
            get => _machine.PlcAddressProductionLength;
            set
            {
                if (_machine.PlcAddressProductionLength == value)
                {
                    return;
                }

                _machine.PlcAddressProductionLength = value;
                OnPropertyChanged();
            }
        }

        public string PlcAddressProductionWeight
        {
            get => _machine.PlcAddressProductionWeight;
            set
            {
                if (_machine.PlcAddressProductionWeight == value)
                {
                    return;
                }

                _machine.PlcAddressProductionWeight = value;
                OnPropertyChanged();
            }
        }

        public string PlcAddressWeight
        {
            get => _machine.PlcAddressWeight;
            set
            {
                if (_machine.PlcAddressWeight == value)
                {
                    return;
                }

                _machine.PlcAddressWeight = value;
                OnPropertyChanged();
            }
        }

        public string PlcAddressDiameter
        {
            get => _machine.PlcAddressDiameter;
            set
            {
                if (_machine.PlcAddressDiameter == value)
                {
                    return;
                }

                _machine.PlcAddressDiameter = value;
                OnPropertyChanged();
            }
        }

        public string PlcAddressRuningSignal
        {
            get => _machine.PlcAddressRuningSignal;
            set
            {
                if (_machine.PlcAddressRuningSignal == value)
                {
                    return;
                }

                _machine.PlcAddressRuningSignal = value;
                OnPropertyChanged();
            }
        }

        public string PlcAddressHint => _machine.PlcAddressHint;

        public DateTime StartDate
        {
            get => _startDate;
            set
            {
                if (SetProperty(ref _startDate, value.Date))
                {
                    NormalizeQueryRange(changedStartBoundary: true);
                }
            }
        }

        public DateTime EndDate
        {
            get => _endDate;
            set
            {
                if (SetProperty(ref _endDate, value.Date))
                {
                    NormalizeQueryRange(changedStartBoundary: false);
                }
            }
        }

        public string SelectedStartTime
        {
            get => _selectedStartTime;
            set
            {
                if (SetProperty(ref _selectedStartTime, value))
                {
                    ApplyStartTimeParts(value, notify: true);
                    NormalizeQueryRange(changedStartBoundary: true);
                }
            }
        }

        public string SelectedEndTime
        {
            get => _selectedEndTime;
            set
            {
                if (SetProperty(ref _selectedEndTime, value))
                {
                    ApplyEndTimeParts(value, notify: true);
                    NormalizeQueryRange(changedStartBoundary: false);
                }
            }
        }

        public string SelectedStartHour
        {
            get => _selectedStartHour;
            set
            {
                if (SetProperty(ref _selectedStartHour, value))
                {
                    UpdateStartTimeFromParts();
                }
            }
        }

        public string SelectedStartMinute
        {
            get => _selectedStartMinute;
            set
            {
                if (SetProperty(ref _selectedStartMinute, value))
                {
                    UpdateStartTimeFromParts();
                }
            }
        }

        public string SelectedStartSecond
        {
            get => _selectedStartSecond;
            set
            {
                if (SetProperty(ref _selectedStartSecond, value))
                {
                    UpdateStartTimeFromParts();
                }
            }
        }

        public string SelectedEndHour
        {
            get => _selectedEndHour;
            set
            {
                if (SetProperty(ref _selectedEndHour, value))
                {
                    UpdateEndTimeFromParts();
                }
            }
        }

        public string SelectedEndMinute
        {
            get => _selectedEndMinute;
            set
            {
                if (SetProperty(ref _selectedEndMinute, value))
                {
                    UpdateEndTimeFromParts();
                }
            }
        }

        public string SelectedEndSecond
        {
            get => _selectedEndSecond;
            set
            {
                if (SetProperty(ref _selectedEndSecond, value))
                {
                    UpdateEndTimeFromParts();
                }
            }
        }

        public bool UseManualYAxis
        {
            get => _useManualYAxis;
            set
            {
                if (SetProperty(ref _useManualYAxis, value))
                {
                    _machine.UseManualYAxis = value;
                    RefreshChart();
                }
            }
        }

        public double ManualYAxisMin
        {
            get => GetManualYAxisRange(SelectedChartType).Min;
            set
            {
                if (SetManualYAxisRange(SelectedChartType, value, isMin: true))
                {
                    if (UseManualYAxis)
                    {
                        RefreshChart();
                    }
                }
            }
        }

        public double ManualYAxisMax
        {
            get => GetManualYAxisRange(SelectedChartType).Max;
            set
            {
                if (SetManualYAxisRange(SelectedChartType, value, isMin: false))
                {
                    if (UseManualYAxis)
                    {
                        RefreshChart();
                    }
                }
            }
        }

        public bool AutoScaleWhenIdle
        {
            get => _autoScaleWhenIdle;
            set
            {
                if (SetProperty(ref _autoScaleWhenIdle, value))
                {
                    RefreshChart();
                }
            }
        }

        public double ExportLengthLowerLimit
        {
            get => _exportLengthLowerLimit;
            set => SetProperty(ref _exportLengthLowerLimit, value);
        }

        public double ExportLengthUpperLimit
        {
            get => _exportLengthUpperLimit;
            set => SetProperty(ref _exportLengthUpperLimit, value);
        }

        public double ExportSpeedLowerLimit
        {
            get => _exportSpeedLowerLimit;
            set => SetProperty(ref _exportSpeedLowerLimit, value);
        }

        public double ExportSpeedUpperLimit
        {
            get => _exportSpeedUpperLimit;
            set => SetProperty(ref _exportSpeedUpperLimit, value);
        }

        public double ExportDiameterLowerLimit
        {
            get => _exportDiameterLowerLimit;
            set => SetProperty(ref _exportDiameterLowerLimit, value);
        }

        public double ExportDiameterUpperLimit
        {
            get => _exportDiameterUpperLimit;
            set => SetProperty(ref _exportDiameterUpperLimit, value);
        }

        public double ExportTensionLowerLimit
        {
            get => _exportTensionLowerLimit;
            set => SetProperty(ref _exportTensionLowerLimit, value);
        }

        public double ExportTensionUpperLimit
        {
            get => _exportTensionUpperLimit;
            set => SetProperty(ref _exportTensionUpperLimit, value);
        }

        public MachineChartType SelectedChartType
        {
            get => _selectedChartType;
            set
            {
                if (!SetProperty(ref _selectedChartType, value))
                {
                    return;
                }

                UpdateChartHeaderState();
                OnPropertyChanged(nameof(ManualYAxisMin));
                OnPropertyChanged(nameof(ManualYAxisMax));
                RefreshChart();
            }
        }

        public MachineChartType[] ChartTypes { get; }

        public ObservableCollection<string> TimeOptions { get; }

        public ObservableCollection<string> HourOptions { get; }

        public ObservableCollection<string> MinuteSecondOptions { get; }

        public ICommand RefreshFromPlcCommand { get; }

        public ICommand RefreshChartCommand { get; }

        public ICommand RecordSampleCommand { get; }

        public ICommand ClearHistoryCommand { get; }

        public ICommand ReturnToLiveChartCommand { get; }

        public ICommand ToggleLanguageCommand { get; }

        public ICommand AnalyzeNowCommand { get; }

        public string WindowTitleText => "Machine Details";

        public string HeaderDescriptionText => "Single-machine monitor: record parameters and query trends by time range.";

        public string BackButtonText => "Back";

        public string LanguageToggleText => "EN";

        public string BasicParametersText => "Basic Parameters";

        public string PlcAddressConfigText => "PLC Address Configuration";

        public string ReconnectPlcText => "Reconnect PLC";

        public string PlcReconnectHintText => "PLC auto-connects after startup. Reconnect manually here if needed.";

        public string ProductionSpeedLabel => "Current Line Speed";

        public string ProductionLengthLabel => "Production Length";

        public string ProductionWeightLabel => "Current Tension";

        public string CurrentDiameterLabel => "Current Diameter";

        public string RecordCurrentDataPointText => "Record Current Data Point";

        public string ClearHistoryText => "Clear History";

        public string ChartTypeLabel => "Trend Type";

        public string StartDateLabel => "Start Date";

        public string StartTimeLabel => "Start Time";

        public string EndDateLabel => "End Date";

        public string EndTimeLabel => "End Time";

        public string QueryChartText => "Query Trend";

        public string ReturnToLiveText => "Back to Live";

        public string ExportCsvText => "Export Excel";

        public string ExportLimitSettingText => "Limit Settings";

        public string AnalysisPanelTitleText => "Trend Analysis";

        public string AnalyzeNowText => "Analyze Now";

        public string ExportLimitWindowTitleText => "Export Limit Settings";

        public string ExportLimitWindowHeaderText => "Export Filter Limits";

        public string ExportLimitLowerText => "Lower";

        public string ExportLimitUpperText => "Upper";

        public string ExportLimitLengthText => "Length";

        public string ExportLimitSpeedText => "Speed";

        public string ExportLimitDiameterText => "Diameter";

        public string ExportLimitTensionText => "Tension";

        public string CloseText => "Close";

        public string ConfirmText => "Confirm";

        public string FixedYAxisText => "Fixed Y-Axis";

        public string MinLabelText => "Min";

        public string MaxLabelText => "Max";

        public string AutoScaleWhenIdleText => "Auto scale when mouse is idle";

        public string LatestSampleLabel => "Latest Sample";

        public string ValueRangeLabel => "Value Range";

        public string SamplePointCountLabel => "Sample Count";

        public string AnalysisResult
        {
            get => _analysisResult;
            private set => SetProperty(ref _analysisResult, value);
        }

        public string AnalysisStatusText
        {
            get => _analysisStatusText;
            private set => SetProperty(ref _analysisStatusText, value);
        }

        public string ChartSummary
        {
            get => _chartSummary;
            private set => SetProperty(ref _chartSummary, value);
        }

        public string ChartTitle
        {
            get => _chartTitle;
            private set => SetProperty(ref _chartTitle, value);
        }

        public string ChartModeText
        {
            get => _chartModeText;
            private set => SetProperty(ref _chartModeText, value);
        }

        public Brush ChartAccentBrush
        {
            get => _chartAccentBrush;
            private set => SetProperty(ref _chartAccentBrush, value);
        }

        public Brush ChartAccentSoftBrush
        {
            get => _chartAccentSoftBrush;
            private set => SetProperty(ref _chartAccentSoftBrush, value);
        }

        public Brush ChartAccentGlowBrush
        {
            get => _chartAccentGlowBrush;
            private set => SetProperty(ref _chartAccentGlowBrush, value);
        }

        public string ChartMetricText
        {
            get => _chartMetricText;
            private set => SetProperty(ref _chartMetricText, value);
        }

        public string ChartHintText
        {
            get => _chartHintText;
            private set => SetProperty(ref _chartHintText, value);
        }

        public string ChartWindowText
        {
            get => _chartWindowText;
            private set => SetProperty(ref _chartWindowText, value);
        }

        public string ValueRangeText
        {
            get => _valueRangeText;
            private set => SetProperty(ref _valueRangeText, value);
        }

        public string PointCountText
        {
            get => _pointCountText;
            private set => SetProperty(ref _pointCountText, value);
        }

        public string LatestSampleText
        {
            get => _latestSampleText;
            private set => SetProperty(ref _latestSampleText, value);
        }

        public ISeries[] LiveSeries
        {
            get => _liveSeries;
            private set => SetProperty(ref _liveSeries, value);
        }

        public Axis[] LiveXAxes
        {
            get => _liveXAxes;
            private set => SetProperty(ref _liveXAxes, value);
        }

        public Axis[] LiveYAxes
        {
            get => _liveYAxes;
            private set => SetProperty(ref _liveYAxes, value);
        }

        public string PlcStatusText => _machine.PlcStatusText;

        public void Dispose()
        {
            _analysisTimer.Dispose();
            _analysisCts.Cancel();
            _analysisCts.Dispose();
            _analysisSemaphore.Dispose();
            _machine.PropertyChanged -= MachineOnPropertyChanged;
            _machine.TrendRecords.CollectionChanged -= TrendRecordsOnCollectionChanged;
        }

        public void NotifyChartMouseMove()
        {
            _isChartMouseMoving = true;
        }

        public void NotifyChartMouseIdle()
        {
            if (!_isChartMouseMoving)
            {
                return;
            }

            _isChartMouseMoving = false;
            if (AutoScaleWhenIdle && !UseManualYAxis)
            {
                RefreshChart();
            }
        }

        private void ToggleLanguage()
        {
            _useTraditionalChinese = !_useTraditionalChinese;
            RaiseLanguagePropertyChanged();
            RefreshChart();
        }

        private void RaiseLanguagePropertyChanged()
        {
            OnPropertyChanged(nameof(WindowTitleText));
            OnPropertyChanged(nameof(HeaderDescriptionText));
            OnPropertyChanged(nameof(BackButtonText));
            OnPropertyChanged(nameof(LanguageToggleText));
            OnPropertyChanged(nameof(BasicParametersText));
            OnPropertyChanged(nameof(PlcAddressConfigText));
            OnPropertyChanged(nameof(ReconnectPlcText));
            OnPropertyChanged(nameof(PlcReconnectHintText));
            OnPropertyChanged(nameof(ProductionSpeedLabel));
            OnPropertyChanged(nameof(ProductionLengthLabel));
            OnPropertyChanged(nameof(ProductionWeightLabel));
            OnPropertyChanged(nameof(CurrentDiameterLabel));
            OnPropertyChanged(nameof(RecordCurrentDataPointText));
            OnPropertyChanged(nameof(ChartTypeLabel));
            OnPropertyChanged(nameof(StartDateLabel));
            OnPropertyChanged(nameof(StartTimeLabel));
            OnPropertyChanged(nameof(EndDateLabel));
            OnPropertyChanged(nameof(EndTimeLabel));
            OnPropertyChanged(nameof(QueryChartText));
            OnPropertyChanged(nameof(ReturnToLiveText));
            OnPropertyChanged(nameof(ExportCsvText));
            OnPropertyChanged(nameof(ExportLimitSettingText));
            OnPropertyChanged(nameof(ExportLimitWindowTitleText));
            OnPropertyChanged(nameof(ExportLimitWindowHeaderText));
            OnPropertyChanged(nameof(ExportLimitLowerText));
            OnPropertyChanged(nameof(ExportLimitUpperText));
            OnPropertyChanged(nameof(ExportLimitLengthText));
            OnPropertyChanged(nameof(ExportLimitSpeedText));
            OnPropertyChanged(nameof(ExportLimitDiameterText));
            OnPropertyChanged(nameof(ExportLimitTensionText));
            OnPropertyChanged(nameof(CloseText));
            OnPropertyChanged(nameof(ConfirmText));
            OnPropertyChanged(nameof(AnalysisPanelTitleText));
            OnPropertyChanged(nameof(AnalyzeNowText));
            OnPropertyChanged(nameof(FixedYAxisText));
            OnPropertyChanged(nameof(MinLabelText));
            OnPropertyChanged(nameof(MaxLabelText));
            OnPropertyChanged(nameof(AutoScaleWhenIdleText));
            OnPropertyChanged(nameof(LatestSampleLabel));
            OnPropertyChanged(nameof(ValueRangeLabel));
            OnPropertyChanged(nameof(SamplePointCountLabel));
        }

        private string GetLocalized(string simplified, string traditional)
        {
            return _useTraditionalChinese ? traditional : simplified;
        }

        private void ExecuteManualAnalysis()
        {
            _ = RunManualAnalysisAsync();
        }

        private async Task RunManualAnalysisAsync()
        {
            if (!await _analysisSemaphore.WaitAsync(0, _analysisCts.Token))
            {
                AnalysisStatusText = "Analysis is in progress. Please try again later.";
                return;
            }

            try
            {
                var (start, end) = GetAnalysisRange();
                var points = _machine.TrendRecords
                    .Where(x => x.Timestamp >= start && x.Timestamp <= end)
                    .OrderBy(x => x.Timestamp)
                    .ToList();

                if (points.Count == 0)
                {
                    AnalysisStatusText = "Manual analysis complete: no data available.";
                    return;
                }

                AnalysisStatusText = "Running manual analysis...";
                AnalysisResult = BuildSimpleAnalysis(points);
                AnalysisStatusText = $"Last analysis time: {DateTime.Now:HH:mm:ss}";
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                AnalysisStatusText = $"Manual analysis failed: {ex.Message}";
            }
            finally
            {
                _analysisSemaphore.Release();
            }
        }

        private static string BuildSimpleAnalysis(System.Collections.Generic.IReadOnlyList<MachineTrendRecordModel> points)
        {
            if (points.Count == 0)
            {
                return "No data.";
            }

            var minSpeed = points.Min(x => x.Speed);
            var maxSpeed = points.Max(x => x.Speed);
            var minLength = points.Min(x => x.Length);
            var maxLength = points.Max(x => x.Length);
            var minDiameter = points.Min(x => x.Diameter);
            var maxDiameter = points.Max(x => x.Diameter);
            var minTension = points.Min(x => x.Tension);
            var maxTension = points.Max(x => x.Tension);

            var sb = new StringBuilder();
            sb.AppendLine($"Samples: {points.Count}");
            sb.AppendLine($"Speed range: {minSpeed:0.###} ~ {maxSpeed:0.###}");
            sb.AppendLine($"Length range: {minLength:0.###} ~ {maxLength:0.###}");
            sb.AppendLine($"Diameter range: {minDiameter:0.###} ~ {maxDiameter:0.###}");
            sb.AppendLine($"Tension range: {minTension:0.###} ~ {maxTension:0.###}");
            return sb.ToString();
        }

        private (DateTime Start, DateTime End) GetAnalysisRange()
        {
            var start = _isLiveChartMode
                ? DateTime.Now - TimeSpan.FromMinutes(5)
                : ComposeDateTime(StartDate, SelectedStartTime, DateTime.Today);
            var end = _isLiveChartMode
                ? DateTime.Now
                : ComposeDateTime(EndDate, SelectedEndTime, DateTime.Today.AddDays(1).AddSeconds(-1));

            return end < start ? (end, start) : (start, end);
        }

        private async Task RunScheduledAnalysisAsync()
        {
            if (!await _analysisSemaphore.WaitAsync(0, _analysisCts.Token))
            {
                return;
            }

            try
            {
                var end = DateTime.Now;
                var start = end - TimeSpan.FromMinutes(5);

                var points = _machine.TrendRecords
                    .Where(x => x.Timestamp >= start && x.Timestamp <= end)
                    .OrderBy(x => x.Timestamp)
                    .ToList();

                if (points.Count == 0)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        AnalysisStatusText = "Automatic analysis complete: no new data.";
                    });
                    return;
                }

                Application.Current.Dispatcher.Invoke(() =>
                {
                    AnalysisStatusText = "Running automatic analysis...";
                });

                Application.Current.Dispatcher.Invoke(() =>
                {
                    AnalysisResult = BuildSimpleAnalysis(points);
                    AnalysisStatusText = $"Last analysis time: {DateTime.Now:HH:mm:ss}";
                });
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    AnalysisStatusText = $"Automatic analysis failed: {ex.Message}";
                });
            }
            finally
            {
                _analysisSemaphore.Release();
            }
        }

        public (bool Success, string Message) ExportQueryDataToExcel(string filePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath))
                {
                    return (false, "Invalid export path.");
                }

                if (!filePath.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
                {
                    filePath = Path.ChangeExtension(filePath, ".xlsx");
                }

                var start = ComposeDateTime(StartDate, SelectedStartTime, DateTime.Today);
                var end = ComposeDateTime(EndDate, SelectedEndTime, DateTime.Today.AddDays(1).AddSeconds(-1));
                if (end < start)
                {
                    (start, end) = (end, start);
                }

                var points = _machine.TrendRecords
                    .Where(x => x.Timestamp >= start && x.Timestamp <= end)
                    .OrderBy(x => x.Timestamp)
                    .ToList();

                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using var workbook = new XLWorkbook();
                var limitsSheet = workbook.Worksheets.Add("Filter Limits");
                limitsSheet.Cell(1, 1).Value = "Parameter";
                limitsSheet.Cell(1, 2).Value = "Lower";
                limitsSheet.Cell(1, 3).Value = "Upper";
                limitsSheet.Cell(2, 1).Value = "Length";
                limitsSheet.Cell(2, 2).Value = ExportLengthLowerLimit;
                limitsSheet.Cell(2, 3).Value = ExportLengthUpperLimit;
                limitsSheet.Cell(3, 1).Value = "Speed";
                limitsSheet.Cell(3, 2).Value = ExportSpeedLowerLimit;
                limitsSheet.Cell(3, 3).Value = ExportSpeedUpperLimit;
                limitsSheet.Cell(4, 1).Value = "Diameter";
                limitsSheet.Cell(4, 2).Value = ExportDiameterLowerLimit;
                limitsSheet.Cell(4, 3).Value = ExportDiameterUpperLimit;
                limitsSheet.Cell(5, 1).Value = "Tension";
                limitsSheet.Cell(5, 2).Value = ExportTensionLowerLimit;
                limitsSheet.Cell(5, 3).Value = ExportTensionUpperLimit;
                limitsSheet.Columns().AdjustToContents();

                var lengthSheet = workbook.Worksheets.Add("Length Data");
                lengthSheet.Cell(1, 1).Value = "Time";
                lengthSheet.Cell(1, 2).Value = "Length";
                for (var row = 0; row < points.Count; row++)
                {
                    lengthSheet.Cell(row + 2, 1).Value = points[row].Timestamp;
                    lengthSheet.Cell(row + 2, 1).Style.DateFormat.Format = "yyyy-MM-dd HH:mm:ss";
                    lengthSheet.Cell(row + 2, 2).Value = points[row].Length;
                }

                var speedSheet = workbook.Worksheets.Add("Speed Data");
                speedSheet.Cell(1, 1).Value = "Time";
                speedSheet.Cell(1, 2).Value = "Speed";
                for (var row = 0; row < points.Count; row++)
                {
                    speedSheet.Cell(row + 2, 1).Value = points[row].Timestamp;
                    speedSheet.Cell(row + 2, 1).Style.DateFormat.Format = "yyyy-MM-dd HH:mm:ss";
                    speedSheet.Cell(row + 2, 2).Value = points[row].Speed;
                }

                var diameterSheet = workbook.Worksheets.Add("Diameter Data");
                diameterSheet.Cell(1, 1).Value = "Time";
                diameterSheet.Cell(1, 2).Value = "Diameter";
                for (var row = 0; row < points.Count; row++)
                {
                    diameterSheet.Cell(row + 2, 1).Value = points[row].Timestamp;
                    diameterSheet.Cell(row + 2, 1).Style.DateFormat.Format = "yyyy-MM-dd HH:mm:ss";
                    diameterSheet.Cell(row + 2, 2).Value = points[row].Diameter;
                }

                var tensionSheet = workbook.Worksheets.Add("Tension Data");
                tensionSheet.Cell(1, 1).Value = "Time";
                tensionSheet.Cell(1, 2).Value = "Tension";
                for (var row = 0; row < points.Count; row++)
                {
                    tensionSheet.Cell(row + 2, 1).Value = points[row].Timestamp;
                    tensionSheet.Cell(row + 2, 1).Style.DateFormat.Format = "yyyy-MM-dd HH:mm:ss";
                    tensionSheet.Cell(row + 2, 2).Value = points[row].Tension;
                }

                var violationsSheet = workbook.Worksheets.Add("Out of Range");
                violationsSheet.Cell(1, 1).Value = "Time";
                violationsSheet.Cell(1, 2).Value = "Parameter";
                violationsSheet.Cell(1, 3).Value = "Value";
                violationsSheet.Cell(1, 4).Value = "Lower";
                violationsSheet.Cell(1, 5).Value = "Upper";

                var violationRow = 2;
                foreach (var point in points)
                {
                    if (point.Length < ExportLengthLowerLimit || point.Length > ExportLengthUpperLimit)
                    {
                        FillViolationRow(violationsSheet, violationRow++, point.Timestamp, "Length", point.Length, ExportLengthLowerLimit, ExportLengthUpperLimit);
                    }

                    if (point.Speed < ExportSpeedLowerLimit || point.Speed > ExportSpeedUpperLimit)
                    {
                        FillViolationRow(violationsSheet, violationRow++, point.Timestamp, "Speed", point.Speed, ExportSpeedLowerLimit, ExportSpeedUpperLimit);
                    }

                    if (point.Diameter < ExportDiameterLowerLimit || point.Diameter > ExportDiameterUpperLimit)
                    {
                        FillViolationRow(violationsSheet, violationRow++, point.Timestamp, "Diameter", point.Diameter, ExportDiameterLowerLimit, ExportDiameterUpperLimit);
                    }

                    if (point.Tension < ExportTensionLowerLimit || point.Tension > ExportTensionUpperLimit)
                    {
                        FillViolationRow(violationsSheet, violationRow++, point.Timestamp, "Tension", point.Tension, ExportTensionLowerLimit, ExportTensionUpperLimit);
                    }
                }

                if (violationRow == 2)
                {
                    violationsSheet.Cell(2, 1).Value = "No out-of-range data in the selected time range.";
                }

                foreach (var worksheet in workbook.Worksheets)
                {
                    var used = worksheet.RangeUsed();
                    if (used is not null)
                    {
                        used.SetAutoFilter();
                    }

                    worksheet.Columns().AdjustToContents();
                }

                var lengthChartPath = BuildChartPngPath(Path.GetDirectoryName(filePath), "length");
                var speedChartPath = BuildChartPngPath(Path.GetDirectoryName(filePath), "speed");
                var diameterChartPath = BuildChartPngPath(Path.GetDirectoryName(filePath), "diameter");
                var tensionChartPath = BuildChartPngPath(Path.GetDirectoryName(filePath), "tension");

                try
                {
                    RenderLineChartPng(lengthChartPath, BuildSingleSeriesDefinition(points, "Length", "#22C55E", p => p.Length), start, end, "Length Trend");
                    RenderLineChartPng(speedChartPath, BuildSingleSeriesDefinition(points, "Speed", "#0EA5E9", p => p.Speed), start, end, "Speed Trend");
                    RenderLineChartPng(diameterChartPath, BuildSingleSeriesDefinition(points, "Diameter", "#7C3AED", p => p.Diameter), start, end, "Diameter Trend");
                    RenderLineChartPng(tensionChartPath, BuildSingleSeriesDefinition(points, "Tension", "#F97316", p => p.Tension), start, end, "Tension Trend");

                    lengthSheet.AddPicture(lengthChartPath).MoveTo(lengthSheet.Cell(2, 6)).WithSize(560, 280);
                    speedSheet.AddPicture(speedChartPath).MoveTo(speedSheet.Cell(2, 6)).WithSize(560, 280);
                    diameterSheet.AddPicture(diameterChartPath).MoveTo(diameterSheet.Cell(2, 6)).WithSize(560, 280);
                    tensionSheet.AddPicture(tensionChartPath).MoveTo(tensionSheet.Cell(2, 6)).WithSize(560, 280);
                }
                finally
                {
                    DeleteFileIfExists(lengthChartPath);
                    DeleteFileIfExists(speedChartPath);
                    DeleteFileIfExists(diameterChartPath);
                    DeleteFileIfExists(tensionChartPath);
                }

                workbook.SaveAs(filePath);

                var successText = $"Excel export succeeded: {filePath}";
                return (true, successText);
            }
            catch (Exception ex)
            {
                var errorText = $"Export failed: {ex.Message}";
                return (false, errorText);
            }
        }

        private async System.Threading.Tasks.Task ReconnectAsync()
        {
            await _machineMonitoringService.ReconnectAsync(_machine);
        }

        private void UpdateStartTimeFromParts()
        {
            var newTime = $"{_selectedStartHour}:{_selectedStartMinute}:{_selectedStartSecond}";
            if (_selectedStartTime == newTime)
            {
                return;
            }

            _selectedStartTime = newTime;
            OnPropertyChanged(nameof(SelectedStartTime));
            NormalizeQueryRange(changedStartBoundary: true);
        }

        private void UpdateEndTimeFromParts()
        {
            var newTime = $"{_selectedEndHour}:{_selectedEndMinute}:{_selectedEndSecond}";
            if (_selectedEndTime == newTime)
            {
                return;
            }

            _selectedEndTime = newTime;
            OnPropertyChanged(nameof(SelectedEndTime));
            NormalizeQueryRange(changedStartBoundary: false);
        }

        private void SyncTimePartSelections(bool notify)
        {
            ApplyStartTimeParts(_selectedStartTime, notify);
            ApplyEndTimeParts(_selectedEndTime, notify);
        }

        private void ApplyStartTimeParts(string timeText, bool notify)
        {
            var (hour, minute, second) = ParseTimeParts(timeText);
            _selectedStartHour = hour;
            _selectedStartMinute = minute;
            _selectedStartSecond = second;

            if (!notify)
            {
                return;
            }

            OnPropertyChanged(nameof(SelectedStartHour));
            OnPropertyChanged(nameof(SelectedStartMinute));
            OnPropertyChanged(nameof(SelectedStartSecond));
        }

        private void ApplyEndTimeParts(string timeText, bool notify)
        {
            var (hour, minute, second) = ParseTimeParts(timeText);
            _selectedEndHour = hour;
            _selectedEndMinute = minute;
            _selectedEndSecond = second;

            if (!notify)
            {
                return;
            }

            OnPropertyChanged(nameof(SelectedEndHour));
            OnPropertyChanged(nameof(SelectedEndMinute));
            OnPropertyChanged(nameof(SelectedEndSecond));
        }

        private static (string Hour, string Minute, string Second) ParseTimeParts(string timeText)
        {
            if (TimeSpan.TryParse(timeText, out var span))
            {
                return (span.Hours.ToString("00"), span.Minutes.ToString("00"), span.Seconds.ToString("00"));
            }

            return ("00", "00", "00");
        }

        private void RecordSample()
        {
            _machine.AddTrendRecord(DateTime.Now);
            ChartSummary = $"Recorded {_machine.TrendRecords.Count} data point(s).";
            RefreshChart();
        }

        private async Task ClearHistoryAsync()
        {
            try
            {
                var machineId = _machine.Id;
                await Task.Run(() =>
                {
                    _machineStorageService.ClearMachineHistory(machineId);
                    _productionReportService.ClearReports(machineId);
                });

                _machine.TrendRecords.Clear();
                ChartSummary = "Current machine history has been cleared.";
                RefreshChart();
            }
            catch (Exception ex)
            {
                ChartSummary = $"Clear history failed: {ex.Message}";
            }
        }

        private void ExecuteManualQuery()
        {
            _isLiveChartMode = false;
            UpdateChartHeaderState();
            RefreshChart();
        }

        private void ReturnToLiveChart()
        {
            _isLiveChartMode = true;
            UpdateChartHeaderState();
            RefreshChart();
        }

        private void MachineOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(MachineItemViewModel.ProductionSpeed):
                    OnPropertyChanged(nameof(ProductionSpeed));
                    break;
                case nameof(MachineItemViewModel.ProductionLength):
                    OnPropertyChanged(nameof(ProductionLength));
                    break;
                case nameof(MachineItemViewModel.ProductionWeight):
                    OnPropertyChanged(nameof(ProductionWeight));
                    break;
                case nameof(MachineItemViewModel.CurrentDiameter):
                    OnPropertyChanged(nameof(CurrentDiameter));
                    break;
                case nameof(MachineItemViewModel.PlcStatusText):
                    OnPropertyChanged(nameof(PlcStatusText));
                    break;
                case nameof(MachineItemViewModel.IsPlcSynchronizing):
                case nameof(MachineItemViewModel.IsManualReconnectInProgress):
                case nameof(MachineItemViewModel.IsEnabled):
                    _refreshFromPlcCommand.RaiseCanExecuteChanged();
                    break;
            }

            if (e.PropertyName is nameof(MachineItemViewModel.ProductionSpeed)
                or nameof(MachineItemViewModel.ProductionLength)
                or nameof(MachineItemViewModel.ProductionWeight)
                or nameof(MachineItemViewModel.CurrentDiameter)
                )
            {
                RefreshChart();
            }
        }

        private void TrendRecordsOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            RefreshChart();
        }

        private void SetLiveWindow(DateTime latestTimestamp)
        {
            var newEnd = latestTimestamp;
            var newStart = newEnd - LiveWindowDuration;

            _startDate = newStart.Date;
            _endDate = newEnd.Date;
            _selectedStartTime = newStart.ToString("HH:mm:ss");
            _selectedEndTime = newEnd.ToString("HH:mm:ss");
            SyncTimePartSelections(notify: false);

            OnPropertyChanged(nameof(StartDate));
            OnPropertyChanged(nameof(EndDate));
            OnPropertyChanged(nameof(SelectedStartTime));
            OnPropertyChanged(nameof(SelectedEndTime));
            OnPropertyChanged(nameof(SelectedStartHour));
            OnPropertyChanged(nameof(SelectedStartMinute));
            OnPropertyChanged(nameof(SelectedStartSecond));
            OnPropertyChanged(nameof(SelectedEndHour));
            OnPropertyChanged(nameof(SelectedEndMinute));
            OnPropertyChanged(nameof(SelectedEndSecond));
        }

        private void NormalizeQueryRange(bool changedStartBoundary)
        {
            var start = ComposeDateTime(_startDate, _selectedStartTime, _startDate);
            var end = ComposeDateTime(_endDate, _selectedEndTime, _endDate);
            if (start <= end)
            {
                return;
            }

            if (changedStartBoundary)
            {
                _endDate = start.Date;
                _selectedEndTime = start.ToString("HH:mm:ss");
                ApplyEndTimeParts(_selectedEndTime, notify: true);
                OnPropertyChanged(nameof(EndDate));
                OnPropertyChanged(nameof(SelectedEndTime));
            }
            else
            {
                _startDate = end.Date;
                _selectedStartTime = end.ToString("HH:mm:ss");
                ApplyStartTimeParts(_selectedStartTime, notify: true);
                OnPropertyChanged(nameof(StartDate));
                OnPropertyChanged(nameof(SelectedStartTime));
            }
        }

        private void RefreshChart()
        {
            var start = _isLiveChartMode
                ? DateTime.Now - LiveWindowDuration
                : ComposeDateTime(StartDate, SelectedStartTime, DateTime.Today);
            var end = _isLiveChartMode
                ? DateTime.Now
                : ComposeDateTime(EndDate, SelectedEndTime, DateTime.Today.AddDays(1).AddSeconds(-1));

            if (end < start)
            {
                (start, end) = (end, start);
            }

            UpdateChartHeaderState(start, end);

            var points = _machine.TrendRecords
                .Where(x => x.Timestamp >= start && x.Timestamp <= end)
                .OrderBy(x => x.Timestamp)
                .ToList();

            var definitions = BuildSeriesDefinitions(points);
            var values = definitions.SelectMany(x => x.Values).Where(IsFinite).ToArray();
            var (defaultMin, defaultMax) = GetDefaultAxisRange();
            var (axisMin, axisMax) = ResolveAxisRange(values, defaultMin, defaultMax);

            LiveSeries = BuildChartSeries(definitions, start, end, axisMin);
            LiveXAxes = BuildXAxes(start, end);
            LiveYAxes = BuildYAxes(axisMin, axisMax);
            UpdateChartOverview(points, values, start, end);

            ChartSummary = points.Count == 0
                ? "No data in the selected time range."
                : $"Points: {points.Count}, range: {axisMin:0.#####} ~ {axisMax:0.#####}, start: {start:yyyy-MM-dd HH:mm:ss}, end: {end:yyyy-MM-dd HH:mm:ss}";
        }

        private ChartSeriesDefinition[] BuildSeriesDefinitions(System.Collections.Generic.IReadOnlyList<MachineTrendRecordModel> points)
        {
            if (points.Count == 0)
            {
                return [];
            }

            return SelectedChartType switch
            {
                MachineChartType.Length =>
                [
                    new ChartSeriesDefinition(
                        "Length",
                        "#22C55E",
                        points.Select(p => p.Timestamp).ToArray(),
                        points.Select(p => SanitizeValue(p.Length, SelectedChartType)).ToArray())
                ],
                MachineChartType.Diameter =>
                [
                    new ChartSeriesDefinition(
                        "Diameter",
                        "#7C3AED",
                        points.Select(p => p.Timestamp).ToArray(),
                        points.Select(p => ConvertRawDiameter(p.Diameter)).ToArray())
                ],
                MachineChartType.Speed =>
                [
                    new ChartSeriesDefinition(
                        "Speed",
                        "#0EA5E9",
                        points.Select(p => p.Timestamp).ToArray(),
                        points.Select(p => SanitizeValue(p.Speed, SelectedChartType)).ToArray())
                ],
                MachineChartType.Tension =>
                [
                    new ChartSeriesDefinition(
                        "Tension",
                        "#F97316",
                        points.Select(p => p.Timestamp).ToArray(),
                        points.Select(p => SanitizeValue(p.Tension, SelectedChartType)).ToArray())
                ],
                _ => []
            };
        }

        private ISeries[] BuildChartSeries(ChartSeriesDefinition[] definitions, DateTime start, DateTime end, double axisMin)
        {
            return definitions
                .Select(BuildLineSeries)
                .Cast<ISeries>()
                .ToArray();
        }

        private LineSeries<ObservablePoint> BuildLineSeries(ChartSeriesDefinition definition)
        {
            var color = ParseColor(definition.ColorHex);
            var points = definition.Timestamps
                .Zip(definition.Values, (timestamp, value) => new ObservablePoint(timestamp.ToOADate(), value))
                .ToArray();
            var visiblePointCount = points.Count(p => p.Y is double y && IsFinite(y));
            var geometrySize = visiblePointCount <= 2 ? 7 : 0;

            return new LineSeries<ObservablePoint>
            {
                Name = definition.Name,
                Values = points,
                Fill = new SolidColorPaint(color.WithAlpha(32)),
                LineSmoothness = 0.45,
                GeometrySize = geometrySize,
                AnimationsSpeed = TimeSpan.Zero,
                Stroke = new SolidColorPaint(color, 4),
                GeometryFill = geometrySize > 0 ? new SolidColorPaint(color) : null,
                GeometryStroke = geometrySize > 0 ? new SolidColorPaint(color.WithAlpha(220), 1) : null
            };
        }

        private ScatterSeries<ObservablePoint>? BuildLineSampleMarkerSeries(ChartSeriesDefinition definition, DateTime[] tickTimes)
        {
            var markerValues = tickTimes
                .Select(tickTime => new ObservablePoint(tickTime.ToOADate(), InterpolateValueAt(definition, tickTime)))
                .Where(point => point.Y is double y && IsFinite(y))
                .ToArray();

            if (markerValues.Length == 0)
            {
                return null;
            }

            var color = ParseColor(definition.ColorHex);

            return new ScatterSeries<ObservablePoint>
            {
                Name = $"{definition.Name}_sample_markers",
                Values = markerValues,
                GeometrySize = 6,
                MinGeometrySize = 6,
                AnimationsSpeed = TimeSpan.Zero,
                Fill = new SolidColorPaint(color),
                Stroke = new SolidColorPaint(color.WithAlpha(220), 1)
            };
        }

        private static double InterpolateValueAt(ChartSeriesDefinition definition, DateTime tickTime)
        {
            var timestamps = definition.Timestamps;
            var values = definition.Values;

            if (timestamps.Length == 0 || values.Length == 0)
            {
                return double.NaN;
            }

            if (tickTime <= timestamps[0])
            {
                return values[0];
            }

            var lastIndex = timestamps.Length - 1;
            if (tickTime >= timestamps[lastIndex])
            {
                return values[lastIndex];
            }

            for (var i = 0; i < lastIndex; i++)
            {
                var leftTime = timestamps[i];
                var rightTime = timestamps[i + 1];

                if (tickTime < leftTime || tickTime > rightTime)
                {
                    continue;
                }

                var leftValue = values[i];
                var rightValue = values[i + 1];
                if (!IsFinite(leftValue) || !IsFinite(rightValue))
                {
                    return double.NaN;
                }

                var duration = (rightTime - leftTime).TotalMilliseconds;
                if (duration <= 0)
                {
                    return leftValue;
                }

                var progress = (tickTime - leftTime).TotalMilliseconds / duration;
                return leftValue + (rightValue - leftValue) * progress;
            }

            return double.NaN;
        }

        private DateTime[] BuildXAxisTickTimes(DateTime start, DateTime end)
        {
            var range = end - start;
            if (range <= TimeSpan.Zero)
            {
                range = TimeSpan.FromMinutes(1);
            }

            var step = TimeSpan.FromTicks(range.Ticks / Math.Max(1, PreferredXAxisLabelCount - 1));
            if (step <= TimeSpan.Zero)
            {
                step = TimeSpan.FromSeconds(1);
            }

            return Enumerable.Range(0, PreferredXAxisLabelCount)
                .Select(i => start + TimeSpan.FromTicks(step.Ticks * i))
                .TakeWhile(time => time <= end)
                .ToArray();
        }

        private Axis[] BuildXAxes(DateTime start, DateTime end)
        {
            var range = end - start;
            if (range <= TimeSpan.Zero)
            {
                range = TimeSpan.FromMinutes(1);
            }

            var step = TimeSpan.FromTicks(range.Ticks / Math.Max(1, PreferredXAxisLabelCount - 1));
            if (step <= TimeSpan.Zero)
            {
                step = TimeSpan.FromSeconds(1);
            }

            var labelFormat = range.TotalHours >= 24 ? "MM-dd HH:mm" : "HH:mm:ss";

            return
            [
                new Axis
                {
                    Name = "Time",
                    LabelsRotation = 0,
                    MinLimit = start.ToOADate(),
                    MaxLimit = end.ToOADate(),
                    MinStep = step.TotalDays,
                    ForceStepToMin = true,
                    TextSize = 12,
                    Labeler = value => DateTime.FromOADate(value).ToString(labelFormat),
                    LabelsPaint = new SolidColorPaint(new SKColor(148, 163, 184)),
                    NamePaint = new SolidColorPaint(new SKColor(148, 163, 184)),
                    SeparatorsPaint = new SolidColorPaint(new SKColor(148, 163, 184, 45)) { StrokeThickness = 1 },
                    CrosshairPaint = new SolidColorPaint(new SKColor(148, 163, 184, 170)) { StrokeThickness = 1 },
                    CrosshairLabelsPaint = null
                }
            ];
        }

        private Axis[] BuildYAxes(double min, double max)
        {
            var span = Math.Max(Math.Abs(max - min), 0.0001);
            var edgePadding = SelectedChartType switch
            {
                MachineChartType.Diameter => Math.Max(span * 0.04, 0.01),
                MachineChartType.Length => Math.Max(span * 0.04, 1d),
                MachineChartType.Speed => Math.Max(span * 0.04, 1d),
                MachineChartType.Tension => Math.Max(span * 0.04, 0.2d),
                _ => Math.Max(span * 0.04, 0.1d)
            };

            var displayMin = min - edgePadding;
            var displayMax = max + edgePadding;

            return
            [
                new Axis
                {
                    Name = GetAxisTitle(),
                    MinLimit = displayMin,
                    MaxLimit = displayMax,
                    TextSize = 13,
                    Labeler = value => value.ToString(SelectedChartType == MachineChartType.Diameter ? "0.000" : "0.#####"),
                    LabelsPaint = new SolidColorPaint(new SKColor(226, 232, 240)),
                    NamePaint = new SolidColorPaint(new SKColor(203, 213, 225)),
                    SeparatorsPaint = new SolidColorPaint(new SKColor(148, 163, 184, 70)) { StrokeThickness = 1 },
                    CrosshairPaint = new SolidColorPaint(new SKColor(203, 213, 225, 150)) { StrokeThickness = 1 },
                    CrosshairLabelsPaint = null
                }
            ];
        }

        private void UpdateChartHeaderState(DateTime? start = null, DateTime? end = null)
        {
            var accentColor = SelectedChartType switch
            {
                MachineChartType.Diameter => "#A855F7",
                MachineChartType.Length => "#22C55E",
                MachineChartType.Tension => "#F97316",
                MachineChartType.Speed => "#38BDF8",
                _ => "#38BDF8"
            };

            ChartAccentBrush = CreateBrush(accentColor);
            ChartAccentSoftBrush = CreateBrush($"#22{accentColor[1..]}");
            ChartAccentGlowBrush = CreateBrush($"#14{accentColor[1..]}");
            ChartTitle = SelectedChartType switch
            {
                MachineChartType.Diameter => "Diameter Trend",
                MachineChartType.Length => "Length Trend",
                MachineChartType.Speed => "Speed Trend",
                MachineChartType.Tension => "Tension Trend",
                _ => "Speed Trend"
            };

            ChartMetricText = SelectedChartType switch
            {
                MachineChartType.Diameter => "Diameter Records",
                MachineChartType.Length => "Length Records",
                MachineChartType.Speed => "Line Speed",
                MachineChartType.Tension => "Tension Records",
                _ => "Speed Records"
            };

            ChartModeText = _isLiveChartMode
                ? "Live Mode"
                : "History Query";

            if (start.HasValue && end.HasValue)
            {
                ChartWindowText = $"{start.Value:MM-dd HH:mm} ~ {end.Value:MM-dd HH:mm}";
            }
            else if (_isLiveChartMode)
            {
                ChartWindowText = "Last 5 Minutes";
            }

            ChartHintText = UseManualYAxis
                ? "Using fixed Y-axis range"
                : AutoScaleWhenIdle
                    ? "Y-axis auto-scales when mouse is idle"
                    : "Scale ratio is fixed";
        }

        private void UpdateChartOverview(System.Collections.Generic.IReadOnlyList<MachineTrendRecordModel> points, double[] values, DateTime start, DateTime end)
        {
            UpdateChartHeaderState(start, end);
            PointCountText = $"{points.Count} points";
            LatestSampleText = points.Count == 0
                ? "No data"
                : points[^1].Timestamp.ToString("yyyy-MM-dd HH:mm:ss");

            ValueRangeText = values.Length == 0
                ? "--"
                : $"{values.Min():0.#####} ~ {values.Max():0.#####}{GetValueUnit()}";
        }

        private string GetAxisTitle()
        {
            return SelectedChartType switch
            {
                MachineChartType.Length => "Length",
                MachineChartType.Diameter => "Diameter",
                MachineChartType.Speed => "Speed",
                MachineChartType.Tension => "Tension",
                _ => "Value"
            };
        }

        private string GetValueUnit()
        {
            return SelectedChartType switch
            {
                MachineChartType.Length => " m",
                MachineChartType.Diameter => " mm",
                MachineChartType.Tension => " N",
                _ => string.Empty
            };
        }



        private static Brush CreateBrush(string color)
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)!);
        }

        private void EnsureMetricYAxisDefaults()
        {
            if (_speedYAxisMax <= _speedYAxisMin)
            {
                _speedYAxisMin = _machine.ManualYAxisMin;
                _speedYAxisMax = _machine.ManualYAxisMax;
            }

            NormalizeMetricAxisRange(MachineChartType.Length, 0, 10000);
            NormalizeMetricAxisRange(MachineChartType.Diameter, 0, 5);
            NormalizeMetricAxisRange(MachineChartType.Speed, 0, 2000);
            NormalizeMetricAxisRange(MachineChartType.Tension, 0, 200);

            PersistMetricYAxisToMachine();
        }

        private void NormalizeMetricAxisRange(MachineChartType chartType, double defaultMin, double defaultMax)
        {
            var (min, max) = GetManualYAxisRange(chartType);
            if (max <= min)
            {
                SetManualYAxisRange(chartType, defaultMin, isMin: true, notify: false);
                SetManualYAxisRange(chartType, defaultMax, isMin: false, notify: false);
            }
        }

        private (double Min, double Max) GetManualYAxisRange(MachineChartType chartType)
        {
            return chartType switch
            {
                MachineChartType.Length => (_lengthYAxisMin, _lengthYAxisMax),
                MachineChartType.Diameter => (_diameterYAxisMin, _diameterYAxisMax),
                MachineChartType.Speed => (_speedYAxisMin, _speedYAxisMax),
                MachineChartType.Tension => (_tensionYAxisMin, _tensionYAxisMax),
                _ => (_speedYAxisMin, _speedYAxisMax)
            };
        }

        private bool SetManualYAxisRange(MachineChartType chartType, double value, bool isMin, bool notify = true)
        {
            var (currentMin, currentMax) = GetManualYAxisRange(chartType);
            var current = isMin ? currentMin : currentMax;
            if (Math.Abs(current - value) < double.Epsilon)
            {
                return false;
            }

            switch (chartType)
            {
                case MachineChartType.Length:
                    if (isMin)
                    {
                        _lengthYAxisMin = value;
                    }
                    else
                    {
                        _lengthYAxisMax = value;
                    }
                    break;
                case MachineChartType.Diameter:
                    if (isMin)
                    {
                        _diameterYAxisMin = value;
                    }
                    else
                    {
                        _diameterYAxisMax = value;
                    }
                    break;
                case MachineChartType.Speed:
                    if (isMin)
                    {
                        _speedYAxisMin = value;
                    }
                    else
                    {
                        _speedYAxisMax = value;
                    }
                    break;
                case MachineChartType.Tension:
                    if (isMin)
                    {
                        _tensionYAxisMin = value;
                    }
                    else
                    {
                        _tensionYAxisMax = value;
                    }
                    break;
                default:
                    return false;
            }

            if (notify)
            {
                OnPropertyChanged(isMin ? nameof(ManualYAxisMin) : nameof(ManualYAxisMax));
            }

            PersistMetricYAxisToMachine();
            return true;
        }

        private void PersistMetricYAxisToMachine()
        {
            _machine.LengthYAxisMin = _lengthYAxisMin;
            _machine.LengthYAxisMax = _lengthYAxisMax;
            _machine.DiameterYAxisMin = _diameterYAxisMin;
            _machine.DiameterYAxisMax = _diameterYAxisMax;
            _machine.SpeedYAxisMin = _speedYAxisMin;
            _machine.SpeedYAxisMax = _speedYAxisMax;
            _machine.TensionYAxisMin = _tensionYAxisMin;
            _machine.TensionYAxisMax = _tensionYAxisMax;

            _machine.ManualYAxisMin = _speedYAxisMin;
            _machine.ManualYAxisMax = _speedYAxisMax;
        }

        private (double Min, double Max) GetDefaultAxisRange()
        {
            return SelectedChartType switch
            {
                MachineChartType.Length => (0, 10000),
                MachineChartType.Diameter => (0, 5),
                MachineChartType.Speed => (0, 2000),
                MachineChartType.Tension => (0, 200),
                _ => (0, 100)
            };
        }

        private (double Min, double Max) ResolveAxisRange(double[] values, double defaultMin, double defaultMax)
        {
            if (UseManualYAxis)
            {
                var min = Math.Min(ManualYAxisMin, ManualYAxisMax);
                var max = Math.Max(ManualYAxisMin, ManualYAxisMax);
                if (Math.Abs(max - min) < 0.0001)
                {
                    max = min + 1;
                }

                _currentAxisMin = min;
                _currentAxisMax = max;
                return (min, max);
            }

            if (AutoScaleWhenIdle && !_isChartMouseMoving && values.Length > 0)
            {
                var min = values.Min();
                var max = values.Max();
                var span = max - min;
                if (span < 0.0001)
                {
                    var halfWindow = SelectedChartType switch
                    {
                        MachineChartType.Length => 50d,
                        MachineChartType.Diameter => 0.2d,
                        MachineChartType.Speed => 50d,
                        MachineChartType.Tension => 5d,
                        _ => 1d
                    };

                    _currentAxisMin = min - halfWindow;
                    _currentAxisMax = max + halfWindow;
                    return (_currentAxisMin, _currentAxisMax);
                }

                var padding = Math.Max(span * 0.12, 0.1);
                _currentAxisMin = min - padding;
                _currentAxisMax = max + padding;
                return (_currentAxisMin, _currentAxisMax);
            }

            if (_currentAxisMax <= _currentAxisMin)
            {
                _currentAxisMin = defaultMin;
                _currentAxisMax = defaultMax;
            }

            return (_currentAxisMin, _currentAxisMax);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static double SanitizeValue(double value, MachineChartType chartType)
        {
            if (!IsFinite(value))
            {
                return double.NaN;
            }

            return chartType switch
            {
                MachineChartType.Length when value is < 0 or > 10_000_000 => double.NaN,
                MachineChartType.Diameter when value is < 0 or > 100 => double.NaN,
                MachineChartType.Speed when value is < 0 or > 1_000_000 => double.NaN,
                MachineChartType.Tension when value is < 0 or > 1_000_000 => double.NaN,
                _ => value
            };
        }

        private static SKColor ParseColor(string color)
        {
            var mediaColor = (Color)ColorConverter.ConvertFromString(color)!;
            return new SKColor(mediaColor.R, mediaColor.G, mediaColor.B, mediaColor.A);
        }

        private static DateTime ComposeDateTime(DateTime date, string timeText, DateTime fallback)
        {
            if (TimeSpan.TryParse(timeText, out var span))
            {
                return date.Date.Add(span);
            }

            return fallback;
        }

        private void FillViolationRow(IXLWorksheet sheet, int row, DateTime timestamp, string parameterName, double value, double lower, double upper)
        {
            sheet.Cell(row, 1).Value = timestamp;
            sheet.Cell(row, 1).Style.DateFormat.Format = "yyyy-MM-dd HH:mm:ss";
            sheet.Cell(row, 2).Value = parameterName;
            sheet.Cell(row, 3).Value = value;
            sheet.Cell(row, 4).Value = lower;
            sheet.Cell(row, 5).Value = upper;
        }

        private static ChartSeriesDefinition[] BuildSingleSeriesDefinition(System.Collections.Generic.IReadOnlyList<MachineTrendRecordModel> points, string name, string color, Func<MachineTrendRecordModel, double> selector)
        {
            return
            [
                new ChartSeriesDefinition(
                    name,
                    color,
                    points.Select(p => p.Timestamp).ToArray(),
                    points.Select(selector).ToArray())
            ];
        }

        private static double ConvertRawDiameter(double raw) => raw;

        private static string BuildChartPngPath(string? directory, string suffix)
        {
            var targetDirectory = string.IsNullOrWhiteSpace(directory) ? Path.GetTempPath() : directory;
            return Path.Combine(targetDirectory, $"chart_{suffix}_{Guid.NewGuid():N}.png");
        }

        private static void DeleteFileIfExists(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
            catch
            {
            }
        }

        private void RenderLineChartPng(string filePath, ChartSeriesDefinition[] definitions, DateTime start, DateTime end, string title)
        {
            const int width = 1200;
            const int height = 420;
            const int left = 70;
            const int right = 20;
            const int top = 40;
            const int bottom = 44;
            var chartWidth = width - left - right;
            var chartHeight = height - top - bottom;

            var values = definitions.SelectMany(x => x.Values).Where(IsFinite).ToArray();
            var minY = values.Length > 0 ? values.Min() : 0;
            var maxY = values.Length > 0 ? values.Max() : 1;
            if (Math.Abs(maxY - minY) < 0.0001)
            {
                maxY = minY + 1;
            }

            var minX = start.ToOADate();
            var maxX = end.ToOADate();
            if (maxX <= minX)
            {
                maxX = minX + TimeSpan.FromMinutes(1).TotalDays;
            }

            float MapX(double oa) => (float)(left + ((oa - minX) / (maxX - minX) * chartWidth));
            float MapY(double value) => (float)(top + ((maxY - value) / (maxY - minY) * chartHeight));

            using var bitmap = new SKBitmap(width, height);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.White);
            using var cjkTypeface = SKTypeface.FromFamilyName("Microsoft YaHei")
                                    ?? SKTypeface.FromFamilyName("Microsoft JhengHei")
                                    ?? SKTypeface.Default;

            using var axisPaint = new SKPaint { Color = new SKColor(148, 163, 184), StrokeWidth = 1, IsAntialias = true };
            using var gridPaint = new SKPaint { Color = new SKColor(226, 232, 240), StrokeWidth = 1, IsAntialias = true };
            using var titlePaint = new SKPaint { Color = new SKColor(15, 23, 42), IsAntialias = true };
            using var labelPaint = new SKPaint { Color = new SKColor(71, 85, 105), IsAntialias = true };
            using var legendPaint = new SKPaint { Color = new SKColor(51, 65, 85), IsAntialias = true };
            using var titleFont = new SKFont(cjkTypeface, 20);
            using var labelFont = new SKFont(cjkTypeface, 12);
            using var legendFont = new SKFont(cjkTypeface, 12);

            canvas.DrawText(title, left, 24, SKTextAlign.Left, titleFont, titlePaint);
            canvas.DrawLine(left, top, left, top + chartHeight, axisPaint);
            canvas.DrawLine(left, top + chartHeight, left + chartWidth, top + chartHeight, axisPaint);

            for (var i = 0; i <= 5; i++)
            {
                var y = top + (chartHeight / 5f * i);
                canvas.DrawLine(left, y, left + chartWidth, y, gridPaint);
                var axisValue = maxY - ((maxY - minY) / 5d * i);
                canvas.DrawText(axisValue.ToString("0.#####", CultureInfo.InvariantCulture), 8, y + 4, SKTextAlign.Left, labelFont, labelPaint);
            }

            canvas.DrawText("Value", 8, top - 8, SKTextAlign.Left, labelFont, labelPaint);

            foreach (var definition in definitions)
            {
                using var seriesPaint = new SKPaint
                {
                    Color = ParseColor(definition.ColorHex),
                    StrokeWidth = 2,
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke
                };

                using var path = new SKPath();
                var started = false;
                for (var i = 0; i < definition.Timestamps.Length && i < definition.Values.Length; i++)
                {
                    var value = definition.Values[i];
                    if (!IsFinite(value))
                    {
                        continue;
                    }

                    var x = MapX(definition.Timestamps[i].ToOADate());
                    var y = MapY(value);
                    if (!started)
                    {
                        path.MoveTo(x, y);
                        started = true;
                    }
                    else
                    {
                        path.LineTo(x, y);
                    }
                }

                if (started)
                {
                    canvas.DrawPath(path, seriesPaint);
                }
            }

            var legendX = left + chartWidth - 260;
            var legendY = top - 4f;
            for (var i = 0; i < definitions.Length; i++)
            {
                var item = definitions[i];
                var y = legendY + (i * 18f);
                if (y > top + chartHeight - 10)
                {
                    break;
                }

                using var itemPaint = new SKPaint
                {
                    Color = ParseColor(item.ColorHex),
                    StrokeWidth = 2,
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke
                };
                canvas.DrawLine(legendX, y, legendX + 18, y, itemPaint);
                canvas.DrawText(item.Name, legendX + 24, y + 4, SKTextAlign.Left, legendFont, legendPaint);
            }

            canvas.DrawText($"{start:yyyy-MM-dd HH:mm:ss} ~ {end:yyyy-MM-dd HH:mm:ss}", left, height - 12, SKTextAlign.Left, labelFont, labelPaint);

            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var file = File.Create(filePath);
            data.SaveTo(file);
        }

        private sealed record ChartSeriesDefinition(string Name, string ColorHex, DateTime[] Timestamps, double[] Values);
    }
}
