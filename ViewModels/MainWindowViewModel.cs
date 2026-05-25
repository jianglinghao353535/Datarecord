using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Datarecord.Models;
using Datarecord.Services;
using System.Windows.Controls.Primitives;
using System.Windows;

namespace Datarecord.ViewModels
{
    public sealed class MainWindowViewModel : ViewModelBase
    {
        private const double MachineCardWidth = 210;
        private const double MachineCardHeight = 188;
        private const double LayoutPadding = 20;
        private const double HorizontalSpacing = 28;
        private const double VerticalSpacing = 28;
        private const double CascadeOffset = 14;
        private const double MinDesignSurfaceHeight = 480;

        private readonly IMachineStorageService _storageService;
        private readonly RelayCommand _deleteSelectedCommand;
        private bool _useTraditionalChinese;
        private MachineItemViewModel? _selectedMachine;
        private string _statusText = "Drag a machine card from the left panel to the canvas to add a machine.";
        private double _designSurfaceWidth = (LayoutPadding * 2) + MachineCardWidth;
        private double _designSurfaceHeight = MinDesignSurfaceHeight;

        public MainWindowViewModel(IMachineStorageService storageService)
        {
            _storageService = storageService;
            Machines = new ObservableCollection<MachineItemViewModel>();
            PlcTypes = Enum.GetValues<PlcType>();

            SaveCommand = new RelayCommand(SaveLayout);
            LoadCommand = new RelayCommand(LoadLayout);
            ToggleLanguageCommand = new RelayCommand(ToggleLanguage);
            _deleteSelectedCommand = new RelayCommand(DeleteSelectedMachine, () => SelectedMachine is not null);
            DeleteSelectedCommand = _deleteSelectedCommand;

            LoadLayout();
        }

        public ObservableCollection<MachineItemViewModel> Machines { get; }

        public PlcType[] PlcTypes { get; }

        public ICommand SaveCommand { get; }

        public ICommand LoadCommand { get; }

        public ICommand DeleteSelectedCommand { get; }

        public ICommand ToggleLanguageCommand { get; }

        public bool UseTraditionalChinese => _useTraditionalChinese;

        public string LanguageToggleText => _useTraditionalChinese ? "English" : "ÖÐÎÄ";

        public string WindowTitleText => "Wire Drawing Machine Monitoring";

        public string HeaderTitleText => "Wire Drawing Machine Monitoring";

        public string ReloadText => "Reload";

        public string SaveLayoutText => "Save Layout";

        public string ReportScreenText => "Open Report";

        public string DatabaseSettingsText => "Database Settings";

        public string DeleteMachineText => "Delete Machine";

        public string ToolboxTitleText => "Toolbox";

        public string ToolboxHintText => "Drag the machine card to the center canvas to add a machine.";

        public string EnamelingMachineText => "Wire Drawing Machine";

        public string ToolboxCardHintText => "After dropping on the canvas, set PLC type, IP, and sample interval.";

        public string EnterMachineText => "Open Machine View";

        public string MachinePropertyTitleText => "Machine Properties";

        public string MachinePropertyHintText => "Select a machine on the canvas to edit its settings.";

        public string MachineNameLabelText => "Machine Name";

        public string IpAddressLabelText => "IP Address";

        public string PlcTypeLabelText => "PLC Type";

        public string PortLabelText => "Port";

        public string SampleIntervalLabelText => "Sample Interval (ms)";

        public string ReconnectPlcText => "Reconnect PLC";

        public string EnableMachineText => "Enable Machine";

        public string SuggestionTitleText => "Suggestion";

        public string SuggestionBodyText => "Siemens usually uses port 102; Delta/Inovance Modbus TCP usually uses port 502.";

        public MachineItemViewModel? SelectedMachine
        {
            get => _selectedMachine;
            private set
            {
                if (SetProperty(ref _selectedMachine, value))
                {
                    OnPropertyChanged(nameof(HasSelectedMachine));
                    _deleteSelectedCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public bool HasSelectedMachine => SelectedMachine is not null;

        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        public double DesignSurfaceWidth
        {
            get => _designSurfaceWidth;
            private set => SetProperty(ref _designSurfaceWidth, value);
        }

        public double DesignSurfaceHeight
        {
            get => _designSurfaceHeight;
            private set => SetProperty(ref _designSurfaceHeight, value);
        }

        public void AddMachine(double surfaceWidth)
        {
            AddMachine(surfaceWidth, LayoutPadding, LayoutPadding);
        }

        public void AddMachine(double surfaceWidth, double dropX, double dropY)
        {
            var machineIndex = Machines.Count + 1;
            var machine = new MachineItemViewModel(new MachineItemModel
            {
                Id = Guid.NewGuid(),
                Name = $"Machine {machineIndex}",
                IpAddress = "192.168.0.1",
                PlcType = PlcType.SiemensS7,
                Port = 102,
                SampleIntervalMs = 1000,
                X = LayoutPadding,
                Y = LayoutPadding,
                IsEnabled = true,
                ProductionSpeed = 0,
                ProductionLength = 0,
                ProductionWeight = 0,
                CurrentDiameter = 0,
                PlcAddressWeight = "MD108",
                PlcAddressRuningSignal = "M0.0",
                UseManualYAxis = false,
                ManualYAxisMin = 0,
                ManualYAxisMax = 2000,
                LengthYAxisMin = 0,
                LengthYAxisMax = 10000,
                DiameterYAxisMin = 0,
                DiameterYAxisMax = 5,
                SpeedYAxisMin = 0,
                SpeedYAxisMax = 2000,
                TensionYAxisMin = 0,
                TensionYAxisMax = 200
            });

            Machines.Add(machine);
            ArrangeMachinesInCascade(surfaceWidth);
            SelectMachine(machine);
            StatusText = $"Added {machine.Name}.";
        }

        public void ReorderMachine(MachineItemViewModel machine, double surfaceWidth, double pointerX, double pointerY)
        {
            var currentIndex = Machines.IndexOf(machine);
            if (currentIndex < 0)
            {
                return;
            }

            var targetIndex = GetTargetIndex(surfaceWidth, pointerX, pointerY, Machines.Count);
            if (targetIndex == currentIndex)
            {
                return;
            }

            Machines.Move(currentIndex, targetIndex);
            ArrangeMachinesInCascade(surfaceWidth);
            SelectMachine(machine);
        }

        public void MoveMachine(MachineItemViewModel machine, double deltaX, double deltaY, double surfaceWidth, double surfaceHeight)
        {
            var maxX = Math.Max(LayoutPadding, surfaceWidth - MachineCardWidth - LayoutPadding);
            var maxY = Math.Max(LayoutPadding, surfaceHeight - MachineCardHeight - LayoutPadding);

            machine.X = Math.Clamp(machine.X + deltaX, LayoutPadding, maxX);
            machine.Y = Math.Clamp(machine.Y + deltaY, LayoutPadding, maxY);
        }

        public void ArrangeMachinesInCascade(double surfaceWidth)
        {
            var effectiveWidth = Math.Max(
                MachineCardWidth + (LayoutPadding * 2),
                surfaceWidth);

            var availableWidth = Math.Max(MachineCardWidth, effectiveWidth - (LayoutPadding * 2));
            var columnCount = Math.Max(
                1,
                (int)((availableWidth + HorizontalSpacing) / (MachineCardWidth + HorizontalSpacing)));

            var usedWidth = (columnCount * MachineCardWidth) + ((columnCount - 1) * HorizontalSpacing);
            var startX = LayoutPadding + Math.Max(0, (availableWidth - usedWidth) / 2);

            for (var index = 0; index < Machines.Count; index++)
            {
                var row = index / columnCount;
                var column = index % columnCount;

                Machines[index].X = startX + (column * (MachineCardWidth + HorizontalSpacing));
                Machines[index].Y = LayoutPadding + (row * (MachineCardHeight + VerticalSpacing));
            }

            var rowCount = Math.Max(1, (int)Math.Ceiling(Machines.Count / (double)columnCount));

            DesignSurfaceWidth = Math.Max(effectiveWidth, startX + usedWidth + LayoutPadding);
            DesignSurfaceHeight = Math.Max(
                MinDesignSurfaceHeight,
                (LayoutPadding * 2) + (rowCount * MachineCardHeight) + ((rowCount - 1) * VerticalSpacing));
        }

        private int GetTargetIndex(double surfaceWidth, double pointerX, double pointerY, int itemCount)
        {
            if (itemCount <= 1)
            {
                return 0;
            }

            var availableWidth = Math.Max(MachineCardWidth, surfaceWidth - (LayoutPadding * 2));
            var columnCount = Math.Max(
                1,
                (int)((availableWidth + HorizontalSpacing) / (MachineCardWidth + HorizontalSpacing)));

            var usedWidth = (columnCount * MachineCardWidth) + ((columnCount - 1) * HorizontalSpacing);
            var startX = LayoutPadding + Math.Max(0, (availableWidth - usedWidth) / 2);

            var slotWidth = MachineCardWidth + HorizontalSpacing;
            var slotHeight = MachineCardHeight + VerticalSpacing;

            var column = (int)Math.Floor((pointerX - startX + (slotWidth / 2)) / slotWidth);
            var row = (int)Math.Floor((pointerY - LayoutPadding + (slotHeight / 2)) / slotHeight);

            column = Math.Clamp(column, 0, columnCount - 1);
            row = Math.Max(0, row);

            var index = (row * columnCount) + column;
            return Math.Clamp(index, 0, itemCount - 1);
        }

        public void SelectMachine(MachineItemViewModel? machine)
        {
            foreach (var item in Machines)
            {
                item.IsSelected = ReferenceEquals(item, machine);
            }

            SelectedMachine = machine;
            if (machine is null)
            {
                StatusText = "No machine selected.";
            }
            else
            {
                StatusText = $"Selected: {machine.Name}.";
            }
        }

        public void SaveLayout()
        {
            _storageService.Save(Machines.Select(x => x.ToModel()));
            StatusText = $"Layout saved. Total machines: {Machines.Count}.";
        }

        private void LoadLayout()
        {
            Machines.Clear();

            foreach (var model in _storageService.Load())
            {
                Machines.Add(new MachineItemViewModel(model));
            }

            SelectMachine(Machines.FirstOrDefault());
            StatusText = Machines.Count == 0
                ? "No machines found. Please add a machine first."
                : $"Loaded {Machines.Count} machine(s).";
        }

        private void DeleteSelectedMachine()
        {
            if (SelectedMachine is null)
            {
                return;
            }

            var machineName = SelectedMachine.Name;
            Machines.Remove(SelectedMachine);
            try
            {
                _storageService.Save(Machines.Select(x => x.ToModel()));
            }
            catch
            {
            }
            SelectMachine(Machines.FirstOrDefault());
            StatusText = $"Deleted {machineName}.";
        }

        private void ToggleLanguage()
        {
            _useTraditionalChinese = !_useTraditionalChinese;
            OnPropertyChanged(nameof(LanguageToggleText));
            OnPropertyChanged(nameof(WindowTitleText));
            OnPropertyChanged(nameof(HeaderTitleText));
            OnPropertyChanged(nameof(ReloadText));
            OnPropertyChanged(nameof(SaveLayoutText));
            OnPropertyChanged(nameof(ReportScreenText));
            OnPropertyChanged(nameof(DatabaseSettingsText));
            OnPropertyChanged(nameof(DeleteMachineText));
            OnPropertyChanged(nameof(ToolboxTitleText));
            OnPropertyChanged(nameof(ToolboxHintText));
            OnPropertyChanged(nameof(EnamelingMachineText));
            OnPropertyChanged(nameof(ToolboxCardHintText));
            OnPropertyChanged(nameof(EnterMachineText));
            OnPropertyChanged(nameof(MachinePropertyTitleText));
            OnPropertyChanged(nameof(MachinePropertyHintText));
            OnPropertyChanged(nameof(MachineNameLabelText));
            OnPropertyChanged(nameof(IpAddressLabelText));
            OnPropertyChanged(nameof(PlcTypeLabelText));
            OnPropertyChanged(nameof(PortLabelText));
            OnPropertyChanged(nameof(SampleIntervalLabelText));
            OnPropertyChanged(nameof(ReconnectPlcText));
            OnPropertyChanged(nameof(EnableMachineText));
            OnPropertyChanged(nameof(SuggestionTitleText));
            OnPropertyChanged(nameof(SuggestionBodyText));

            if (SelectedMachine is null)
            {
                StatusText = "No machine selected.";
            }
            else
            {
                StatusText = $"Selected: {SelectedMachine.Name}.";
            }
        }

        private string GetLocalized(string simplified, string traditional)
        {
            return _useTraditionalChinese ? traditional : simplified;
        }

        public void CommitMachineMove(MachineItemViewModel machine, double surfaceWidth)
        {
            var currentIndex = Machines.IndexOf(machine);
            if (currentIndex < 0)
            {
                return;
            }

            var dropX = machine.X + (MachineCardWidth / 2);
            var dropY = machine.Y + (MachineCardHeight / 2);
            var targetIndex = GetTargetIndex(surfaceWidth, dropX, dropY, Machines.Count);

            if (targetIndex != currentIndex)
            {
                Machines.Move(currentIndex, targetIndex);
            }

            ArrangeMachinesInCascade(surfaceWidth);
            SelectMachine(machine);
        }
    }
}