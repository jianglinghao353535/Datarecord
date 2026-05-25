namespace Datarecord.Models
{
    public sealed class PlcRealtimeSnapshotModel
    {
        public double ProductionSpeed { get; set; }

        public double ProductionLength { get; set; }

        public double ProductionWeight { get; set; }

        public double ReportWeight { get; set; }

        public double CurrentDiameter { get; set; }

        public bool IsRunningSignalOn { get; set; }
    }
}
