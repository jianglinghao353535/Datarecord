using Datarecord.Models;

namespace Datarecord.ViewModels
{
    public sealed class MySqlSettingsViewModel : ViewModelBase
    {
        private bool _enabled;
        private string _server = "localhost";
        private int _port = 3306;
        private string _database = "datarecord_accel";
        private string _userId = "root";
        private string _password = string.Empty;
        private string _charset = "utf8mb4";
        private string _statusText = "Please enter database connection settings.";

        public bool Enabled
        {
            get => _enabled;
            set => SetProperty(ref _enabled, value);
        }

        public string Server
        {
            get => _server;
            set => SetProperty(ref _server, value);
        }

        public int Port
        {
            get => _port;
            set => SetProperty(ref _port, value);
        }

        public string Database
        {
            get => _database;
            set => SetProperty(ref _database, value);
        }

        public string UserId
        {
            get => _userId;
            set => SetProperty(ref _userId, value);
        }

        public string Password
        {
            get => _password;
            set => SetProperty(ref _password, value);
        }

        public string Charset
        {
            get => _charset;
            set => SetProperty(ref _charset, value);
        }

        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        public static MySqlSettingsViewModel FromModel(MySqlSettingsModel model)
        {
            return new MySqlSettingsViewModel
            {
                Enabled = model.Enabled,
                Server = model.Server,
                Port = model.Port,
                Database = model.Database,
                UserId = model.UserId,
                Password = model.Password,
                Charset = model.Charset
            };
        }

        public MySqlSettingsModel ToModel()
        {
            return new MySqlSettingsModel
            {
                Enabled = Enabled,
                Server = Server,
                Port = Port,
                Database = Database,
                UserId = UserId,
                Password = Password,
                Charset = Charset
            };
        }
    }
}
