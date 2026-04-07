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
        private MachineItemViewModel? _selectedMachine;
        private string _statusText = "將左側機台拖到中間畫布即可新增。";
        private double _designSurfaceWidth = (LayoutPadding * 2) + MachineCardWidth;
        private double _designSurfaceHeight = MinDesignSurfaceHeight;

        public MainWindowViewModel(IMachineStorageService storageService)
        {
            _storageService = storageService;
            Machines = new ObservableCollection<MachineItemViewModel>();
            PlcTypes = Enum.GetValues<PlcType>();

            SaveCommand = new RelayCommand(SaveLayout);
            LoadCommand = new RelayCommand(LoadLayout);
            _deleteSelectedCommand = new RelayCommand(DeleteSelectedMachine, () => SelectedMachine is not null);
            DeleteSelectedCommand = _deleteSelectedCommand;

            LoadLayout();
        }

        public ObservableCollection<MachineItemViewModel> Machines { get; }

        public PlcType[] PlcTypes { get; }

        public ICommand SaveCommand { get; }

        public ICommand LoadCommand { get; }

        public ICommand DeleteSelectedCommand { get; }

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
                Name = $"機台 {machineIndex}",
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
                ProductionStatus = "待機",
                CurrentDiameter = 0,
                CurrentTemperatures = new double[8]
            });

            Machines.Add(machine);
            ArrangeMachinesInCascade(surfaceWidth);
            SelectMachine(machine);
            StatusText = $"已新增 {machine.Name}。";
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
                StatusText = "目前未選取機台。";
            }
            else
            {
                StatusText = $"目前選取：{machine.Name}。";
            }
        }

        public void SaveLayout()
        {
            _storageService.Save(Machines.Select(x => x.ToModel()));
            StatusText = $"佈局已儲存，共 {Machines.Count} 台機台。";
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
                ? "目前沒有機台，請先新增。"
                : $"已載入 {Machines.Count} 台機台。";
        }

        private void DeleteSelectedMachine()
        {
            if (SelectedMachine is null)
            {
                return;
            }

            var machineName = SelectedMachine.Name;
            Machines.Remove(SelectedMachine);
            SelectMachine(Machines.FirstOrDefault());
            StatusText = $"已刪除 {machineName}。";
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