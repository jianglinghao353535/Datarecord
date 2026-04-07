using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Datarecord.ViewModels;

namespace Datarecord.Services
{
    public interface IMachineMonitoringService : IDisposable
    {
        void Attach(ObservableCollection<MachineItemViewModel> machines);

        Task ReconnectAsync(MachineItemViewModel machine, CancellationToken cancellationToken = default);
    }
}
