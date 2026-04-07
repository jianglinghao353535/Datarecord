namespace Datarecord.Models
{
    public sealed class PlcRealtimeSnapshotModel
    {
        public double ProductionSpeed { get; set; }

        public double ProductionLength { get; set; }

        public double ProductionWeight { get; set; }

        public string ProductionStatus { get; set; } = "´ý™C";

        public double CurrentDiameter { get; set; }

        public double[] Temperatures { get; set; } = new double[8];
    }
}
