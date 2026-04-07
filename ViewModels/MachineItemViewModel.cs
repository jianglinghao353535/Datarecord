using System;
using System.Collections.ObjectModel;
using System.Linq;
using Datarecord.Models;

namespace Datarecord.ViewModels
{
    public sealed class MachineItemViewModel : ViewModelBase
    {
        private Guid _id;
        private string _name;
        private string _ipAddress;
        private PlcType _plcType;
        private int _port;
        private int _sampleIntervalMs;
        private double _x;
        private double _y;
        private bool _isEnabled;
        private bool _isSelected;
        private bool _isPlcSynchronizing;
        private bool _isManualReconnectInProgress;
        private string _plcStatusText = "等待啟動後自動連線 PLC，連線成功後會自動開始變數歸檔。";
        private double _productionSpeed;
        private double _productionLength;
        private double _productionWeight;
        private string _productionStatus;
        private double _currentDiameter;
        private double[] _currentTemperatures;
        private string _plcAddressProductionSpeed;
        private string _plcAddressProductionLength;
        private string _plcAddressProductionWeight;
        private string _plcAddressProductionStatus;
        private string _plcAddressDiameter;
        private string[] _plcAddressTemperatureZones;

        public MachineItemViewModel(MachineItemModel model)
        {
            _id = model.Id == Guid.Empty ? Guid.NewGuid() : model.Id;
            _name = model.Name;
            _ipAddress = model.IpAddress;
            _plcType = model.PlcType;
            _port = model.Port;
            _sampleIntervalMs = model.SampleIntervalMs;
            _x = model.X;
            _y = model.Y;
            _isEnabled = model.IsEnabled;
            _productionSpeed = model.ProductionSpeed;
            _productionLength = model.ProductionLength;
            _productionWeight = model.ProductionWeight;
            _productionStatus = string.IsNullOrWhiteSpace(model.ProductionStatus) ? "待機" : model.ProductionStatus;
            _currentDiameter = model.CurrentDiameter;
            _currentTemperatures = NormalizeTemperatures(model.CurrentTemperatures);

            _plcAddressProductionSpeed = model.PlcAddressProductionSpeed;
            _plcAddressProductionLength = model.PlcAddressProductionLength;
            _plcAddressProductionWeight = model.PlcAddressProductionWeight;
            _plcAddressProductionStatus = model.PlcAddressProductionStatus;
            _plcAddressDiameter = model.PlcAddressDiameter;
            _plcAddressTemperatureZones = NormalizeAddressArray(model.PlcAddressTemperatureZones);

            ApplyAddressTemplateForPlcType(force: false);
            ApplyDefaultPortForPlcType(force: _port <= 0);

            PlcTemperatureAddresses = new ObservableCollection<PlcTemperatureAddressViewModel>(
                Enumerable.Range(0, 8)
                    .Select(i => new PlcTemperatureAddressViewModel(i, _plcAddressTemperatureZones[i], OnTemperatureAddressChanged)));

            TrendRecords = new ObservableCollection<MachineTrendRecordModel>(
                model.TrendRecords?.Where(x => x is not null) ?? Enumerable.Empty<MachineTrendRecordModel>());
        }

        public Guid Id
        {
            get => _id;
            set => SetProperty(ref _id, value);
        }

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public string IpAddress
        {
            get => _ipAddress;
            set => SetProperty(ref _ipAddress, value);
        }

        public PlcType PlcType
        {
            get => _plcType;
            set
            {
                if (SetProperty(ref _plcType, value))
                {
                    ApplyAddressTemplateForPlcType(force: true);
                    ApplyDefaultPortForPlcType(force: true);
                    OnPropertyChanged(nameof(PlcTypeText));
                    OnPropertyChanged(nameof(PlcAddressHint));
                }
            }
        }

        public int Port
        {
            get => _port;
            set => SetProperty(ref _port, value);
        }

        public bool IsPlcSynchronizing
        {
            get => _isPlcSynchronizing;
            set => SetProperty(ref _isPlcSynchronizing, value);
        }

        public bool IsManualReconnectInProgress
        {
            get => _isManualReconnectInProgress;
            set => SetProperty(ref _isManualReconnectInProgress, value);
        }

        public string PlcStatusText
        {
            get => _plcStatusText;
            set => SetProperty(ref _plcStatusText, value);
        }

        public int SampleIntervalMs
        {
            get => _sampleIntervalMs;
            set
            {
                if (SetProperty(ref _sampleIntervalMs, value))
                {
                    OnPropertyChanged(nameof(SampleText));
                }
            }
        }

        public double X
        {
            get => _x;
            set => SetProperty(ref _x, value);
        }

        public double Y
        {
            get => _y;
            set => SetProperty(ref _y, value);
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (SetProperty(ref _isEnabled, value))
                {
                    OnPropertyChanged(nameof(EnabledText));
                }
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public double ProductionSpeed
        {
            get => _productionSpeed;
            set => SetProperty(ref _productionSpeed, value);
        }

        public double ProductionLength
        {
            get => _productionLength;
            set => SetProperty(ref _productionLength, value);
        }

        public double ProductionWeight
        {
            get => _productionWeight;
            set => SetProperty(ref _productionWeight, value);
        }

        public string ProductionStatus
        {
            get => _productionStatus;
            set => SetProperty(ref _productionStatus, value);
        }

        public double CurrentDiameter
        {
            get => _currentDiameter;
            set => SetProperty(ref _currentDiameter, value);
        }

        public string PlcAddressProductionSpeed
        {
            get => _plcAddressProductionSpeed;
            set => SetProperty(ref _plcAddressProductionSpeed, value);
        }

        public string PlcAddressProductionLength
        {
            get => _plcAddressProductionLength;
            set => SetProperty(ref _plcAddressProductionLength, value);
        }

        public string PlcAddressProductionWeight
        {
            get => _plcAddressProductionWeight;
            set => SetProperty(ref _plcAddressProductionWeight, value);
        }

        public string PlcAddressProductionStatus
        {
            get => _plcAddressProductionStatus;
            set => SetProperty(ref _plcAddressProductionStatus, value);
        }

        public string PlcAddressDiameter
        {
            get => _plcAddressDiameter;
            set => SetProperty(ref _plcAddressDiameter, value);
        }

        public double[] CurrentTemperatures
        {
            get => _currentTemperatures;
            set => SetProperty(ref _currentTemperatures, NormalizeTemperatures(value));
        }

        public ObservableCollection<PlcTemperatureAddressViewModel> PlcTemperatureAddresses { get; }

        public ObservableCollection<MachineTrendRecordModel> TrendRecords { get; }

        public string PlcTypeText => PlcType switch
        {
            PlcType.SiemensS7 => "西門子 S7",
            PlcType.DeltaModbusTcp => "台達 Modbus TCP",
            _ => PlcType.ToString()
        };

        public string PlcAddressHint => PlcType switch
        {
            PlcType.SiemensS7 => "地址格式：MD100 / MW112 / M0.0；V 記憶體可輸入 VD100 / VW112 / VB200 / V100.0（會轉為 DB0 對應位址）",
            PlcType.DeltaModbusTcp => "建議格式：D100 / M0（台達常用 D 暫存器與 M 位元）",
            _ => "請依 PLC 驅動支援格式填寫"
        };

        public string SampleText => $"採樣週期：{SampleIntervalMs} ms";

        public string EnabledText => IsEnabled ? "運行中" : "已停機";

        public MachineItemModel ToModel()
        {
            return new MachineItemModel
            {
                Id = Id,
                Name = Name,
                IpAddress = IpAddress,
                PlcType = PlcType,
                Port = Port,
                SampleIntervalMs = SampleIntervalMs,
                X = X,
                Y = Y,
                IsEnabled = IsEnabled,
                ProductionSpeed = ProductionSpeed,
                ProductionLength = ProductionLength,
                ProductionWeight = ProductionWeight,
                ProductionStatus = ProductionStatus,
                CurrentDiameter = CurrentDiameter,
                PlcAddressProductionSpeed = PlcAddressProductionSpeed,
                PlcAddressProductionLength = PlcAddressProductionLength,
                PlcAddressProductionWeight = PlcAddressProductionWeight,
                PlcAddressProductionStatus = PlcAddressProductionStatus,
                PlcAddressDiameter = PlcAddressDiameter,
                PlcAddressTemperatureZones = PlcTemperatureAddresses.Select(x => x.Address).ToArray(),
                CurrentTemperatures = NormalizeTemperatures(CurrentTemperatures),
                TrendRecords = TrendRecords.ToList()
            };
        }

        public void ApplySnapshot(PlcRealtimeSnapshotModel snapshot)
        {
            ProductionSpeed = snapshot.ProductionSpeed;
            ProductionLength = snapshot.ProductionLength;
            ProductionWeight = snapshot.ProductionWeight;
            ProductionStatus = snapshot.ProductionStatus;
            CurrentDiameter = snapshot.CurrentDiameter;
            CurrentTemperatures = NormalizeTemperatures(snapshot.Temperatures);
        }

        public void AddTrendRecord(DateTime timestamp)
        {
            TrendRecords.Add(new MachineTrendRecordModel
            {
                Timestamp = timestamp,
                Speed = ProductionSpeed,
                Diameter = CurrentDiameter,
                TemperatureZones = NormalizeTemperatures(CurrentTemperatures)
            });
        }

        private void OnTemperatureAddressChanged(int index, string address)
        {
            _plcAddressTemperatureZones[index] = address;
        }

        private void ApplyAddressTemplateForPlcType(bool force)
        {
            switch (PlcType)
            {
                case PlcType.SiemensS7:
                    AssignIfNeeded(ref _plcAddressProductionSpeed, "MD100", force);
                    AssignIfNeeded(ref _plcAddressProductionLength, "MD104", force);
                    AssignIfNeeded(ref _plcAddressProductionWeight, "MD108", force);
                    AssignIfNeeded(ref _plcAddressProductionStatus, "MD112", force);
                    AssignIfNeeded(ref _plcAddressDiameter, "MD116", force);
                    for (var i = 0; i < 8; i++)
                    {
                        AssignTempIfNeeded(i, $"MD{200 + (i * 4)}", force);
                    }
                    break;
                case PlcType.DeltaModbusTcp:
                    AssignIfNeeded(ref _plcAddressProductionSpeed, "D100", force);
                    AssignIfNeeded(ref _plcAddressProductionLength, "D102", force);
                    AssignIfNeeded(ref _plcAddressProductionWeight, "D104", force);
                    AssignIfNeeded(ref _plcAddressProductionStatus, "D106", force);
                    AssignIfNeeded(ref _plcAddressDiameter, "D108", force);
                    for (var i = 0; i < 8; i++)
                    {
                        AssignTempIfNeeded(i, $"D{200 + (i * 2)}", force);
                    }
                    break;
            }

            if (PlcTemperatureAddresses is null)
            {
                return;
            }

            for (var i = 0; i < PlcTemperatureAddresses.Count; i++)
            {
                PlcTemperatureAddresses[i].Address = _plcAddressTemperatureZones[i];
            }
        }

        private void ApplyDefaultPortForPlcType(bool force)
        {
            var defaultPort = PlcType switch
            {
                PlcType.SiemensS7 => 102,
                PlcType.DeltaModbusTcp => 502,
                _ => 0
            };

            if (force || _port <= 0)
            {
                Port = defaultPort;
            }
        }

        private void AssignTempIfNeeded(int index, string value, bool force)
        {
            if (force || string.IsNullOrWhiteSpace(_plcAddressTemperatureZones[index]))
            {
                _plcAddressTemperatureZones[index] = value;
            }
        }

        private static void AssignIfNeeded(ref string target, string value, bool force)
        {
            if (force || string.IsNullOrWhiteSpace(target))
            {
                target = value;
            }
        }

        private static double[] NormalizeTemperatures(double[]? source)
        {
            var values = new double[8];
            if (source is null)
            {
                return values;
            }

            for (var i = 0; i < values.Length && i < source.Length; i++)
            {
                values[i] = source[i];
            }

            return values;
        }

        private static string[] NormalizeAddressArray(string[]? source)
        {
            var values = new string[8];
            if (source is null)
            {
                return values;
            }

            for (var i = 0; i < values.Length && i < source.Length; i++)
            {
                values[i] = source[i] ?? string.Empty;
            }

            return values;
        }
    }

    public sealed class PlcTemperatureAddressViewModel : ViewModelBase
    {
        private readonly Action<int, string> _onAddressChanged;
        private string _address;

        public PlcTemperatureAddressViewModel(int zoneIndex, string address, Action<int, string> onAddressChanged)
        {
            ZoneIndex = zoneIndex;
            _address = address;
            _onAddressChanged = onAddressChanged;
        }

        public int ZoneIndex { get; }

        public string ZoneName => $"溫區 {ZoneIndex + 1}";

        public string Address
        {
            get => _address;
            set
            {
                if (!SetProperty(ref _address, value))
                {
                    return;
                }

                _onAddressChanged(ZoneIndex, value);
            }
        }
    }
}