using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using VGList.App.Models;
using VGList.App.Services;
using VGList.App.Settings;
using VGList.Desktop.Main;
using VGList.Desktop.Services;
using VGList.Platform;

namespace VGList.Desktop;

public partial class App : Application
{
    private TrayIcon? _trayIcon;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // A0：本地 SQLite(WAL) 持久化仓储，主窗口与两个小组件共享同一实例。
            var (repository, _, startupNotice) = SampleData.OpenPersistentRepository();
            try
            {
                repository.PurgeExpiredTrash(30);
            }
            catch
            {
            }

            var settingsStore = new SettingsStore();
            var modelStore = new ModelProfileStore(PlatformServices.Secrets);
            var connectionTester = new ModelConnectionTester();
            var coordinator = new WidgetCoordinator(repository, settingsStore);
            AppTheme.Apply(coordinator.MainSettings.Theme, coordinator.MainSettings.FontPreset);

            // 主窗口是生命周期主体：隐藏到系统托盘时不退出，真实关闭时关闭所有小组件并释放资源。
            var main = new MainWindowViewModel(repository, coordinator, modelStore, connectionTester);
            var mainWindow = new MainWindow(main);
            var notificationService = PlatformServices.Notifications;
            var reminderScheduler = new ReminderScheduler(
                repository,
                args =>
                {
                    notificationService.Show(args.NotificationTitle, args.NotificationBody, args.Task.Id);
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (mainWindow.IsVisible)
                        {
                            main.Toast.Show($"🔔 {args.Task.Title}", durationMs: 5000);
                        }
                    });
                });
            var cleanedUp = false;
            var mainWindowShownOnce = !Program.StartMinimized;

            void ShowMainFromTray()
            {
                mainWindowShownOnce = true;
                if (desktop.MainWindow is null)
                {
                    desktop.MainWindow = mainWindow;
                }

                mainWindow.ShowFromTray();
            }

            notificationService.NotificationClicked += _ => Dispatcher.UIThread.Post(ShowMainFromTray);

            void OnActivateRequested(object? sender, EventArgs e) =>
                Dispatcher.UIThread.Post(ShowMainFromTray);

            void Cleanup()
            {
                if (cleanedUp)
                {
                    return;
                }

                cleanedUp = true;
                Program.ActivateRequested -= OnActivateRequested;
                reminderScheduler.Dispose();
                notificationService.Dispose();
                DisposeTrayIcon();
                coordinator.CloseAll();
                main.Dispose();
                if (repository is IDisposable disposableRepo)
                {
                    disposableRepo.Dispose();
                }
            }

            void RequestExit()
            {
                if (mainWindowShownOnce)
                {
                    mainWindow.ForceExit();
                }
                else
                {
                    Cleanup();
                    desktop.Shutdown();
                }
            }

            if (Program.StartMinimized)
            {
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            }
            else
            {
                desktop.MainWindow = mainWindow;
                desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            }

            SetupTrayIcon(ShowMainFromTray, RequestExit, coordinator);

            Program.ActivateRequested += OnActivateRequested;

            mainWindow.Opened += (_, _) =>
            {
                mainWindowShownOnce = true;
                if (!string.IsNullOrEmpty(startupNotice))
                {
                    main.Toast.Show(startupNotice, durationMs: 6000);
                }
            };

            mainWindow.Closed += (_, _) =>
            {
                Cleanup();
                if (desktop.ShutdownMode == ShutdownMode.OnExplicitShutdown)
                {
                    desktop.Shutdown();
                }
            };

            coordinator.InitializeWidgets();
            coordinator.InitializeMcpIfEnabled();
            reminderScheduler.Start();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void SetupTrayIcon(
        Action showMainWindow,
        Action exitApp,
        WidgetCoordinator coordinator)
    {
        try
        {
            var iconUri = OperatingSystem.IsMacOS()
                ? "avares://VGList/Assets/Plana/plana-mark.png"
                : "avares://VGList/Assets/Plana/plana.ico";
            using var iconStream = AssetLoader.Open(new Uri(iconUri));
            var icon = new WindowIcon(iconStream);

            var openItem = new NativeMenuItem("打开 Plana");
            openItem.Click += (_, _) => showMainWindow();

            var unlockClickThroughItem = new NativeMenuItem("解除小组件点击穿透");
            unlockClickThroughItem.Click += (_, _) => coordinator.DisableAllClickThrough();

            var exitItem = new NativeMenuItem("退出 Plana");
            exitItem.Click += (_, _) => exitApp();

            var menu = new NativeMenu();
            menu.Items.Add(openItem);
            menu.Items.Add(unlockClickThroughItem);
            menu.Items.Add(new NativeMenuItemSeparator());
            menu.Items.Add(exitItem);

            var trayIcon = new TrayIcon
            {
                Icon = icon,
                ToolTipText = "Plana",
                Menu = menu,
                IsVisible = true,
            };
            trayIcon.Clicked += (_, _) => showMainWindow();

            _trayIcon = trayIcon;
            TrayIcon.SetIcons(this, [trayIcon]);
        }
        catch
        {
            // 非桌面或无托盘环境下静默跳过，不影响主窗口运行
        }
    }

    private void DisposeTrayIcon()
    {
        if (_trayIcon is null)
        {
            return;
        }

        try
        {
            _trayIcon.IsVisible = false;
            _trayIcon.Dispose();
        }
        catch
        {
        }
        finally
        {
            _trayIcon = null;
            TrayIcon.SetIcons(this, []);
        }
    }
}
