using System.Collections.Generic;
using System;
using Datarecord.Models;

namespace Datarecord.Services
{
    public interface IMachineStorageService
    {
        IReadOnlyList<MachineItemModel> Load();

        void Save(IEnumerable<MachineItemModel> machines);

        void ClearMachineHistory(Guid machineId);
    }
}
