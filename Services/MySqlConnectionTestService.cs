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

                return (true, "MySQL connection succeeded.");
            }
            catch (Exception ex)
            {
                return (false, $"MySQL connection failed: {ex.Message}");
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

                return (true, $"Database initialization completed: {settings.Database}");
            }
            catch (Exception ex)
            {
                return (false, $"Database initialization failed: {ex.Message}");
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

                var trendDeleted = 0;
                var reportDeleted = 0;

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "DELETE FROM machine_trend_records;";
                    trendDeleted = command.ExecuteNonQuery();
                }

                if (TableExists(connection, "machine_run_reports"))
                {
                    using var reportCommand = connection.CreateCommand();
                    reportCommand.CommandText = "DELETE FROM machine_run_reports;";
                    reportDeleted = reportCommand.ExecuteNonQuery();
                }

                return (true, $"History cleared. Deleted {trendDeleted} trend record(s), {reportDeleted} run report record(s).");
            }
            catch (Exception ex)
            {
                return (false, $"Failed to clear history: {ex.Message}");
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
                    length DOUBLE NOT NULL,
                    diameter DOUBLE NOT NULL,
                    tension DOUBLE NOT NULL,
                    PRIMARY KEY (machine_id, timestamp)
                );
                """;
            command.ExecuteNonQuery();

            EnsureColumnExists(connection, "machine_trend_records", "length", "DOUBLE NOT NULL DEFAULT 0");
            EnsureColumnExists(connection, "machine_trend_records", "tension", "DOUBLE NOT NULL DEFAULT 0");
        }

        private static void EnsureColumnExists(MySqlConnection connection, string tableName, string columnName, string columnDefinition)
        {
            using var checkCommand = connection.CreateCommand();
            checkCommand.CommandText = """
                SELECT COUNT(*)
                FROM information_schema.columns
                WHERE table_schema = DATABASE()
                  AND table_name = @tableName
                  AND column_name = @columnName;
                """;
            checkCommand.Parameters.AddWithValue("@tableName", tableName);
            checkCommand.Parameters.AddWithValue("@columnName", columnName);

            var exists = Convert.ToInt32(checkCommand.ExecuteScalar()) > 0;
            if (exists)
            {
                return;
            }

            using var alterCommand = connection.CreateCommand();
            alterCommand.CommandText = $"ALTER TABLE `{tableName}` ADD COLUMN `{columnName}` {columnDefinition};";
            alterCommand.ExecuteNonQuery();
        }

        private static bool TableExists(MySqlConnection connection, string tableName)
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT COUNT(*)
                FROM information_schema.tables
                WHERE table_schema = DATABASE()
                  AND table_name = @tableName;
                """;
            command.Parameters.AddWithValue("@tableName", tableName);
            return Convert.ToInt32(command.ExecuteScalar()) > 0;
        }
    }
}
