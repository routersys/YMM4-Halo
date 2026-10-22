using System.Windows;
using GitHubUpdateNotifier;
using YukkuriMovieMaker.Plugin.Update;
using Notifier = GitHubUpdateNotifier.GitHubUpdateNotifier;

namespace Halo;

internal static class HaloUpdateNotifier
{
    private const string Owner = "routersys";
    private const string Repository = "YMM4-Halo";
    private static int _started;

    public static void EnsureCheckedOnce()
    {
        if (Application.Current is null || Interlocked.Exchange(ref _started, 1) != 0)
            return;
        _ = RunAsync();
    }

    private static async Task RunAsync()
    {
        try
        {
            var options = new UpdateNotifierOptions
            {
                CurrentVersion = PluginVersion.FromAssemblyInformationalVersion(typeof(HaloUpdateNotifier)),
                IncludePrerelease = false,
                MessageFormatter = FormatMessage,
                NotificationHandler = ShowNotificationAsync,
            };
            await new Notifier(Owner, Repository, options).NotifyAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            if (exception is not UpdateCheckException)
                HaloTelemetry.Report(exception);
        }
    }

    private static string FormatMessage(UpdateInfo update)
        => string.Format(Texts.UpdateAvailableMessage, update.TagName) + Environment.NewLine + update.ReleaseUrl;

    private static Task ShowNotificationAsync(UpdateNotification notification, CancellationToken cancellationToken)
    {
        var application = Application.Current;
        if (application is null)
            return Task.CompletedTask;
        return application.Dispatcher.InvokeAsync(() =>
            MessageBox.Show(notification.Message, Texts.Halo, MessageBoxButton.OK, MessageBoxImage.Information)).Task;
    }
}
