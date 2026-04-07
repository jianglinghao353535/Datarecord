using System.Threading;
using System.Threading.Tasks;
using Datarecord.Models;

namespace Datarecord.Services
{
    public interface IPlcIntegrationService
    {
        Task<PlcRealtimeSnapshotModel> ReadCurrentValuesAsync(MachineItemModel machine, CancellationToken cancellationToken = default);
    }
}
