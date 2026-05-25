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
        private string _plcStatusText = "Waiting to auto-connect to PLC after startup. Archiving starts automatically after a successful connection.";
        private double _productionSpeed;
        private double _productionLength;
        private double _productionWeight;
        private double _currentDiameter;
        private bool _useManualYAxis;
        private double _manualYAxisMin;
        private double _manualYAxisMax = 300;
        private double _lengthYAxisMin;
        private double _lengthYAxisMax = 10000;
        private double _diameterYAxisMin;
        private double _diameterYAxisMax = 5;
        private double _speedYAxisMin;
        private double _speedYAxisMax = 2000;
        private double _tensionYAxisMin;
        private double _tensionYAxisMax = 200;
        private string _plcAddressProductionSpeed;
        private string _plcAddressProductionLength;
        private string _plcAddressProductionWeight;
        private string _plcAddressWeight;
        private string _plcAddressDiameter;
        private string _plcAddressRuningSignal;

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
            _currentDiameter = model.CurrentDiameter;
            _useManualYAxis = model.UseManualYAxis;
            _manualYAxisMin = model.ManualYAxisMin;
            _manualYAxisMax = model.ManualYAxisMax;
            _lengthYAxisMin = model.LengthYAxisMin;
            _lengthYAxisMax = model.LengthYAxisMax;
            _diameterYAxisMin = model.DiameterYAxisMin;
            _diameterYAxisMax = model.DiameterYAxisMax;
            _speedYAxisMin = model.SpeedYAxisMin;
            _speedYAxisMax = model.SpeedYAxisMax;
            _tensionYAxisMin = model.TensionYAxisMin;
            _tensionYAxisMax = model.TensionYAxisMax;

            _plcAddressProductionSpeed = model.PlcAddressProductionSpeed;
            _plcAddressProductionLength = model.PlcAddressProductionLength;
            _plcAddressProductionWeight = model.PlcAddressProductionWeight;
            _plcAddressWeight = string.IsNullOrWhiteSpace(model.PlcAddressWeight)
                ? model.PlcAddressProductionWeight
                : model.PlcAddressWeight;
            _plcAddressDiameter = model.PlcAddressDiameter;
            _plcAddressRuningSignal = model.PlcAddressRuningSignal;

            ApplyAddressTemplateForPlcType(force: false);
            ApplyDefaultPortForPlcType(force: _port <= 0);

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

        public double CurrentDiameter
        {
            get => _currentDiameter;
            set => SetProperty(ref _currentDiameter, value);
        }

        public bool UseManualYAxis
        {
            get => _useManualYAxis;
            set => SetProperty(ref _useManualYAxis, value);
        }

        public double ManualYAxisMin
        {
            get => _manualYAxisMin;
            set => SetProperty(ref _manualYAxisMin, value);
        }

        public double ManualYAxisMax
        {
            get => _manualYAxisMax;
            set => SetProperty(ref _manualYAxisMax, value);
        }

        public double LengthYAxisMin
        {
            get => _lengthYAxisMin;
            set => SetProperty(ref _lengthYAxisMin, value);
        }

        public double LengthYAxisMax
        {
            get => _lengthYAxisMax;
            set => SetProperty(ref _lengthYAxisMax, value);
        }

        public double DiameterYAxisMin
        {
            get => _diameterYAxisMin;
            set => SetProperty(ref _diameterYAxisMin, value);
        }

        public double DiameterYAxisMax
        {
            get => _diameterYAxisMax;
            set => SetProperty(ref _diameterYAxisMax, value);
        }

        public double SpeedYAxisMin
        {
            get => _speedYAxisMin;
            set => SetProperty(ref _speedYAxisMin, value);
        }

        public double SpeedYAxisMax
        {
            get => _speedYAxisMax;
            set => SetProperty(ref _speedYAxisMax, value);
        }

        public double TensionYAxisMin
        {
            get => _tensionYAxisMin;
            set => SetProperty(ref _tensionYAxisMin, value);
        }

        public double TensionYAxisMax
        {
            get => _tensionYAxisMax;
            set => SetProperty(ref _tensionYAxisMax, value);
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

        public string PlcAddressWeight
        {
            get => _plcAddressWeight;
            set => SetProperty(ref _plcAddressWeight, value);
        }

        public string PlcAddressDiameter
        {
            get => _plcAddressDiameter;
            set => SetProperty(ref _plcAddressDiameter, value);
        }

        public string PlcAddressRuningSignal
        {
            get => _plcAddressRuningSignal;
            set => SetProperty(ref _plcAddressRuningSignal, value);
        }

        public ObservableCollection<MachineTrendRecordModel> TrendRecords { get; }

        public string PlcTypeText => PlcType switch
        {
            PlcType.SiemensS7 => "Siemens S7",
            PlcType.DeltaModbusTcp => "Delta Modbus TCP",
            _ => PlcType.ToString()
        };

        public string PlcAddressHint => PlcType switch
        {
            PlcType.SiemensS7 => "Address format: MD100 / MW112 / M0.0; V memory supports VD100 / VW112 / VB200 / V100.0 (auto-mapped to DB address)",
            PlcType.DeltaModbusTcp => "Recommended format: D100 / M0 (common Delta registers and bits)",
            _ => "Please use an address format supported by your PLC driver"
        };

        public string SampleText => $"Sample interval: {SampleIntervalMs} ms";

        public string EnabledText => IsEnabled ? "Running" : "Stopped";

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
                CurrentDiameter = CurrentDiameter,
                UseManualYAxis = UseManualYAxis,
                ManualYAxisMin = ManualYAxisMin,
                ManualYAxisMax = ManualYAxisMax,
                LengthYAxisMin = LengthYAxisMin,
                LengthYAxisMax = LengthYAxisMax,
                DiameterYAxisMin = DiameterYAxisMin,
                DiameterYAxisMax = DiameterYAxisMax,
                SpeedYAxisMin = SpeedYAxisMin,
                SpeedYAxisMax = SpeedYAxisMax,
                TensionYAxisMin = TensionYAxisMin,
                TensionYAxisMax = TensionYAxisMax,
                PlcAddressProductionSpeed = PlcAddressProductionSpeed,
                PlcAddressProductionLength = PlcAddressProductionLength,
                PlcAddressProductionWeight = PlcAddressProductionWeight,
                PlcAddressWeight = PlcAddressWeight,
                PlcAddressDiameter = PlcAddressDiameter,
                PlcAddressRuningSignal = PlcAddressRuningSignal,
                TrendRecords = TrendRecords.ToList()
            };
        }

        public void ApplySnapshot(PlcRealtimeSnapshotModel snapshot)
        {
            ProductionSpeed = snapshot.ProductionSpeed;
            ProductionLength = snapshot.ProductionLength;
            ProductionWeight = snapshot.ProductionWeight;
            CurrentDiameter = snapshot.CurrentDiameter;
        }

        public void AddTrendRecord(DateTime timestamp)
        {
            TrendRecords.Add(new MachineTrendRecordModel
            {
                Timestamp = timestamp,
                Speed = ProductionSpeed,
                Length = ProductionLength,
                Diameter = CurrentDiameter,
                Tension = ProductionWeight
            });
        }

        private void ApplyAddressTemplateForPlcType(bool force)
        {
            switch (PlcType)
            {
                case PlcType.SiemensS7:
                    AssignIfNeeded(ref _plcAddressProductionSpeed, "MD100", force);
                    AssignIfNeeded(ref _plcAddressProductionLength, "MD104", force);
                    AssignIfNeeded(ref _plcAddressProductionWeight, "MD108", force);
                    AssignIfNeeded(ref _plcAddressWeight, "MD108", force);
                    AssignIfNeeded(ref _plcAddressDiameter, "MD112", force);
                    AssignIfNeeded(ref _plcAddressRuningSignal, "M0.0", force);
                    break;
                case PlcType.DeltaModbusTcp:
                case PlcType.InovanceModbusTcp:
                    AssignIfNeeded(ref _plcAddressProductionSpeed, "D100", force);
                    AssignIfNeeded(ref _plcAddressProductionLength, "D102", force);
                    AssignIfNeeded(ref _plcAddressProductionWeight, "D104", force);
                    AssignIfNeeded(ref _plcAddressWeight, "D104", force);
                    AssignIfNeeded(ref _plcAddressDiameter, "D106", force);
                    AssignIfNeeded(ref _plcAddressRuningSignal, "M0", force);
                    break;
            }
        }

        private void ApplyDefaultPortForPlcType(bool force)
        {
            var defaultPort = PlcType switch
            {
                PlcType.SiemensS7 => 102,
                PlcType.DeltaModbusTcp => 502,
                PlcType.InovanceModbusTcp => 502,
                _ => 0
            };

            if (force || _port <= 0)
            {
                Port = defaultPort;
            }
        }

        private static void AssignIfNeeded(ref string target, string value, bool force)
        {
            if (force || string.IsNullOrWhiteSpace(target))
            {
                target = value;
            }
        }
    }
}