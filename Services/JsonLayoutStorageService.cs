using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Datarecord.Models;

namespace Datarecord.Services
{
    public sealed class JsonLayoutStorageService : IMachineStorageService
    {
        private readonly string _filePath;
        private readonly JsonSerializerOptions _serializerOptions;

        public JsonLayoutStorageService()
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Datarecord");

            _filePath = Path.Combine(directory, "layout.json");
            _serializerOptions = new JsonSerializerOptions
            {
                WriteIndented = true
            };
        }

        public IReadOnlyList<MachineItemModel> Load()
        {
            if (!File.Exists(_filePath))
            {
                return Array.Empty<MachineItemModel>();
            }

            try
            {
                var json = File.ReadAllText(_filePath);
                var document = JsonSerializer.Deserialize<MachineLayoutDocument>(json, _serializerOptions);
                return document?.Machines != null && document.Machines.Count > 0
                    ? document.Machines
                    : Array.Empty<MachineItemModel>();
            }
            catch
            {
                return Array.Empty<MachineItemModel>();
            }
        }

        public bool IsMachineHistoryEmpty(Guid machineId)
        {
            try
            {
                var machine = Load().FirstOrDefault(x => x.Id == machineId);
                return machine is null || machine.TrendRecords.Count == 0;
            }
            catch
            {
                return false;
            }
        }

        public void Save(IEnumerable<MachineItemModel> machines)
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var document = new MachineLayoutDocument();
            document.Machines.AddRange(machines);

            var json = JsonSerializer.Serialize(document, _serializerOptions);
            File.WriteAllText(_filePath, json);
        }

        public void ClearMachineHistory(Guid machineId)
        {
            if (!File.Exists(_filePath))
            {
                return;
            }

            try
            {
                var json = File.ReadAllText(_filePath);
                var document = JsonSerializer.Deserialize<MachineLayoutDocument>(json, _serializerOptions);
                if (document?.Machines is null)
                {
                    return;
                }

                var machine = document.Machines.Find(x => x.Id == machineId);
                if (machine is null)
                {
                    return;
                }

                machine.TrendRecords.Clear();
                var output = JsonSerializer.Serialize(document, _serializerOptions);
                File.WriteAllText(_filePath, output);
            }
            catch
            {
            }
        }

        private sealed class MachineLayoutDocument
        {
            public List<MachineItemModel> Machines { get; set; } = [];
        }
    }
}