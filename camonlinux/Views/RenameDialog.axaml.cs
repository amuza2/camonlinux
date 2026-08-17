using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace camonlinux.Views;

public partial class RenameDialog : Window
{
    public RenameDialog(string currentBaseName)
    {
        InitializeComponent();
        NameBox.Text = currentBaseName;
        NameBox.SelectAll();
        NameBox.Focus();
    }

    private void OnOk(object? sender, RoutedEventArgs e) => Close(NameBox.Text?.Trim());

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
