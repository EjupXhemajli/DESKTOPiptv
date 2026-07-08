using System.Windows;
using ExIptv.ViewModels;

namespace ExIptv.Views;

public partial class SourceDialog : Window
{
    public SourceDialog()
    {
        InitializeComponent();
    }

    private SourceDialogViewModel? Vm => DataContext as SourceDialogViewModel;

    private void ModeAuto_Checked(object sender, RoutedEventArgs e)
    {
        if (AutoPanel is null || XtreamPanel is null) return;
        AutoPanel.Visibility = Visibility.Visible;
        XtreamPanel.Visibility = Visibility.Collapsed;
        if (Vm is not null) Vm.UseXtreamFields = false;
    }

    private void ModeXtream_Checked(object sender, RoutedEventArgs e)
    {
        if (AutoPanel is null || XtreamPanel is null) return;
        AutoPanel.Visibility = Visibility.Collapsed;
        XtreamPanel.Visibility = Visibility.Visible;
        if (Vm is not null) Vm.UseXtreamFields = true;
    }

    // PasswordBox unterstützt kein direktes Binding -> manuell ins ViewModel spiegeln.
    private void PasswordField_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (Vm is not null) Vm.Password = PasswordField.Password;
    }
}
