using System.Windows;
using DupliXon.App.Interop;

namespace DupliXon.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            // Without system acrylic (Windows 10) fall back to an opaque tint.
            if (!NativeMethods.ApplyGlass(this)) Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x0B, 0x0D, 0x18));
        };
    }

    public void BringToFront()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    // ---- Credits --------------------------------------------------------------------------

    private void Credits_Click(object sender, RoutedEventArgs e)
    {
        var v = typeof(MainWindow).Assembly.GetName().Version;
        VersionText.Text = v is null ? "" : $"Versión {v.Major}.{v.Minor}.{v.Build}";
        CreditsSheet.Visibility = Visibility.Visible;
    }

    private void CloseCredits_Click(object sender, RoutedEventArgs e) => CreditsSheet.Visibility = Visibility.Collapsed;
    private void CreditsBackdrop_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => CreditsSheet.Visibility = Visibility.Collapsed;
    private void CreditsCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => e.Handled = true;
    private void Email_Click(object sender, RoutedEventArgs e) => Open("mailto:njochoa8@gmail.com?subject=DupliXon");
    private void GitHub_Click(object sender, RoutedEventArgs e) => Open("https://github.com/NixonOchoa");

    private static void Open(string uri)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true }); }
        catch { /* no handler for the link */ }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
