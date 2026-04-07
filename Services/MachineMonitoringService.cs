using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Datarecord.Models;
using Datarecord.ViewModels;

namespace Datarecord.Services
{
    public sealed class MachineMonitoringService : IMachineMonitoringService
    {
        private static readonly TimeSpan AutoReconnectDelay = TimeSpan.FromSeconds(5);
        private readonly IPlcIntegrationService _plcIntegrationService;
        private readonly IMachineStorageService _storageService;
        private readonly Dispatcher _dispatcher;
        private readonly Dictionary<Guid, MachineMonitorState> _states = [];
        private readonly SemaphoreSlim _archiveSemaphore = new(1, 1);
        private readonly CancellationTokenSource _disposeCts = new();
        private readonly object _syncRoot = new();
        private ObservableCollection<MachineItemViewModel>? _machines;
        private CancellationTokenSource? _archiveDelayCts;

        public MachineMonitoringService(
            IPlcIntegrationService plcIntegrationService,
            IMachineStorageService storageService,
            Dispatcher dispatcher)
        {
            _plcIntegrationService = plcIntegrationService;
            _storageService = storageService;
            _dispatcher = dispatcher;
        }

        public void Attach(ObservableCollection<MachineItemViewModel> machines)
        {
            if (_machines is not null)
            {
                _machines.CollectionChanged -= MachinesOnCollectionChanged;
                foreach (var machine in _machines)
                {
                    machine.PropertyChanged -= MachineOnPropertyChanged;
                }
            }

            _machines = machines;
            _machines.CollectionChanged += MachinesOnCollectionChanged;

            foreach (var machine in _machines)
            {
                machine.PropertyChanged += MachineOnPropertyChanged;
                if (machine.IsEnabled)
                {
                    EnsureMonitorStarted(machine);
                }
            }
        }

        public async Task ReconnectAsync(MachineItemViewModel machine, CancellationToken cancellationToken = default)
        {
            await _dispatcher.InvokeAsync(() =>
            {
                machine.IsManualReconnectInProgress = true;
                machine.IsPlcSynchronizing = true;
                machine.PlcStatusText = "正在手動重新連線 PLC…";
            });

            try
            {
                await RestartMonitorAsync(machine, true, cancellationToken);
            }
            finally
            {
                await _dispatcher.InvokeAsync(() => machine.IsManualReconnectInProgress = false);
            }
        }

        public void Dispose()
        {
            _disposeCts.Cancel();

            if (_machines is not null)
            {
                _machines.CollectionChanged -= MachinesOnCollectionChanged;
                foreach (var machine in _machines)
                {
                    machine.PropertyChanged -= MachineOnPropertyChanged;
                }
            }

            CancellationTokenSource? archiveDelayCts;
            lock (_syncRoot)
            {
                archiveDelayCts = _archiveDelayCts;
                _archiveDelayCts = null;
            }

            archiveDelayCts?.Cancel();
            archiveDelayCts?.Dispose();

            List<CancellationTokenSource> ctsList;
            lock (_syncRoot)
            {
                ctsList = _states.Values
                    .Select(x => x.CancellationTokenSource)
                    .Where(x => x is not null)
                    .Cast<CancellationTokenSource>()
                    .ToList();
                _states.Clear();
            }

            foreach (var cts in ctsList)
            {
                cts.Cancel();
                cts.Dispose();
            }

            _archiveSemaphore.Dispose();
            _disposeCts.Dispose();
        }

        private void MachinesOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems is not null)
            {
                foreach (var machine in e.OldItems.OfType<MachineItemViewModel>())
                {
                    machine.PropertyChanged -= MachineOnPropertyChanged;
                    StopMonitor(machine, "PLC 監控已停止。");
                }
            }

            if (e.NewItems is not null)
            {
                foreach (var machine in e.NewItems.OfType<MachineItemViewModel>())
                {
                    machine.PropertyChanged += MachineOnPropertyChanged;
                    if (machine.IsEnabled)
                    {
                        EnsureMonitorStarted(machine);
                    }
                }
            }

            ScheduleArchiveSave();
        }

        private void MachineOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not MachineItemViewModel machine)
            {
                return;
            }

            if (e.PropertyName == nameof(MachineItemViewModel.IsEnabled))
            {
                if (machine.IsEnabled)
                {
                    EnsureMonitorStarted(machine);
                }
                else
                {
                    StopMonitor(machine, "機台已停用，PLC 監控已停止。");
                }

                ScheduleArchiveSave();
                return;
            }

            if (e.PropertyName is nameof(MachineItemViewModel.Name)
                or nameof(MachineItemViewModel.IpAddress)
                or nameof(MachineItemViewModel.PlcType)
                or nameof(MachineItemViewModel.Port)
                or nameof(MachineItemViewModel.SampleIntervalMs)
                or nameof(MachineItemViewModel.PlcAddressProductionSpeed)
                or nameof(MachineItemViewModel.PlcAddressProductionLength)
                or nameof(MachineItemViewModel.PlcAddressProductionWeight)
                or nameof(MachineItemViewModel.PlcAddressProductionStatus)
                or nameof(MachineItemViewModel.PlcAddressDiameter))
            {
                ResetMonitorAfterConfigurationChanged(machine);
                ScheduleArchiveSave();
            }
        }

        private void EnsureMonitorStarted(MachineItemViewModel machine)
        {
            if (_disposeCts.IsCancellationRequested)
            {
                return;
            }

            lock (_syncRoot)
            {
                if (_states.TryGetValue(machine.Id, out var existingState) && existingState.MonitorTask is { IsCompleted: false })
                {
                    return;
                }
            }

            _ = RestartMonitorAsync(machine, false, CancellationToken.None);
        }

        private async Task RestartMonitorAsync(MachineItemViewModel machine, bool manualReconnect, CancellationToken cancellationToken)
        {
            if (_disposeCts.IsCancellationRequested)
            {
                return;
            }

            MachineMonitorState state;
            CancellationTokenSource? previousCts = null;
            Task? previousTask = null;

            lock (_syncRoot)
            {
                if (!_states.TryGetValue(machine.Id, out state!))
                {
                    state = new MachineMonitorState();
                    _states[machine.Id] = state;
                }
                else if (!manualReconnect && state.MonitorTask is { IsCompleted: false })
                {
                    return;
                }

                previousCts = state.CancellationTokenSource;
                previousTask = state.MonitorTask;
                state.CancellationTokenSource = null;
                state.MonitorTask = null;
            }

            previousCts?.Cancel();

            if (previousTask is { IsCompleted: false })
            {
                try
                {
                    await previousTask.WaitAsync(TimeSpan.FromMilliseconds(300), cancellationToken);
                }
                catch
                {
                }
            }

            previousCts?.Dispose();

            if (_disposeCts.IsCancellationRequested || !machine.IsEnabled)
            {
                return;
            }

            var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token, cancellationToken);
            var monitorTask = RunMonitorLoopAsync(machine, linkedCts.Token, manualReconnect);

            lock (_syncRoot)
            {
                if (_states.TryGetValue(machine.Id, out var currentState))
                {
                    currentState.CancellationTokenSource = linkedCts;
                    currentState.MonitorTask = monitorTask;
                }
            }
        }

        private async Task RunMonitorLoopAsync(MachineItemViewModel machine, CancellationToken cancellationToken, bool manualReconnect)
        {
            try
            {
                await SyncMachineOnceAsync(
                    machine,
                    manualReconnect
                        ? "正在重新連線 PLC，成功後將自動開始變數歸檔…"
                        : "正在自動連線 PLC，成功後將自動開始變數歸檔…",
                    cancellationToken);

                while (!cancellationToken.IsCancellationRequested && machine.IsEnabled)
                {
                    var delayMs = Math.Max(200, machine.SampleIntervalMs);
                    await Task.Delay(delayMs, cancellationToken);
                    await SyncMachineOnceAsync(machine, null, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                ClearMonitorState(machine.Id);

                await _dispatcher.InvokeAsync(() =>
                {
                    machine.IsPlcSynchronizing = false;
                    machine.PlcStatusText = $"PLC 連線失敗或已逾時：{ex.Message}。輪詢已停止，請手動按下重新連線 PLC。";
                });
            }
        }

        private async Task SyncMachineOnceAsync(MachineItemViewModel machine, string? startStatusText, CancellationToken cancellationToken)
        {
            if (!machine.IsEnabled)
            {
                return;
            }

            MachineItemModel machineSnapshot = await _dispatcher.InvokeAsync(() =>
            {
                machine.IsPlcSynchronizing = true;
                if (!string.IsNullOrWhiteSpace(startStatusText))
                {
                    machine.PlcStatusText = startStatusText;
                }

                return machine.ToModel();
            });

            PlcRealtimeSnapshotModel plcSnapshot;
            try
            {
                plcSnapshot = await _plcIntegrationService.ReadCurrentValuesAsync(machineSnapshot, cancellationToken);
            }
            finally
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            await _dispatcher.InvokeAsync(() =>
            {
                machine.ApplySnapshot(plcSnapshot);
                machine.AddTrendRecord(DateTime.Now);
                machine.PlcStatusText = $"PLC 已連線，變數歸檔中，最後同步：{DateTime.Now:yyyy-MM-dd HH:mm:ss}";
                machine.IsPlcSynchronizing = false;
            });

            ScheduleArchiveSave();
        }

        private void StopMonitor(MachineItemViewModel machine, string statusText)
        {
            CancellationTokenSource? cts = null;
            lock (_syncRoot)
            {
                if (_states.TryGetValue(machine.Id, out var state))
                {
                    cts = state.CancellationTokenSource;
                    state.CancellationTokenSource = null;
                    state.MonitorTask = null;
                }
            }

            cts?.Cancel();
            cts?.Dispose();

            _ = _dispatcher.InvokeAsync(() =>
            {
                machine.IsPlcSynchronizing = false;
                machine.PlcStatusText = statusText;
            });
        }

        private void ScheduleArchiveSave()
        {
            if (_disposeCts.IsCancellationRequested || _machines is null)
            {
                return;
            }

            CancellationTokenSource delayCts;
            lock (_syncRoot)
            {
                _archiveDelayCts?.Cancel();
                _archiveDelayCts?.Dispose();
                _archiveDelayCts = CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token);
                delayCts = _archiveDelayCts;
            }

            _ = PersistMachinesDelayedAsync(delayCts.Token);
        }

        private async Task PersistMachinesDelayedAsync(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(600, cancellationToken);
                await _archiveSemaphore.WaitAsync(cancellationToken);
                try
                {
                    if (_machines is null)
                    {
                        return;
                    }

                    var machineModels = await _dispatcher.InvokeAsync(
                        () => _machines.Select(x => x.ToModel()).ToList(),
                        DispatcherPriority.Background,
                        cancellationToken);

                    await Task.Run(() => _storageService.Save(machineModels), cancellationToken);
                }
                finally
                {
                    _archiveSemaphore.Release();
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private void ClearMonitorState(Guid machineId)
        {
            lock (_syncRoot)
            {
                if (_states.TryGetValue(machineId, out var state))
                {
                    state.CancellationTokenSource = null;
                    state.MonitorTask = null;
                }
            }
        }

        private void ResetMonitorAfterConfigurationChanged(MachineItemViewModel machine)
        {
            CancellationTokenSource? cts = null;
            lock (_syncRoot)
            {
                if (_states.TryGetValue(machine.Id, out var state))
                {
                    cts = state.CancellationTokenSource;
                    state.CancellationTokenSource = null;
                    state.MonitorTask = null;
                }
            }

            cts?.Cancel();
            cts?.Dispose();

            _ = _dispatcher.InvokeAsync(() =>
            {
                machine.IsPlcSynchronizing = false;
                machine.PlcStatusText = "PLC 參數已變更，請手動按下重新連線 PLC。";
            });
        }

        private sealed class MachineMonitorState
        {
            public CancellationTokenSource? CancellationTokenSource { get; set; }

            public Task? MonitorTask { get; set; }
        }
    }
}
