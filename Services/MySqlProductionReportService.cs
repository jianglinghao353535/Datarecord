using System;
using System.Collections.Generic;
using Datarecord.Models;
using MySqlConnector;

namespace Datarecord.Services
{
    public sealed class MySqlProductionReportService : IProductionReportService
    {
        private readonly MySqlSettingsService _settingsService;

        public MySqlProductionReportService(MySqlSettingsService settingsService)
        {
            _settingsService = settingsService;
        }

        public void SaveRunReport(ProductionReportRecordModel record)
        {
            var settings = _settingsService.Load();
            if (!settings.Enabled)
            {
                return;
            }

            try
            {
                EnsureDatabase(settings);

                using var connection = new MySqlConnection(BuildConnectionString(settings, includeDatabase: true));
                connection.Open();
                EnsureSchema(connection);

                using var command = connection.CreateCommand();
                command.CommandText = """
                    INSERT INTO machine_run_reports (
                        machine_id, machine_name, start_time, end_time, length, weight, avg_speed, created_at)
                    VALUES (
                        @machineId, @machineName, @startTime, @endTime, @length, @weight, @avgSpeed, @createdAt);
                    """;
                command.Parameters.AddWithValue("@machineId", record.MachineId.ToString());
                command.Parameters.AddWithValue("@machineName", record.MachineName ?? string.Empty);
                command.Parameters.AddWithValue("@startTime", record.StartTime);
                command.Parameters.AddWithValue("@endTime", record.EndTime);
                command.Parameters.AddWithValue("@length", record.Length);
                command.Parameters.AddWithValue("@weight", record.Weight);
                command.Parameters.AddWithValue("@avgSpeed", record.AverageSpeed);
                command.Parameters.AddWithValue("@createdAt", DateTime.Now);
                command.ExecuteNonQuery();
            }
            catch
            {
            }
        }

        public IReadOnlyList<ProductionReportRecordModel> QueryReports(Guid machineId, DateTime startInclusive, DateTime endExclusive)
        {
            var settings = _settingsService.Load();
            if (!settings.Enabled)
            {
                return Array.Empty<ProductionReportRecordModel>();
            }

            try
            {
                EnsureDatabase(settings);

                using var connection = new MySqlConnection(BuildConnectionString(settings, includeDatabase: true));
                connection.Open();
                EnsureSchema(connection);

                using var command = connection.CreateCommand();
                command.CommandText = """
                    SELECT id, machine_id, machine_name, start_time, end_time, length, weight, avg_speed
                    FROM machine_run_reports
                    WHERE machine_id = @machineId
                      AND start_time >= @startInclusive
                      AND start_time < @endExclusive
                    ORDER BY start_time DESC;
                    """;
                command.Parameters.AddWithValue("@machineId", machineId.ToString());
                command.Parameters.AddWithValue("@startInclusive", startInclusive);
                command.Parameters.AddWithValue("@endExclusive", endExclusive);

                var list = new List<ProductionReportRecordModel>();
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    list.Add(new ProductionReportRecordModel
                    {
                        Id = reader.GetInt64("id"),
                        MachineId = Guid.Parse(reader.GetString("machine_id")),
                        MachineName = reader.GetString("machine_name"),
                        StartTime = reader.GetDateTime("start_time"),
                        EndTime = reader.GetDateTime("end_time"),
                        Length = reader.GetDouble("length"),
                        Weight = reader.GetDouble("weight"),
                        AverageSpeed = reader.GetDouble("avg_speed")
                    });
                }

                return list;
            }
            catch
            {
                return Array.Empty<ProductionReportRecordModel>();
            }
        }

        public (double TotalLength, double TotalWeight) QueryTotals(Guid machineId, DateTime startInclusive, DateTime endExclusive)
        {
            var settings = _settingsService.Load();
            if (!settings.Enabled)
            {
                return (0, 0);
            }

            try
            {
                EnsureDatabase(settings);

                using var connection = new MySqlConnection(BuildConnectionString(settings, includeDatabase: true));
                connection.Open();
                EnsureSchema(connection);

                using var command = connection.CreateCommand();
                command.CommandText = """
                    SELECT COALESCE(SUM(length), 0) AS total_length,
                           COALESCE(SUM(weight), 0) AS total_weight
                    FROM machine_run_reports
                    WHERE machine_id = @machineId
                      AND start_time >= @startInclusive
                      AND start_time < @endExclusive;
                    """;
                command.Parameters.AddWithValue("@machineId", machineId.ToString());
                command.Parameters.AddWithValue("@startInclusive", startInclusive);
                command.Parameters.AddWithValue("@endExclusive", endExclusive);

                using var reader = command.ExecuteReader();
                if (reader.Read())
                {
                    return (reader.GetDouble("total_length"), reader.GetDouble("total_weight"));
                }
            }
            catch
            {
            }

            return (0, 0);
        }

        public void ClearReports(Guid machineId)
        {
            var settings = _settingsService.Load();
            if (!settings.Enabled)
            {
                return;
            }

            try
            {
                EnsureDatabase(settings);

                using var connection = new MySqlConnection(BuildConnectionString(settings, includeDatabase: true));
                connection.Open();
                EnsureSchema(connection);

                using var command = connection.CreateCommand();
                command.CommandText = "DELETE FROM machine_run_reports WHERE machine_id = @machineId;";
                command.Parameters.AddWithValue("@machineId", machineId.ToString());
                command.ExecuteNonQuery();
            }
            catch
            {
            }
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
                AllowUserVariables = true
            };

            if (includeDatabase)
            {
                builder.Database = settings.Database;
            }

            return builder.ConnectionString;
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

        private static string EscapeIdentifier(string identifier)
        {
            return (identifier ?? string.Empty).Replace("`", "``", StringComparison.Ordinal);
        }

        private static void EnsureSchema(MySqlConnection connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS machine_run_reports (
                    id BIGINT AUTO_INCREMENT PRIMARY KEY,
                    machine_id VARCHAR(36) NOT NULL,
                    machine_name VARCHAR(100) NOT NULL,
                    start_time DATETIME(6) NOT NULL,
                    end_time DATETIME(6) NOT NULL,
                    length DOUBLE NOT NULL,
                    weight DOUBLE NOT NULL,
                    avg_speed DOUBLE NOT NULL,
                    created_at DATETIME(6) NOT NULL,
                    INDEX idx_machine_time (machine_id, start_time)
                );
                """;
            command.ExecuteNonQuery();
        }
    }
}
