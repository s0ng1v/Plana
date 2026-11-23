using Avalonia;
using Avalonia.Media;
using System;
using System.IO;
using System.Threading;
using VGList.Widgets.Themes;

namespace VGList.Desktop;

internal static class Program
{
#if WINDOWS
    private const string InstanceMutexName = "VGList.Desktop.SingleInstance";
    private const string ActivateEventName = "VGList.Desktop.ActivateMain";

    private static EventWaitHandle? _activateHandle;
#endif

    public static bool StartMinimized { get; private set; }

#if WINDOWS
    public static event EventHandler? ActivateRequested;
#else
    public static event EventHandler? ActivateRequested
    {
        add { }
        remove { }
    }
#endif

    [STAThread]
    public static void Main(string[] args)
    {
        StartMinimized = args.Any(a =>
            string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a, "/minimized", StringComparison.OrdinalIgnoreCase));

#if WINDOWS
        using var mutex = new Mutex(initiallyOwned: true, InstanceMutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            if (StartMinimized)
            {
                return;
            }

            // 若已有实例藏在系统托盘中，再次启动时直接唤起其主窗口
            try
            {
                if (EventWaitHandle.TryOpenExisting(ActivateEventName, out var existing))
                {
                    using (existing)
                    {
                        existing.Set();
                    }
                }
            }
            catch
            {
            }

            return;
        }

        using var activateCts = new CancellationTokenSource();
        StartActivateListener(activateCts.Token);
        PlatformFactory.Configure();
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            activateCts.Cancel();
            _activateHandle?.Set();
            _activateHandle?.Dispose();
            _activateHandle = null;
        }
#else
        // macOS 首版用文件锁保证单实例；再次启动直接退出，不实现跨进程唤起。
        using var instanceLock = TryAcquireSingleInstanceLock();
        if (instanceLock is null)
        {
            return;
        }

        PlatformFactory.Configure();
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            instanceLock.Dispose();
        }
#endif
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .ConfigureFonts(fontManager =>
            {
                fontManager.AddFontCollection(new AnthropicFontCollection());
            })
            .With(new FontManagerOptions
            {
                FontFamilyMappings = AnthropicFontCollection.CreateMappings(),
            })
#if DEBUG
            .WithDeveloperTools()
#endif
            .LogToTrace();

#if WINDOWS
    private static void StartActivateListener(CancellationToken ct)
    {
        try
        {
            _activateHandle = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
            var thread = new Thread(() =>
            {
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        if (_activateHandle is null || !_activateHandle.WaitOne())
                        {
                            break;
                        }

                        if (!ct.IsCancellationRequested)
                        {
                            ActivateRequested?.Invoke(null, EventArgs.Empty);
                        }
                    }
                    catch
                    {
                        break;
                    }
                }
            })
            {
                IsBackground = true,
                Name = "Plana.ActivateListener",
            };
            thread.Start();
        }
        catch
        {
        }
    }
#else
    private static FileStream? TryAcquireSingleInstanceLock()
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VGList");
            Directory.CreateDirectory(directory);
            return new FileStream(
                Path.Combine(directory, "app.lock"),
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
        }
        catch
        {
            return null;
        }
    }
#endif
}
