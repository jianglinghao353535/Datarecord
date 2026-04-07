using System.Collections.Generic;
using Datarecord.Models;

namespace Datarecord.Services
{
    public interface IMachineStorageService
    {
        IReadOnlyList<MachineItemModel> Load();

        void Save(IEnumerable<MachineItemModel> machines);
    }
}
