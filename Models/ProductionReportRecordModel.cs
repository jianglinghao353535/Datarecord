using System;

namespace Datarecord.Models
{
    public sealed class ProductionReportRecordModel
    {
        public int SerialNumber { get; set; }

        public long Id { get; set; }

        public Guid MachineId { get; set; }

        public string MachineName { get; set; } = string.Empty;

        public DateTime StartTime { get; set; }

        public DateTime EndTime { get; set; }

        public double Length { get; set; }

        public double Weight { get; set; }

        public double AverageSpeed { get; set; }
    }
}
