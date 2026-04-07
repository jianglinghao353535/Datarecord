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

        public string ProductionStatus { get; set; } = "´ý™C";

        public double CurrentDiameter { get; set; }

        public string PlcAddressProductionSpeed { get; set; } = string.Empty;

        public string PlcAddressProductionLength { get; set; } = string.Empty;

        public string PlcAddressProductionWeight { get; set; } = string.Empty;

        public string PlcAddressProductionStatus { get; set; } = string.Empty;

        public string PlcAddressDiameter { get; set; } = string.Empty;

        public string[] PlcAddressTemperatureZones { get; set; } = new string[8];

        public double[] CurrentTemperatures { get; set; } = new double[8];

        public List<MachineTrendRecordModel> TrendRecords { get; set; } = [];
    }
}