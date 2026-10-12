using System;
using AvaloniaDispatcher = Avalonia.Threading.Dispatcher;
using AvaloniaPriority = Avalonia.Threading.DispatcherPriority;

// The subset of WPF's System.Windows.Threading that upstream's engine files use, backed by Avalonia's UI thread.
// Lets DownloadStore.cs and BridgeServer.cs compile unchanged.
namespace System.Windows.Threading;

public enum DispatcherPriority { Background, Normal }

public sealed class Dispatcher
{
    static readonly Dispatcher Instance = new();

    /// Upstream only calls this on the UI thread, so it is always Avalonia's UI dispatcher.
    public static Dispatcher CurrentDispatcher => Instance;

    public Avalonia.Threading.DispatcherOperation InvokeAsync(Action callback) => AvaloniaDispatcher.UIThread.InvokeAsync(callback);

    public Avalonia.Threading.DispatcherOperation<T> InvokeAsync<T>(Func<T> callback) => AvaloniaDispatcher.UIThread.InvokeAsync(callback);

    internal static AvaloniaPriority Map(DispatcherPriority p) =>
        p == DispatcherPriority.Background ? AvaloniaPriority.Background : AvaloniaPriority.Normal;
}

/// Like WPF's, the constructor that takes a callback starts the timer.
public sealed class DispatcherTimer
{
    readonly Avalonia.Threading.DispatcherTimer _timer;

    public DispatcherTimer(TimeSpan interval, DispatcherPriority priority, EventHandler callback, Dispatcher dispatcher) =>
        _timer = new Avalonia.Threading.DispatcherTimer(interval, Dispatcher.Map(priority), callback);

    public void Start() => _timer.Start();
    public void Stop() => _timer.Stop();
}
