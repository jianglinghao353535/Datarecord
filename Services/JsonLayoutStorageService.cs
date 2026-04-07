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

        private sealed class MachineLayoutDocument
        {
            public List<MachineItemModel> Machines { get; set; } = [];
        }
    }
}