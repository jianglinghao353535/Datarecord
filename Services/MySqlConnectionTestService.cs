using System;
using Datarecord.Models;
using MySqlConnector;

namespace Datarecord.Services
{
    public sealed class MySqlConnectionTestService
    {
        public (bool Success, string Message) Test(MySqlSettingsModel settings)
        {
            try
            {
                using var connection = new MySqlConnection(BuildConnectionString(settings, includeDatabase: true));
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT 1";
                command.ExecuteScalar();

                return (true, "MySQL 連線成功。");
            }
            catch (Exception ex)
            {
                return (false, $"MySQL 連線失敗：{ex.Message}");
            }
        }

        public (bool Success, string Message) InitializeDatabase(MySqlSettingsModel settings)
        {
            try
            {
                EnsureDatabase(settings);

                using var connection = new MySqlConnection(BuildConnectionString(settings, includeDatabase: true));
                connection.Open();
                EnsureSchema(connection);

                return (true, $"資料庫初始化完成：{settings.Database}");
            }
            catch (Exception ex)
            {
                return (false, $"初始化資料庫失敗：{ex.Message}");
            }
        }

        public (bool Success, string Message) ClearHistory(MySqlSettingsModel settings)
        {
            try
            {
                EnsureDatabase(settings);

                using var connection = new MySqlConnection(BuildConnectionString(settings, includeDatabase: true));
                connection.Open();
                EnsureSchema(connection);

                using var command = connection.CreateCommand();
                command.CommandText = "DELETE FROM machine_trend_records;";
                var affectedRows = command.ExecuteNonQuery();

                return (true, $"已清空歷史資料，共刪除 {affectedRows} 筆趨勢記錄。");
            }
            catch (Exception ex)
            {
                return (false, $"清空歷史資料失敗：{ex.Message}");
            }
        }

        private static void EnsureDatabase(MySqlSettingsModel settings)
        {
            using var connection = new MySqlConnection(BuildConnectionString(settings, includeDatabase: false));
            connection.Open();

            using var command = connection.CreateCommand();
            var databaseName = EscapeIdentifier(settings.Database);
            var charset = string.IsNullOrWhiteSpace(settings.Charset) ? "utf8mb4" : settings.Charset.Trim();
            command.CommandText = $"CREATE DATABASE IF NOT EXISTS `{databaseName}` CHARACTER SET {charset};";
            command.ExecuteNonQuery();
        }

        private static string BuildConnectionString(MySqlSettingsModel settings, bool includeDatabase)
        {
            var builder = new MySqlConnectionStringBuilder
            {
                Server = settings.Server,
                Port = (uint)settings.Port,
                UserID = settings.UserId,
                Password = settings.Password,
                CharacterSet = settings.Charset,
                AllowUserVariables = true,
                ConnectionTimeout = 5
            };

            if (includeDatabase)
            {
                builder.Database = settings.Database;
            }

            return builder.ConnectionString;
        }

        private static string EscapeIdentifier(string identifier)
        {
            return (identifier ?? string.Empty).Replace("`", "``", StringComparison.Ordinal);
        }

        private static void EnsureSchema(MySqlConnection connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS machine_configs (
                    id VARCHAR(36) PRIMARY KEY,
                    name VARCHAR(100) NOT NULL,
                    ip_address VARCHAR(100) NOT NULL,
                    plc_type INT NOT NULL,
                    port INT NOT NULL,
                    sample_interval_ms INT NOT NULL,
                    position_x DOUBLE NOT NULL,
                    position_y DOUBLE NOT NULL,
                    is_enabled BIT NOT NULL,
                    production_speed DOUBLE NOT NULL,
                    production_length DOUBLE NOT NULL,
                    production_weight DOUBLE NOT NULL,
                    production_status VARCHAR(100) NOT NULL,
                    current_diameter DOUBLE NOT NULL,
                    plc_address_production_speed VARCHAR(100) NOT NULL,
                    plc_address_production_length VARCHAR(100) NOT NULL,
                    plc_address_production_weight VARCHAR(100) NOT NULL,
                    plc_address_production_status VARCHAR(100) NOT NULL,
                    plc_address_diameter VARCHAR(100) NOT NULL,
                    plc_address_temperature_zones LONGTEXT NOT NULL,
                    current_temperatures LONGTEXT NOT NULL,
                    updated_at DATETIME(6) NOT NULL
                );

                CREATE TABLE IF NOT EXISTS machine_trend_records (
                    machine_id VARCHAR(36) NOT NULL,
                    timestamp DATETIME(6) NOT NULL,
                    speed DOUBLE NOT NULL,
                    diameter DOUBLE NOT NULL,
                    temperature_zones LONGTEXT NOT NULL,
                    PRIMARY KEY (machine_id, timestamp)
                );
                """;
            command.ExecuteNonQuery();
        }
    }
}
