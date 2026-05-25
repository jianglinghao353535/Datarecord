using System;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Datarecord.Models;
using MySqlConnector;

namespace Datarecord.Services
{
    public sealed class MySqlMachineStorageService : IMachineStorageService
    {
        private readonly MySqlSettingsService _settingsService;
        private readonly IMachineStorageService _fallbackStorageService;
        private readonly JsonSerializerOptions _serializerOptions = new()
        {
            WriteIndented = false
        };

        public MySqlMachineStorageService(MySqlSettingsService settingsService, IMachineStorageService fallbackStorageService)
        {
            _settingsService = settingsService;
            _fallbackStorageService = fallbackStorageService;
        }

        public IReadOnlyList<MachineItemModel> Load()
        {
            var settings = _settingsService.Load();
            if (!settings.Enabled)
            {
                return _fallbackStorageService.Load();
            }

            try
            {
                EnsureDatabase(settings);

                using var connection = new MySqlConnection(BuildConnectionString(settings, includeDatabase: true));
                connection.Open();
                EnsureSchema(connection);

                var machines = LoadMachines(connection).ToDictionary(x => x.Id);
                foreach (var trendRecord in LoadTrendRecords(connection))
                {
                    if (machines.TryGetValue(trendRecord.MachineId, out var machine))
                    {
                        machine.TrendRecords.Add(trendRecord.Record);
                    }
                }

                return machines.Values.ToList();
            }
            catch
            {
                return _fallbackStorageService.Load();
            }
        }

        public void ClearMachineHistory(Guid machineId)
        {
            _fallbackStorageService.ClearMachineHistory(machineId);

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
                command.CommandText = "DELETE FROM machine_trend_records WHERE machine_id = @machineId;";
                command.Parameters.AddWithValue("@machineId", machineId.ToString());
                command.ExecuteNonQuery();
            }
            catch
            {
            }
        }

        public void Save(IEnumerable<MachineItemModel> machines)
        {
            var machineList = machines.ToList();
            _fallbackStorageService.Save(machineList);

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

                using var transaction = connection.BeginTransaction();
                var hasReportTable = TableExists(connection, transaction, "machine_run_reports");

                var machineIds = machineList.Select(x => x.Id.ToString()).ToList();
                if (machineIds.Count > 0)
                {
                    using var deleteMissing = connection.CreateCommand();
                    deleteMissing.Transaction = transaction;
                    deleteMissing.CommandText = $"DELETE FROM machine_configs WHERE id NOT IN ({string.Join(",", machineIds.Select((_, i) => $"@id{i}"))})";
                    for (var i = 0; i < machineIds.Count; i++)
                    {
                        deleteMissing.Parameters.AddWithValue($"@id{i}", machineIds[i]);
                    }
                    deleteMissing.ExecuteNonQuery();

                    using var deleteMissingTrends = connection.CreateCommand();
                    deleteMissingTrends.Transaction = transaction;
                    deleteMissingTrends.CommandText = $"DELETE FROM machine_trend_records WHERE machine_id NOT IN ({string.Join(",", machineIds.Select((_, i) => $"@tid{i}"))})";
                    for (var i = 0; i < machineIds.Count; i++)
                    {
                        deleteMissingTrends.Parameters.AddWithValue($"@tid{i}", machineIds[i]);
                    }
                    deleteMissingTrends.ExecuteNonQuery();

                    if (hasReportTable)
                    {
                        using var deleteMissingReports = connection.CreateCommand();
                        deleteMissingReports.Transaction = transaction;
                        deleteMissingReports.CommandText = $"DELETE FROM machine_run_reports WHERE machine_id NOT IN ({string.Join(",", machineIds.Select((_, i) => $"@rid{i}"))})";
                        for (var i = 0; i < machineIds.Count; i++)
                        {
                            deleteMissingReports.Parameters.AddWithValue($"@rid{i}", machineIds[i]);
                        }

                        deleteMissingReports.ExecuteNonQuery();
                    }
                }
                else
                {
                    using var truncateTrends = connection.CreateCommand();
                    truncateTrends.Transaction = transaction;
                    truncateTrends.CommandText = "DELETE FROM machine_trend_records; DELETE FROM machine_configs;";
                    truncateTrends.ExecuteNonQuery();

                    if (hasReportTable)
                    {
                        using var truncateReports = connection.CreateCommand();
                        truncateReports.Transaction = transaction;
                        truncateReports.CommandText = "DELETE FROM machine_run_reports;";
                        truncateReports.ExecuteNonQuery();
                    }
                }

                foreach (var machine in machineList)
                {
                    SaveMachine(connection, transaction, machine);
                    SaveTrendRecords(connection, transaction, machine);
                }

                transaction.Commit();
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

        private void EnsureSchema(MySqlConnection connection)
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
                    use_manual_y_axis BIT NOT NULL,
                    manual_y_axis_min DOUBLE NOT NULL,
                    manual_y_axis_max DOUBLE NOT NULL,
                    length_y_axis_min DOUBLE NOT NULL,
                    length_y_axis_max DOUBLE NOT NULL,
                    diameter_y_axis_min DOUBLE NOT NULL,
                    diameter_y_axis_max DOUBLE NOT NULL,
                    speed_y_axis_min DOUBLE NOT NULL,
                    speed_y_axis_max DOUBLE NOT NULL,
                    tension_y_axis_min DOUBLE NOT NULL,
                    tension_y_axis_max DOUBLE NOT NULL,
                    plc_address_production_speed VARCHAR(100) NOT NULL,
                    plc_address_production_length VARCHAR(100) NOT NULL,
                    plc_address_production_weight VARCHAR(100) NOT NULL,
                    plc_address_weight VARCHAR(100) NOT NULL,
                    plc_address_runing_signal VARCHAR(100) NOT NULL,
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
            EnsureColumnExists(connection, "machine_configs", "use_manual_y_axis", "BIT NOT NULL DEFAULT b'0'");
            EnsureColumnExists(connection, "machine_configs", "manual_y_axis_min", "DOUBLE NOT NULL DEFAULT 0");
            EnsureColumnExists(connection, "machine_configs", "manual_y_axis_max", "DOUBLE NOT NULL DEFAULT 300");
            EnsureColumnExists(connection, "machine_configs", "length_y_axis_min", "DOUBLE NOT NULL DEFAULT 0");
            EnsureColumnExists(connection, "machine_configs", "length_y_axis_max", "DOUBLE NOT NULL DEFAULT 10000");
            EnsureColumnExists(connection, "machine_configs", "diameter_y_axis_min", "DOUBLE NOT NULL DEFAULT 0");
            EnsureColumnExists(connection, "machine_configs", "diameter_y_axis_max", "DOUBLE NOT NULL DEFAULT 5");
            EnsureColumnExists(connection, "machine_configs", "speed_y_axis_min", "DOUBLE NOT NULL DEFAULT 0");
            EnsureColumnExists(connection, "machine_configs", "speed_y_axis_max", "DOUBLE NOT NULL DEFAULT 2000");
            EnsureColumnExists(connection, "machine_configs", "tension_y_axis_min", "DOUBLE NOT NULL DEFAULT 0");
            EnsureColumnExists(connection, "machine_configs", "tension_y_axis_max", "DOUBLE NOT NULL DEFAULT 200");
            EnsureColumnExists(connection, "machine_configs", "plc_address_weight", "VARCHAR(100) NOT NULL DEFAULT ''");
            EnsureColumnExists(connection, "machine_configs", "plc_address_runing_signal", "VARCHAR(100) NOT NULL DEFAULT ''");
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

        private static bool TableExists(MySqlConnection connection, MySqlTransaction transaction, string tableName)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                SELECT COUNT(*)
                FROM information_schema.tables
                WHERE table_schema = DATABASE()
                  AND table_name = @tableName;
                """;
            command.Parameters.AddWithValue("@tableName", tableName);
            return Convert.ToInt32(command.ExecuteScalar()) > 0;
        }

        private IEnumerable<MachineItemModel> LoadMachines(MySqlConnection connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM machine_configs ORDER BY name";
            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                yield return new MachineItemModel
                {
                    Id = Guid.Parse(reader.GetString("id")),
                    Name = reader.GetString("name"),
                    IpAddress = reader.GetString("ip_address"),
                    PlcType = (PlcType)reader.GetInt32("plc_type"),
                    Port = reader.GetInt32("port"),
                    SampleIntervalMs = reader.GetInt32("sample_interval_ms"),
                    X = reader.GetDouble("position_x"),
                    Y = reader.GetDouble("position_y"),
                    IsEnabled = reader.GetBoolean("is_enabled"),
                    ProductionSpeed = reader.GetDouble("production_speed"),
                    ProductionLength = reader.GetDouble("production_length"),
                    ProductionWeight = reader.GetDouble("production_weight"),
                    CurrentDiameter = reader.GetDouble("current_diameter"),
                    UseManualYAxis = reader.GetBoolean("use_manual_y_axis"),
                    ManualYAxisMin = reader.GetDouble("manual_y_axis_min"),
                    ManualYAxisMax = reader.GetDouble("manual_y_axis_max"),
                    LengthYAxisMin = reader.GetDouble("length_y_axis_min"),
                    LengthYAxisMax = reader.GetDouble("length_y_axis_max"),
                    DiameterYAxisMin = reader.GetDouble("diameter_y_axis_min"),
                    DiameterYAxisMax = reader.GetDouble("diameter_y_axis_max"),
                    SpeedYAxisMin = reader.GetDouble("speed_y_axis_min"),
                    SpeedYAxisMax = reader.GetDouble("speed_y_axis_max"),
                    TensionYAxisMin = reader.GetDouble("tension_y_axis_min"),
                    TensionYAxisMax = reader.GetDouble("tension_y_axis_max"),
                    PlcAddressProductionSpeed = reader.GetString("plc_address_production_speed"),
                    PlcAddressProductionLength = reader.GetString("plc_address_production_length"),
                    PlcAddressProductionWeight = reader.GetString("plc_address_production_weight"),
                    PlcAddressWeight = reader.GetString("plc_address_weight"),
                    PlcAddressRuningSignal = reader.GetString("plc_address_runing_signal"),
                    PlcAddressDiameter = reader.GetString("plc_address_diameter"),
                    TrendRecords = []
                };
            }
        }

        private IEnumerable<(Guid MachineId, MachineTrendRecordModel Record)> LoadTrendRecords(MySqlConnection connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT machine_id, timestamp, speed, length, diameter, tension FROM machine_trend_records ORDER BY timestamp";
            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                yield return (
                    Guid.Parse(reader.GetString("machine_id")),
                    new MachineTrendRecordModel
                    {
                        Timestamp = reader.GetDateTime("timestamp"),
                        Speed = reader.GetDouble("speed"),
                        Length = reader.GetDouble("length"),
                        Diameter = reader.GetDouble("diameter"),
                        Tension = reader.GetDouble("tension")
                    });
            }
        }

        private void SaveMachine(MySqlConnection connection, MySqlTransaction transaction, MachineItemModel machine)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO machine_configs (
                    id, name, ip_address, plc_type, port, sample_interval_ms, position_x, position_y, is_enabled,
                    production_speed, production_length, production_weight, production_status, current_diameter,
                    use_manual_y_axis, manual_y_axis_min, manual_y_axis_max,
                    length_y_axis_min, length_y_axis_max, diameter_y_axis_min, diameter_y_axis_max,
                    speed_y_axis_min, speed_y_axis_max, tension_y_axis_min, tension_y_axis_max,
                    plc_address_production_speed, plc_address_production_length, plc_address_production_weight, plc_address_weight, plc_address_runing_signal,
                    plc_address_production_status, plc_address_diameter, plc_address_temperature_zones,
                    current_temperatures, updated_at)
                VALUES (
                    @id, @name, @ipAddress, @plcType, @port, @sampleIntervalMs, @positionX, @positionY, @isEnabled,
                    @productionSpeed, @productionLength, @productionWeight, @productionStatus, @currentDiameter,
                    @useManualYAxis, @manualYAxisMin, @manualYAxisMax,
                    @lengthYAxisMin, @lengthYAxisMax, @diameterYAxisMin, @diameterYAxisMax,
                    @speedYAxisMin, @speedYAxisMax, @tensionYAxisMin, @tensionYAxisMax,
                    @addressSpeed, @addressLength, @addressWeight, @addressWeightReport, @addressRuningSignal, @addressStatus, @addressDiameter,
                    @addressTemperatureZones, @currentTemperatures, @updatedAt)
                ON DUPLICATE KEY UPDATE
                    name = VALUES(name),
                    ip_address = VALUES(ip_address),
                    plc_type = VALUES(plc_type),
                    port = VALUES(port),
                    sample_interval_ms = VALUES(sample_interval_ms),
                    position_x = VALUES(position_x),
                    position_y = VALUES(position_y),
                    is_enabled = VALUES(is_enabled),
                    production_speed = VALUES(production_speed),
                    production_length = VALUES(production_length),
                    production_weight = VALUES(production_weight),
                    production_status = VALUES(production_status),
                    current_diameter = VALUES(current_diameter),
                    use_manual_y_axis = VALUES(use_manual_y_axis),
                    manual_y_axis_min = VALUES(manual_y_axis_min),
                    manual_y_axis_max = VALUES(manual_y_axis_max),
                    length_y_axis_min = VALUES(length_y_axis_min),
                    length_y_axis_max = VALUES(length_y_axis_max),
                    diameter_y_axis_min = VALUES(diameter_y_axis_min),
                    diameter_y_axis_max = VALUES(diameter_y_axis_max),
                    speed_y_axis_min = VALUES(speed_y_axis_min),
                    speed_y_axis_max = VALUES(speed_y_axis_max),
                    tension_y_axis_min = VALUES(tension_y_axis_min),
                    tension_y_axis_max = VALUES(tension_y_axis_max),
                    plc_address_production_speed = VALUES(plc_address_production_speed),
                    plc_address_production_length = VALUES(plc_address_production_length),
                    plc_address_production_weight = VALUES(plc_address_production_weight),
                    plc_address_weight = VALUES(plc_address_weight),
                    plc_address_runing_signal = VALUES(plc_address_runing_signal),
                    plc_address_production_status = VALUES(plc_address_production_status),
                    plc_address_diameter = VALUES(plc_address_diameter),
                    plc_address_temperature_zones = VALUES(plc_address_temperature_zones),
                    current_temperatures = VALUES(current_temperatures),
                    updated_at = VALUES(updated_at);
                """;

            command.Parameters.AddWithValue("@id", machine.Id.ToString());
            command.Parameters.AddWithValue("@name", machine.Name);
            command.Parameters.AddWithValue("@ipAddress", machine.IpAddress);
            command.Parameters.AddWithValue("@plcType", (int)machine.PlcType);
            command.Parameters.AddWithValue("@port", machine.Port);
            command.Parameters.AddWithValue("@sampleIntervalMs", machine.SampleIntervalMs);
            command.Parameters.AddWithValue("@positionX", machine.X);
            command.Parameters.AddWithValue("@positionY", machine.Y);
            command.Parameters.AddWithValue("@isEnabled", machine.IsEnabled);
            command.Parameters.AddWithValue("@productionSpeed", machine.ProductionSpeed);
            command.Parameters.AddWithValue("@productionLength", machine.ProductionLength);
            command.Parameters.AddWithValue("@productionWeight", machine.ProductionWeight);
            command.Parameters.AddWithValue("@productionStatus", "N/A");
            command.Parameters.AddWithValue("@currentDiameter", machine.CurrentDiameter);
            command.Parameters.AddWithValue("@useManualYAxis", machine.UseManualYAxis);
            command.Parameters.AddWithValue("@manualYAxisMin", machine.ManualYAxisMin);
            command.Parameters.AddWithValue("@manualYAxisMax", machine.ManualYAxisMax);
            command.Parameters.AddWithValue("@lengthYAxisMin", machine.LengthYAxisMin);
            command.Parameters.AddWithValue("@lengthYAxisMax", machine.LengthYAxisMax);
            command.Parameters.AddWithValue("@diameterYAxisMin", machine.DiameterYAxisMin);
            command.Parameters.AddWithValue("@diameterYAxisMax", machine.DiameterYAxisMax);
            command.Parameters.AddWithValue("@speedYAxisMin", machine.SpeedYAxisMin);
            command.Parameters.AddWithValue("@speedYAxisMax", machine.SpeedYAxisMax);
            command.Parameters.AddWithValue("@tensionYAxisMin", machine.TensionYAxisMin);
            command.Parameters.AddWithValue("@tensionYAxisMax", machine.TensionYAxisMax);
            command.Parameters.AddWithValue("@addressSpeed", machine.PlcAddressProductionSpeed ?? string.Empty);
            command.Parameters.AddWithValue("@addressLength", machine.PlcAddressProductionLength ?? string.Empty);
            command.Parameters.AddWithValue("@addressWeight", machine.PlcAddressProductionWeight ?? string.Empty);
            command.Parameters.AddWithValue("@addressWeightReport", machine.PlcAddressWeight ?? string.Empty);
            command.Parameters.AddWithValue("@addressRuningSignal", machine.PlcAddressRuningSignal ?? string.Empty);
            command.Parameters.AddWithValue("@addressStatus", string.Empty);
            command.Parameters.AddWithValue("@addressDiameter", machine.PlcAddressDiameter ?? string.Empty);
            command.Parameters.AddWithValue("@addressTemperatureZones", JsonSerializer.Serialize(Array.Empty<string>(), _serializerOptions));
            command.Parameters.AddWithValue("@currentTemperatures", JsonSerializer.Serialize(Array.Empty<double>(), _serializerOptions));
            command.Parameters.AddWithValue("@updatedAt", DateTime.Now);
            command.ExecuteNonQuery();
        }

        private void SaveTrendRecords(MySqlConnection connection, MySqlTransaction transaction, MachineItemModel machine)
        {
            using var deleteCommand = connection.CreateCommand();
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = "DELETE FROM machine_trend_records WHERE machine_id = @machineId";
            deleteCommand.Parameters.AddWithValue("@machineId", machine.Id.ToString());
            deleteCommand.ExecuteNonQuery();

            foreach (var record in machine.TrendRecords)
            {
                using var insertCommand = connection.CreateCommand();
                insertCommand.Transaction = transaction;
                insertCommand.CommandText = """
                    INSERT INTO machine_trend_records (machine_id, timestamp, speed, length, diameter, tension)
                    VALUES (@machineId, @timestamp, @speed, @length, @diameter, @tension)
                    """;
                insertCommand.Parameters.AddWithValue("@machineId", machine.Id.ToString());
                insertCommand.Parameters.AddWithValue("@timestamp", record.Timestamp);
                insertCommand.Parameters.AddWithValue("@speed", record.Speed);
                insertCommand.Parameters.AddWithValue("@length", record.Length);
                insertCommand.Parameters.AddWithValue("@diameter", record.Diameter);
                insertCommand.Parameters.AddWithValue("@tension", record.Tension);
                insertCommand.ExecuteNonQuery();
            }
        }

    }
}
