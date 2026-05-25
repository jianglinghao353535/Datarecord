using System;

namespace Datarecord.Models
{
    public sealed class MachineTrendRecordModel
    {
        public DateTime Timestamp { get; set; }

        public double Speed { get; set; }

        public double Length { get; set; }

        public double Diameter { get; set; }

        public double Tension { get; set; }
    }
}
