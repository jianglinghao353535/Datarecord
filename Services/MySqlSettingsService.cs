using System;
using System.IO;
using System.Text.Json;
using Datarecord.Models;

namespace Datarecord.Services
{
    public sealed class MySqlSettingsService
    {
        private readonly string _filePath;
        private readonly JsonSerializerOptions _serializerOptions = new()
        {
            WriteIndented = true
        };

        public MySqlSettingsService()
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Datarecord");

            Directory.CreateDirectory(directory);
            _filePath = Path.Combine(directory, "mysql-settings.json");
        }

        public MySqlSettingsModel Load()
        {
            if (!File.Exists(_filePath))
            {
                var defaults = new MySqlSettingsModel();
                Save(defaults);
                return defaults;
            }

            try
            {
                var json = File.ReadAllText(_filePath);
                return JsonSerializer.Deserialize<MySqlSettingsModel>(json, _serializerOptions) ?? new MySqlSettingsModel();
            }
            catch
            {
                return new MySqlSettingsModel();
            }
        }

        public void Save(MySqlSettingsModel settings)
        {
            var json = JsonSerializer.Serialize(settings, _serializerOptions);
            File.WriteAllText(_filePath, json);
        }
    }
}
