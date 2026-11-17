using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace TorManager.Desktop;

public sealed partial class MainWindow : Window
{
    public MainWindow() => AvaloniaXamlLoader.Load(this);
}
