namespace Datarecord.Models
{
    public sealed class MySqlSettingsModel
    {
        public bool Enabled { get; set; }

        public string Server { get; set; } = "localhost";

        public int Port { get; set; } = 3306;

        public string Database { get; set; } = "datarecord_accel";

        public string UserId { get; set; } = "root";

        public string Password { get; set; } = string.Empty;

        public string Charset { get; set; } = "utf8mb4";
    }
}
