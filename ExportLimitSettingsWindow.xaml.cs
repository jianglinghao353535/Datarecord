using System.Windows;

namespace Datarecord
{
    public partial class ExportLimitSettingsWindow : Window
    {
        public ExportLimitSettingsWindow(object dataContext)
        {
            InitializeComponent();
            DataContext = dataContext;
        }

        private void ConfirmButton_OnClick(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void CancelButton_OnClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
