using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Simple;
using Compositor.Core;
using System.Runtime.InteropServices;

namespace Compositor.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        NativeLibrary.SetDllImportResolver(typeof(NativeSelections).Assembly, (_, _, _) =>
        {
            string[] names = OperatingSystem.IsWindows()
                ? ["compositor_native.dll"]
                : OperatingSystem.IsMacOS() ? ["libcompositor_native.dylib", "compositor_native"]
                : ["libcompositor_native.so", "compositor_native"];
            foreach (string name in names)
            {
                string path = Path.Combine(AppContext.BaseDirectory, name);
                if (NativeLibrary.TryLoad(path, out nint handle)) return handle;
            }
            return 0;
        });
        AppBuilder.Configure<CompositorApplication>()
            .UsePlatformDetect()
            .With(new Win32PlatformOptions { RenderingMode = [Win32RenderingMode.Software], CompositionMode = [Win32CompositionMode.RedirectionSurface] })
            .With(new AvaloniaNativePlatformOptions { RenderingMode = [AvaloniaNativeRenderingMode.Software] })
            .StartWithClassicDesktopLifetime(args);
    }
}

public sealed class CompositorApplication : Application
{
    public override void Initialize() => Styles.Add(new SimpleTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }
}
