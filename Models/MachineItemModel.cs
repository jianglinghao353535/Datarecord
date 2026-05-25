using System;
using System.Collections.Generic;

namespace Datarecord.Models
{
    public sealed class MachineItemModel
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string IpAddress { get; set; } = string.Empty;

        public PlcType PlcType { get; set; }

        public int Port { get; set; }

        public int SampleIntervalMs { get; set; }

        public double X { get; set; }

        public double Y { get; set; }

        public bool IsEnabled { get; set; }

        public double ProductionSpeed { get; set; }

        public double ProductionLength { get; set; }

        public double ProductionWeight { get; set; }

        public double CurrentDiameter { get; set; }

        public bool UseManualYAxis { get; set; }

        public double ManualYAxisMin { get; set; }

        public double ManualYAxisMax { get; set; } = 300;

        public double LengthYAxisMin { get; set; }

        public double LengthYAxisMax { get; set; } = 10000;

        public double DiameterYAxisMin { get; set; }

        public double DiameterYAxisMax { get; set; } = 5;

        public double SpeedYAxisMin { get; set; }

        public double SpeedYAxisMax { get; set; } = 2000;

        public double TensionYAxisMin { get; set; }

        public double TensionYAxisMax { get; set; } = 200;

        public string PlcAddressProductionSpeed { get; set; } = string.Empty;

        public string PlcAddressProductionLength { get; set; } = string.Empty;

        public string PlcAddressProductionWeight { get; set; } = string.Empty;

        public string PlcAddressWeight { get; set; } = string.Empty;

        public string PlcAddressDiameter { get; set; } = string.Empty;

        public string PlcAddressRuningSignal { get; set; } = string.Empty;

        public List<MachineTrendRecordModel> TrendRecords { get; set; } = [];
    }
}