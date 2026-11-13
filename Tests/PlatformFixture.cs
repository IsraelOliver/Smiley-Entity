using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Smiley;

// Interactive test fixture in the TEST executable, not another diagnostic/product window.
internal static class PlatformFixture
{
    internal static int Run()
    {
        var window = new Window
        {
            Title = "Smiley — janela de teste de plataformas", Width = 420, Height = 180,
            ShowActivated = false, Background = Brushes.AliceBlue,
            Content = new TextBlock { Text = "Janela de teste\nSolte o Smiley sobre a borda superior.\nMover, minimizar e fechar deve atualizar o apoio.", Margin = new Thickness(16), FontSize = 14, TextWrapping = TextWrapping.Wrap }
        };
        window.Loaded += (_, _) =>
        {
            nint hwnd = new WindowInteropHelper(window).Handle;
            var area = NativeMethods.GetWorkArea(hwnd);
            NativeMethods.SetWindowPos(hwnd, 0, (int)area.Right - 470, (int)area.Bottom - 192, 420, 180, 0x0014);
        };
        return new Application().Run(window);
    }
}
