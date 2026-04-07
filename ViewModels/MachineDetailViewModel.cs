using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using System.Windows.Media;
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
        Temperature,
        Diameter,
        Speed
    }

    public sealed class MachineDetailViewModel : ViewModelBase, IDisposable
    {
        private const int PreferredXAxisLabelCount = 10;
        private static readonly TimeSpan ChartAnimationDuration = TimeSpan.FromMilliseconds(350);
        private static readonly TimeSpan LiveWindowDuration = TimeSpan.FromMinutes(5);
        private static readonly string[] TemperatureColors =
        [
            "#EF4444", "#F97316", "#EAB308", "#84CC16",
            "#06B6D4", "#3B82F6", "#8B5CF6", "#EC4899"
        ];

        private readonly MachineItemViewModel _machine;
        private readonly IMachineMonitoringService _machineMonitoringService;
        private readonly RelayCommand _refreshFromPlcCommand;
        private readonly RelayCommand _returnToLiveChartCommand;
        private DateTime _startDate;
        private DateTime _endDate;
        private string _selectedStartTime;
        private string _selectedEndTime;
        private MachineChartType _selectedChartType;
        private string _chartSummary = "暫無資料";
        private bool _useManualYAxis;
        private double _manualYAxisMin;
        private double _manualYAxisMax = 300;
        private bool _autoScaleWhenIdle = true;
        private bool _isChartMouseMoving;
        private bool _isLiveChartMode = true;
        private double _currentAxisMin;
        private double _currentAxisMax = 100;
        private ISeries[] _liveSeries = [];
        private Axis[] _liveXAxes = [];
        private Axis[] _liveYAxes = [];

        public MachineDetailViewModel(MachineItemViewModel machine, IMachineMonitoringService machineMonitoringService)
        {
            _machine = machine;
            _machineMonitoringService = machineMonitoringService;
            _machine.PropertyChanged += MachineOnPropertyChanged;
            _machine.TrendRecords.CollectionChanged += TrendRecordsOnCollectionChanged;

            var now = DateTime.Now;
            _startDate = now.Add(-LiveWindowDuration).Date;
            _endDate = now.Date;

            TimeOptions = new ObservableCollection<string>(
                Enumerable.Range(0, 24 * 60)
                    .Select(i => TimeSpan.FromMinutes(i).ToString(@"hh\:mm\:ss")));

            _selectedStartTime = now.Add(-LiveWindowDuration).ToString("HH:mm:ss");
            _selectedEndTime = now.ToString("HH:mm:ss");

            if (!TimeOptions.Contains(_selectedStartTime))
            {
                _selectedStartTime = "00:00:00";
            }

            if (!TimeOptions.Contains(_selectedEndTime))
            {
                _selectedEndTime = "23:59:00";
            }

            TemperatureZones = new ObservableCollection<TemperatureZoneViewModel>(
                Enumerable.Range(0, 8)
                    .Select(i => new TemperatureZoneViewModel(
                        i,
                        machine.CurrentTemperatures.ElementAtOrDefault(i),
                        OnZoneValueChanged,
                        RefreshChart)));

            ChartTypes = Enum.GetValues<MachineChartType>();

            _refreshFromPlcCommand = new RelayCommand(
                () => _ = ReconnectAsync(),
                () => _machine.IsEnabled && !_machine.IsManualReconnectInProgress);
            _returnToLiveChartCommand = new RelayCommand(ReturnToLiveChart);

            RefreshFromPlcCommand = _refreshFromPlcCommand;
            RefreshChartCommand = new RelayCommand(ExecuteManualQuery);
            RecordSampleCommand = new RelayCommand(RecordSample);
            ReturnToLiveChartCommand = _returnToLiveChartCommand;

            SelectedChartType = MachineChartType.Temperature;
            RefreshChart();
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

        public string ProductionStatus
        {
            get => _machine.ProductionStatus;
            set
            {
                if (_machine.ProductionStatus == value)
                {
                    return;
                }

                _machine.ProductionStatus = value;
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

        public string PlcAddressProductionStatus
        {
            get => _machine.PlcAddressProductionStatus;
            set
            {
                if (_machine.PlcAddressProductionStatus == value)
                {
                    return;
                }

                _machine.PlcAddressProductionStatus = value;
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
                    NormalizeQueryRange(changedStartBoundary: false);
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
                    RefreshChart();
                }
            }
        }

        public double ManualYAxisMin
        {
            get => _manualYAxisMin;
            set
            {
                if (SetProperty(ref _manualYAxisMin, value) && UseManualYAxis)
                {
                    RefreshChart();
                }
            }
        }

        public double ManualYAxisMax
        {
            get => _manualYAxisMax;
            set
            {
                if (SetProperty(ref _manualYAxisMax, value) && UseManualYAxis)
                {
                    RefreshChart();
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

        public MachineChartType SelectedChartType
        {
            get => _selectedChartType;
            set
            {
                if (!SetProperty(ref _selectedChartType, value))
                {
                    return;
                }

                OnPropertyChanged(nameof(IsTemperatureChart));
                RefreshChart();
            }
        }

        public bool IsTemperatureChart => SelectedChartType == MachineChartType.Temperature;

        public MachineChartType[] ChartTypes { get; }

        public ObservableCollection<string> TimeOptions { get; }

        public ObservableCollection<PlcTemperatureAddressViewModel> PlcTemperatureAddresses => _machine.PlcTemperatureAddresses;

        public ObservableCollection<TemperatureZoneViewModel> TemperatureZones { get; }

        public ICommand RefreshFromPlcCommand { get; }

        public ICommand RefreshChartCommand { get; }

        public ICommand RecordSampleCommand { get; }

        public ICommand ReturnToLiveChartCommand { get; }

        public string ChartSummary
        {
            get => _chartSummary;
            private set => SetProperty(ref _chartSummary, value);
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

        public ObservableCollection<TemperatureLegendItemViewModel> TemperatureLegendItems { get; } =
        [
            new("溫區 1", "#EF4444", "M4,0 L8,4 4,8 0,4 Z"),
            new("溫區 2", "#F97316", "M4,0 L8,8 0,8 Z"),
            new("溫區 3", "#EAB308", "M4,0 L5.2,2.8 8,3 6,4.8 6.6,8 4,6.2 1.4,8 2,4.8 0,3 2.8,2.8 Z"),
            new("溫區 4", "#84CC16", "M4,0 L8,4 4,8 0,4 Z"),
            new("溫區 5", "#06B6D4", "M4,0 L8,8 0,8 Z"),
            new("溫區 6", "#3B82F6", "M4,0 L5.2,2.8 8,3 6,4.8 6.6,8 4,6.2 1.4,8 2,4.8 0,3 2.8,2.8 Z"),
            new("溫區 7", "#8B5CF6", "M4,0 L8,4 4,8 0,4 Z"),
            new("溫區 8", "#EC4899", "M4,0 L8,8 0,8 Z")
        ];

        public void Dispose()
        {
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

        private void OnZoneValueChanged(int zoneIndex, double value)
        {
            _machine.CurrentTemperatures[zoneIndex] = value;
        }

        private async System.Threading.Tasks.Task ReconnectAsync()
        {
            await _machineMonitoringService.ReconnectAsync(_machine);
        }

        private void RecordSample()
        {
            _machine.AddTrendRecord(DateTime.Now);
            ChartSummary = $"已記錄 {_machine.TrendRecords.Count} 筆資料";
            RefreshChart();
        }

        private void ExecuteManualQuery()
        {
            _isLiveChartMode = false;
            RefreshChart();
        }

        private void ReturnToLiveChart()
        {
            _isLiveChartMode = true;
            SetLiveWindow(DateTime.Now);
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
                case nameof(MachineItemViewModel.ProductionStatus):
                    OnPropertyChanged(nameof(ProductionStatus));
                    break;
                case nameof(MachineItemViewModel.CurrentDiameter):
                    OnPropertyChanged(nameof(CurrentDiameter));
                    break;
                case nameof(MachineItemViewModel.CurrentTemperatures):
                    SyncTemperatureZonesFromMachine();
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
                or nameof(MachineItemViewModel.CurrentDiameter)
                or nameof(MachineItemViewModel.CurrentTemperatures))
            {
                RefreshChart();
            }
        }

        private void TrendRecordsOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems is { Count: > 0 })
            {
                var lastRecord = e.NewItems[e.NewItems.Count - 1] as MachineTrendRecordModel;
                if (lastRecord is not null && _isLiveChartMode)
                {
                    SetLiveWindow(lastRecord.Timestamp);
                }
            }

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

            OnPropertyChanged(nameof(StartDate));
            OnPropertyChanged(nameof(EndDate));
            OnPropertyChanged(nameof(SelectedStartTime));
            OnPropertyChanged(nameof(SelectedEndTime));
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
                OnPropertyChanged(nameof(EndDate));
                OnPropertyChanged(nameof(SelectedEndTime));
            }
            else
            {
                _startDate = end.Date;
                _selectedStartTime = end.ToString("HH:mm:ss");
                OnPropertyChanged(nameof(StartDate));
                OnPropertyChanged(nameof(SelectedStartTime));
            }
        }

        private void SyncTemperatureZonesFromMachine()
        {
            for (var i = 0; i < TemperatureZones.Count && i < _machine.CurrentTemperatures.Length; i++)
            {
                TemperatureZones[i].Value = _machine.CurrentTemperatures[i];
            }
        }

        private void RefreshChart()
        {
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

            var definitions = BuildSeriesDefinitions(points);
            var values = definitions.SelectMany(x => x.Values).Where(IsFinite).ToArray();
            var (defaultMin, defaultMax) = GetDefaultAxisRange();
            var (axisMin, axisMax) = ResolveAxisRange(values, defaultMin, defaultMax);

            LiveSeries = definitions
                .Select((definition, index) => BuildLineSeries(definition, index))
                .ToArray<ISeries>();
            LiveXAxes = BuildXAxes(start, end);
            LiveYAxes = BuildYAxes(axisMin, axisMax);

            ChartSummary = points.Count == 0
                ? "目前時間區段沒有資料"
                : $"時間區段內資料點：{points.Count}，Y軸：{axisMin:0.##} ~ {axisMax:0.##}，開始：{start:yyyy-MM-dd HH:mm:ss}，結束：{end:yyyy-MM-dd HH:mm:ss}";
        }

        private ChartSeriesDefinition[] BuildSeriesDefinitions(System.Collections.Generic.IReadOnlyList<MachineTrendRecordModel> points)
        {
            if (points.Count == 0)
            {
                return [];
            }

            return SelectedChartType switch
            {
                MachineChartType.Temperature => TemperatureZones
                    .Where(z => z.IsVisible)
                    .Select(z => new ChartSeriesDefinition(
                        z.Name,
                        TemperatureColors[z.ZoneIndex],
                        points.Select(p => p.Timestamp).ToArray(),
                        points.Select(p => SanitizeValue(p.TemperatureZones.ElementAtOrDefault(z.ZoneIndex), SelectedChartType)).ToArray()))
                    .Where(x => x.Values.Any(IsFinite))
                    .ToArray(),
                MachineChartType.Diameter =>
                [
                    new ChartSeriesDefinition(
                        "線徑",
                        "#7C3AED",
                        points.Select(p => p.Timestamp).ToArray(),
                        points.Select(p => SanitizeValue(p.Diameter, SelectedChartType)).ToArray())
                ],
                MachineChartType.Speed =>
                [
                    new ChartSeriesDefinition(
                        "速度",
                        "#0EA5E9",
                        points.Select(p => p.Timestamp).ToArray(),
                        points.Select(p => SanitizeValue(p.Speed, SelectedChartType)).ToArray())
                ],
                _ => []
            };
        }

        private LineSeries<ObservablePoint> BuildLineSeries(ChartSeriesDefinition definition, int index)
        {
            var color = ParseColor(definition.ColorHex);
            var animationSpeed = SelectedChartType == MachineChartType.Temperature
                ? ChartAnimationDuration
                : TimeSpan.Zero;

            return new LineSeries<ObservablePoint>
            {
                Name = definition.Name,
                Values = definition.Timestamps
                    .Zip(definition.Values, (timestamp, value) => new ObservablePoint(timestamp.ToOADate(), value))
                    .ToArray(),
                Fill = null,
                LineSmoothness = 0,
                GeometrySize = 0,
                AnimationsSpeed = animationSpeed,
                Stroke = new SolidColorPaint(color, 3),
                GeometryFill = null,
                GeometryStroke = null
            };
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
                    Labeler = value => DateTime.FromOADate(value).ToString("HH:mm:ss"),
                    LabelsPaint = new SolidColorPaint(new SKColor(148, 163, 184)),
                    NamePaint = new SolidColorPaint(new SKColor(148, 163, 184)),
                    SeparatorsPaint = new SolidColorPaint(new SKColor(226, 232, 240)) { StrokeThickness = 1 }
                }
            ];
        }

        private static int[] BuildMarkerIndexes(int pointCount)
        {
            if (pointCount <= 0)
            {
                return [];
            }

            if (pointCount <= PreferredXAxisLabelCount)
            {
                return Enumerable.Range(0, pointCount).ToArray();
            }

            return Enumerable.Range(0, PreferredXAxisLabelCount)
                .Select(i => (int)Math.Round(i * (pointCount - 1d) / (PreferredXAxisLabelCount - 1)))
                .Distinct()
                .ToArray();
        }

        private Axis[] BuildYAxes(double min, double max)
        {
            return
            [
                new Axis
                {
                    Name = SelectedChartType switch
                    {
                        MachineChartType.Diameter => "Diameter",
                        MachineChartType.Speed => "Speed",
                        _ => "Temperature"
                    },
                    MinLimit = min,
                    MaxLimit = max,
                    TextSize = 12,
                    Labeler = value => value.ToString("0.##"),
                    LabelsPaint = new SolidColorPaint(new SKColor(148, 163, 184)),
                    NamePaint = new SolidColorPaint(new SKColor(148, 163, 184)),
                    SeparatorsPaint = new SolidColorPaint(new SKColor(226, 232, 240)) { StrokeThickness = 1 }
                }
            ];
        }

        private (double Min, double Max) GetDefaultAxisRange()
        {
            return SelectedChartType switch
            {
                MachineChartType.Temperature => (0, 300),
                MachineChartType.Diameter => (0, 5),
                MachineChartType.Speed => (0, 2000),
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
                        MachineChartType.Temperature => 5d,
                        MachineChartType.Diameter => 0.2d,
                        MachineChartType.Speed => 50d,
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
                MachineChartType.Temperature when value is < -50 or > 500 => double.NaN,
                MachineChartType.Diameter when value is < 0 or > 100 => double.NaN,
                MachineChartType.Speed when value is < 0 or > 1_000_000 => double.NaN,
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

        private sealed record ChartSeriesDefinition(string Name, string ColorHex, DateTime[] Timestamps, double[] Values);
    }

    public sealed class TemperatureZoneViewModel : ViewModelBase
    {
        private readonly Action<int, double> _onValueChanged;
        private readonly Action _onVisibilityChanged;
        private double _value;
        private bool _isVisible = true;

        public TemperatureZoneViewModel(int zoneIndex, double value, Action<int, double> onValueChanged, Action onVisibilityChanged)
        {
            ZoneIndex = zoneIndex;
            _value = value;
            _onValueChanged = onValueChanged;
            _onVisibilityChanged = onVisibilityChanged;
        }

        public int ZoneIndex { get; }

        public string Name => $"溫區 {ZoneIndex + 1}";

        public double Value
        {
            get => _value;
            set
            {
                if (!SetProperty(ref _value, value))
                {
                    return;
                }

                _onValueChanged(ZoneIndex, value);
            }
        }

        public bool IsVisible
        {
            get => _isVisible;
            set
            {
                if (!SetProperty(ref _isVisible, value))
                {
                    return;
                }

                _onVisibilityChanged();
            }
        }
    }

    public sealed class TemperatureLegendItemViewModel
    {
        public TemperatureLegendItemViewModel(string name, string color, string markerGeometry)
        {
            Name = name;
            Stroke = (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
            MarkerGeometry = Geometry.Parse(markerGeometry);
        }

        public string Name { get; }

        public Brush Stroke { get; }

        public Geometry MarkerGeometry { get; }
    }
}
