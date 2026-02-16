using System.Windows.Controls;
using ViperMigrate.App.ViewModels;

namespace ViperMigrate.App.Views;

public partial class RestoreView : UserControl
{
    public RestoreView()
    {
        InitializeComponent();
    }

    private void PasswordBox_PasswordChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is RestoreViewModel vm)
            vm.Password = PasswordBox.Password;
    }
}
