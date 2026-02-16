using System.Windows.Controls;
using ViperMigrate.App.ViewModels;

namespace ViperMigrate.App.Views;

public partial class CaptureView : UserControl
{
    public CaptureView()
    {
        InitializeComponent();
    }

    private void PasswordBox_PasswordChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is CaptureViewModel vm)
            vm.Password = PasswordBox.Password;
    }

    private void ConfirmPasswordBox_PasswordChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is CaptureViewModel vm)
            vm.ConfirmPassword = ConfirmPasswordBox.Password;
    }
}
