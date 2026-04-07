using System.Windows;
using Datarecord.Services;
using Datarecord.ViewModels;

namespace Datarecord
{
    public partial class MySqlSettingsWindow : Window
    {
        private readonly MySqlSettingsService _settingsService;
        private readonly MySqlConnectionTestService _connectionTestService;

        public MySqlSettingsWindow(MySqlSettingsService settingsService)
        {
            InitializeComponent();
            _settingsService = settingsService;
            _connectionTestService = new MySqlConnectionTestService();
            DataContext = MySqlSettingsViewModel.FromModel(_settingsService.Load());
        }

        private MySqlSettingsViewModel ViewModel => (MySqlSettingsViewModel)DataContext;

        private void TestConnectionButton_OnClick(object sender, RoutedEventArgs e)
        {
            var result = _connectionTestService.Test(ViewModel.ToModel());
            ViewModel.StatusText = result.Message;
        }

        private void InitializeDatabaseButton_OnClick(object sender, RoutedEventArgs e)
        {
            var result = _connectionTestService.InitializeDatabase(ViewModel.ToModel());
            ViewModel.StatusText = result.Message;
        }

        private void ClearHistoryButton_OnClick(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "這會清空 machine_trend_records 內的所有歷史資料，是否繼續？",
                "確認清空歷史",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            var clearResult = _connectionTestService.ClearHistory(ViewModel.ToModel());
            ViewModel.StatusText = clearResult.Message;
        }

        private void SaveButton_OnClick(object sender, RoutedEventArgs e)
        {
            _settingsService.Save(ViewModel.ToModel());
            ViewModel.StatusText = "設定已儲存。";
            DialogResult = true;
        }

        private void CloseButton_OnClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
