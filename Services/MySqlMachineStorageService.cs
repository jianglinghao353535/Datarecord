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
                using var connection = new MySqlConnection(BuildConnectionString(settings));
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
                using var connection = new MySqlConnection(BuildConnectionString(settings));
                connection.Open();
                EnsureSchema(connection);

                using var transaction = connection.BeginTransaction();

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
                }
                else
                {
                    using var truncateTrends = connection.CreateCommand();
                    truncateTrends.Transaction = transaction;
                    truncateTrends.CommandText = "DELETE FROM machine_trend_records; DELETE FROM machine_configs;";
                    truncateTrends.ExecuteNonQuery();
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

        private static string BuildConnectionString(MySqlSettingsModel settings)
        {
            var builder = new MySqlConnectionStringBuilder
            {
                Server = settings.Server,
                Port = (uint)settings.Port,
                Database = settings.Database,
                UserID = settings.UserId,
                Password = settings.Password,
                CharacterSet = settings.Charset,
                AllowUserVariables = true
            };

            return builder.ConnectionString;
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
                    ProductionStatus = reader.GetString("production_status"),
                    CurrentDiameter = reader.GetDouble("current_diameter"),
                    PlcAddressProductionSpeed = reader.GetString("plc_address_production_speed"),
                    PlcAddressProductionLength = reader.GetString("plc_address_production_length"),
                    PlcAddressProductionWeight = reader.GetString("plc_address_production_weight"),
                    PlcAddressProductionStatus = reader.GetString("plc_address_production_status"),
                    PlcAddressDiameter = reader.GetString("plc_address_diameter"),
                    PlcAddressTemperatureZones = DeserializeStringArray(reader.GetString("plc_address_temperature_zones")),
                    CurrentTemperatures = DeserializeDoubleArray(reader.GetString("current_temperatures")),
                    TrendRecords = []
                };
            }
        }

        private IEnumerable<(Guid MachineId, MachineTrendRecordModel Record)> LoadTrendRecords(MySqlConnection connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT machine_id, timestamp, speed, diameter, temperature_zones FROM machine_trend_records ORDER BY timestamp";
            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                yield return (
                    Guid.Parse(reader.GetString("machine_id")),
                    new MachineTrendRecordModel
                    {
                        Timestamp = reader.GetDateTime("timestamp"),
                        Speed = reader.GetDouble("speed"),
                        Diameter = reader.GetDouble("diameter"),
                        TemperatureZones = DeserializeDoubleArray(reader.GetString("temperature_zones"))
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
                    plc_address_production_speed, plc_address_production_length, plc_address_production_weight,
                    plc_address_production_status, plc_address_diameter, plc_address_temperature_zones,
                    current_temperatures, updated_at)
                VALUES (
                    @id, @name, @ipAddress, @plcType, @port, @sampleIntervalMs, @positionX, @positionY, @isEnabled,
                    @productionSpeed, @productionLength, @productionWeight, @productionStatus, @currentDiameter,
                    @addressSpeed, @addressLength, @addressWeight, @addressStatus, @addressDiameter,
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
                    plc_address_production_speed = VALUES(plc_address_production_speed),
                    plc_address_production_length = VALUES(plc_address_production_length),
                    plc_address_production_weight = VALUES(plc_address_production_weight),
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
            command.Parameters.AddWithValue("@productionStatus", machine.ProductionStatus);
            command.Parameters.AddWithValue("@currentDiameter", machine.CurrentDiameter);
            command.Parameters.AddWithValue("@addressSpeed", machine.PlcAddressProductionSpeed ?? string.Empty);
            command.Parameters.AddWithValue("@addressLength", machine.PlcAddressProductionLength ?? string.Empty);
            command.Parameters.AddWithValue("@addressWeight", machine.PlcAddressProductionWeight ?? string.Empty);
            command.Parameters.AddWithValue("@addressStatus", machine.PlcAddressProductionStatus ?? string.Empty);
            command.Parameters.AddWithValue("@addressDiameter", machine.PlcAddressDiameter ?? string.Empty);
            command.Parameters.AddWithValue("@addressTemperatureZones", JsonSerializer.Serialize(machine.PlcAddressTemperatureZones ?? [], _serializerOptions));
            command.Parameters.AddWithValue("@currentTemperatures", JsonSerializer.Serialize(machine.CurrentTemperatures ?? [], _serializerOptions));
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
                    INSERT INTO machine_trend_records (machine_id, timestamp, speed, diameter, temperature_zones)
                    VALUES (@machineId, @timestamp, @speed, @diameter, @temperatureZones)
                    """;
                insertCommand.Parameters.AddWithValue("@machineId", machine.Id.ToString());
                insertCommand.Parameters.AddWithValue("@timestamp", record.Timestamp);
                insertCommand.Parameters.AddWithValue("@speed", record.Speed);
                insertCommand.Parameters.AddWithValue("@diameter", record.Diameter);
                insertCommand.Parameters.AddWithValue("@temperatureZones", JsonSerializer.Serialize(record.TemperatureZones ?? [], _serializerOptions));
                insertCommand.ExecuteNonQuery();
            }
        }

        private double[] DeserializeDoubleArray(string json)
        {
            return JsonSerializer.Deserialize<double[]>(json, _serializerOptions) ?? new double[8];
        }

        private string[] DeserializeStringArray(string json)
        {
            return JsonSerializer.Deserialize<string[]>(json, _serializerOptions) ?? new string[8];
        }
    }
}
