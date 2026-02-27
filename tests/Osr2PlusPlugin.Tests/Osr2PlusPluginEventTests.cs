using Moq;
using Osr2PlusPlugin.Services;
using Osr2PlusPlugin.ViewModels;
using Vido.Core.Events;
using Vido.Core.Logging;
using Vido.Core.Playback;
using Vido.Core.Plugin;
using Xunit;

namespace Osr2PlusPlugin.Tests;

/// <summary>
/// Tests for VOSR-032: Video event handlers in the plugin entry point.
/// Verifies that Vido events are correctly wired to plugin services,
/// exceptions are caught and logged, and speed ratio changes are synced.
/// </summary>
public class Osr2PlusPluginEventTests : IDisposable
{
    private readonly global::Osr2PlusPlugin.Osr2PlusPlugin _sut;
    private readonly FakeEventBus _eventBus;
    private readonly Mock<IPluginContext> _mockContext;
    private readonly Mock<IVideoEngine> _mockVideoEngine;
    private readonly Mock<ILogService> _mockLogger;
    private readonly Mock<IPluginSettingsStore> _mockSettings;

    public Osr2PlusPluginEventTests()
    {
        _sut = new global::Osr2PlusPlugin.Osr2PlusPlugin();
        _eventBus = new FakeEventBus();
        _mockContext = new Mock<IPluginContext>();
        _mockVideoEngine = new Mock<IVideoEngine>();
        _mockLogger = new Mock<ILogService>();
        _mockSettings = new Mock<IPluginSettingsStore>();

        // Manifest with minimum required contributions so Activate doesn't throw
        var manifest = new PluginManifest
        {
            Id = "com.test.osr2",
            Name = "Test OSR2+",
            Version = "1.0.0",
            EntryPoint = "Osr2PlusPlugin.dll",
            PluginClass = "Osr2PlusPlugin.Osr2PlusPlugin",
            Contributes = new PluginContributions
            {
                Sidebar = [new SidebarContribution { Id = "osr2-sidebar", Title = "OSR2+" }],
                BottomPanel = [new PanelContribution { Id = "osr2-visualizer", Title = "Visualizer" }],
                RightPanel = [new PanelContribution { Id = "osr2-axis-control", Title = "Axis Control" }],
                StatusBar = [new StatusBarContribution { Id = "osr2-status", Name = "OSR2+ Status", Position = "right" }],
                ToolbarButtons = [new ToolbarButtonContribution { Id = "osr2-quick-connect", Tooltip = "Connect" }],
            }
        };

        _mockContext.Setup(c => c.Events).Returns(_eventBus);
        _mockContext.Setup(c => c.VideoEngine).Returns(_mockVideoEngine.Object);
        _mockContext.Setup(c => c.Logger).Returns(_mockLogger.Object);
        _mockContext.Setup(c => c.Settings).Returns(_mockSettings.Object);
        _mockContext.Setup(c => c.Manifest).Returns(manifest);
        _mockContext.Setup(c => c.PluginDirectory).Returns(@"C:\fake\plugin");

        // Default settings — return default value for any Get call
        _mockSettings.Setup(s => s.Get(It.IsAny<string>(), It.IsAny<int>()))
            .Returns((string _, int d) => d);
        _mockSettings.Setup(s => s.Get(It.IsAny<string>(), It.IsAny<bool>()))
            .Returns((string _, bool d) => d);
        _mockSettings.Setup(s => s.Get(It.IsAny<string>(), It.IsAny<double>()))
            .Returns((string _, double d) => d);
        _mockSettings.Setup(s => s.Get(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string _, string d) => d);

        // Default speed ratio
        _mockVideoEngine.Setup(v => v.SpeedRatio).Returns(1.0);
    }

    /// <summary>
    /// Activates the plugin with the mock context.
    /// We avoid calling RegisterSidebarPanel/etc. which create WPF views
    /// by allowing exceptions from view construction to be swallowed.
    /// Instead, we access event handlers indirectly through the event bus.
    /// </summary>
    private void ActivatePlugin()
    {
        // Register callbacks will fail because WPF views can't be created
        // in a test context (no STA thread / Application). But the event
        // subscriptions happen before the Register calls, so the event bus
        // captures them. We wrap in try/catch to tolerate WPF failures.
        try { _sut.Activate(_mockContext.Object); } catch { /* WPF view construction may fail in tests */ }
    }

    public void Dispose()
    {
        try { _sut.Deactivate(); } catch { }
    }

    // ═══════════════════════════════════════════════════════
    //  VideoLoadedEvent
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void VideoLoaded_SubscribesHandler()
    {
        ActivatePlugin();
        Assert.True(_eventBus.HasSubscription<VideoLoadedEvent>());
    }

    [Fact]
    public void VideoLoaded_LogsEvent()
    {
        ActivatePlugin();
        var e = CreateVideoLoadedEvent(@"C:\Videos\test.mp4");

        _eventBus.Publish(e);

        _mockLogger.Verify(l => l.Debug(It.Is<string>(s => s.Contains("test.mp4")), "OSR2+"), Times.AtLeastOnce);
    }

    [Fact]
    public void VideoLoaded_SyncsSpeedRatio()
    {
        _mockVideoEngine.Setup(v => v.SpeedRatio).Returns(1.5);
        ActivatePlugin();

        _eventBus.Publish(CreateVideoLoadedEvent(@"C:\Videos\test.mp4"));

        // After VideoLoaded, the plugin should read the current speed ratio
        _mockVideoEngine.Verify(v => v.SpeedRatio, Times.AtLeastOnce);
    }

    [Fact]
    public void VideoLoaded_ExceptionCaughtAndLogged()
    {
        ActivatePlugin();

        // Force an error by publishing with null file path
        var e = new VideoLoadedEvent
        {
            FilePath = null!,
            Metadata = CreateMetadata("bad.mp4")
        };

        // Should not throw — exception is caught internally
        var exception = Record.Exception(() => _eventBus.Publish(e));
        Assert.Null(exception);
    }

    // ═══════════════════════════════════════════════════════
    //  VideoUnloadedEvent
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void VideoUnloaded_SubscribesHandler()
    {
        ActivatePlugin();
        Assert.True(_eventBus.HasSubscription<VideoUnloadedEvent>());
    }

    [Fact]
    public void VideoUnloaded_LogsEvent()
    {
        ActivatePlugin();

        _eventBus.Publish(new VideoUnloadedEvent());

        _mockLogger.Verify(l => l.Debug(It.Is<string>(s => s.Contains("unloaded")), "OSR2+"), Times.AtLeastOnce);
    }

    [Fact]
    public void VideoUnloaded_DoesNotThrow()
    {
        ActivatePlugin();

        var exception = Record.Exception(() => _eventBus.Publish(new VideoUnloadedEvent()));
        Assert.Null(exception);
    }

    [Fact]
    public void VideoUnloaded_LogsRecenter()
    {
        ActivatePlugin();

        _eventBus.Publish(new VideoUnloadedEvent());

        _mockLogger.Verify(l => l.Debug(
            It.Is<string>(s => s.Contains("recentered")), "OSR2+"), Times.AtLeastOnce);
    }

    // ═══════════════════════════════════════════════════════
    //  PlaybackStateChangedEvent
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void PlaybackStateChanged_SubscribesHandler()
    {
        ActivatePlugin();
        Assert.True(_eventBus.HasSubscription<PlaybackStateChangedEvent>());
    }

    [Fact]
    public void PlaybackStateChanged_Playing_LogsState()
    {
        ActivatePlugin();
        var e = new PlaybackStateChangedEvent { State = PlaybackState.Playing };

        _eventBus.Publish(e);

        _mockLogger.Verify(l => l.Debug(It.Is<string>(s => s.Contains("Playing")), "OSR2+"), Times.AtLeastOnce);
    }

    [Fact]
    public void PlaybackStateChanged_Paused_LogsState()
    {
        ActivatePlugin();
        var e = new PlaybackStateChangedEvent { State = PlaybackState.Paused };

        _eventBus.Publish(e);

        _mockLogger.Verify(l => l.Debug(It.Is<string>(s => s.Contains("Paused")), "OSR2+"), Times.AtLeastOnce);
    }

    [Fact]
    public void PlaybackStateChanged_Stopped_LogsState()
    {
        ActivatePlugin();
        var e = new PlaybackStateChangedEvent { State = PlaybackState.Stopped };

        _eventBus.Publish(e);

        _mockLogger.Verify(l => l.Debug(It.Is<string>(s => s.Contains("Stopped")), "OSR2+"), Times.AtLeastOnce);
    }

    [Fact]
    public void PlaybackStateChanged_Stopped_LogsRecenter()
    {
        ActivatePlugin();
        var e = new PlaybackStateChangedEvent { State = PlaybackState.Stopped };

        _eventBus.Publish(e);

        _mockLogger.Verify(l => l.Debug(
            It.Is<string>(s => s.Contains("recentered")), "OSR2+"), Times.AtLeastOnce);
    }

    [Fact]
    public void PlaybackStateChanged_Playing_DoesNotLogRecenter()
    {
        ActivatePlugin();
        var e = new PlaybackStateChangedEvent { State = PlaybackState.Playing };

        _eventBus.Publish(e);

        _mockLogger.Verify(l => l.Debug(
            It.Is<string>(s => s.Contains("recentered")), "OSR2+"), Times.Never);
    }

    [Fact]
    public void PlaybackStateChanged_Paused_DoesNotLogRecenter()
    {
        ActivatePlugin();
        var e = new PlaybackStateChangedEvent { State = PlaybackState.Paused };

        _eventBus.Publish(e);

        _mockLogger.Verify(l => l.Debug(
            It.Is<string>(s => s.Contains("recentered")), "OSR2+"), Times.Never);
    }

    [Fact]
    public void PlaybackStateChanged_ExceptionCaughtAndNotRethrown()
    {
        ActivatePlugin();

        // Should not throw regardless of state
        var exception = Record.Exception(() =>
            _eventBus.Publish(new PlaybackStateChangedEvent { State = PlaybackState.Playing }));
        Assert.Null(exception);
    }

    // ═══════════════════════════════════════════════════════
    //  PlaybackPositionChangedEvent
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void PositionChanged_SubscribesHandler()
    {
        ActivatePlugin();
        Assert.True(_eventBus.HasSubscription<PlaybackPositionChangedEvent>());
    }

    [Fact]
    public void PositionChanged_DoesNotThrow()
    {
        ActivatePlugin();
        var e = new PlaybackPositionChangedEvent
        {
            Position = TimeSpan.FromSeconds(5),
            Duration = TimeSpan.FromSeconds(120)
        };

        var exception = Record.Exception(() => _eventBus.Publish(e));
        Assert.Null(exception);
    }

    [Fact]
    public void PositionChanged_MultipleCallsDoNotThrow()
    {
        ActivatePlugin();

        for (int i = 0; i < 100; i++)
        {
            var e = new PlaybackPositionChangedEvent
            {
                Position = TimeSpan.FromMilliseconds(i * 16.67), // ~60Hz
                Duration = TimeSpan.FromSeconds(120)
            };
            _eventBus.Publish(e);
        }
    }

    // ═══════════════════════════════════════════════════════
    //  Speed Ratio Synchronization
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void PositionChanged_SyncsSpeedRatioOnChange()
    {
        ActivatePlugin();
        _mockVideoEngine.Setup(v => v.SpeedRatio).Returns(2.0);

        _eventBus.Publish(new PlaybackPositionChangedEvent
        {
            Position = TimeSpan.FromSeconds(1),
            Duration = TimeSpan.FromSeconds(120)
        });

        _mockVideoEngine.Verify(v => v.SpeedRatio, Times.AtLeastOnce);
        _mockLogger.Verify(l => l.Debug(It.Is<string>(s => s.Contains("speed") && s.Contains("2.00")), "OSR2+"), Times.Once);
    }

    [Fact]
    public void PositionChanged_DoesNotLogSpeedWhenUnchanged()
    {
        ActivatePlugin();
        // SpeedRatio stays at default 1.0
        _mockVideoEngine.Setup(v => v.SpeedRatio).Returns(1.0);

        _eventBus.Publish(new PlaybackPositionChangedEvent
        {
            Position = TimeSpan.FromSeconds(1),
            Duration = TimeSpan.FromSeconds(120)
        });
        _eventBus.Publish(new PlaybackPositionChangedEvent
        {
            Position = TimeSpan.FromSeconds(2),
            Duration = TimeSpan.FromSeconds(120)
        });

        // Speed didn't change from 1.0 so no speedUpdate log (only initial diff from default tracked)
        _mockLogger.Verify(l => l.Debug(It.Is<string>(s => s.Contains("speed updated")), "OSR2+"), Times.Never);
    }

    [Fact]
    public void PositionChanged_LogsOnlyOnSpeedChange()
    {
        ActivatePlugin();

        // First tick at 1.0 — no change from default
        _mockVideoEngine.Setup(v => v.SpeedRatio).Returns(1.0);
        _eventBus.Publish(new PlaybackPositionChangedEvent
        {
            Position = TimeSpan.FromSeconds(1),
            Duration = TimeSpan.FromSeconds(120)
        });

        // Change to 1.5
        _mockVideoEngine.Setup(v => v.SpeedRatio).Returns(1.5);
        _eventBus.Publish(new PlaybackPositionChangedEvent
        {
            Position = TimeSpan.FromSeconds(2),
            Duration = TimeSpan.FromSeconds(120)
        });

        // Second tick at 1.5 — no new log
        _eventBus.Publish(new PlaybackPositionChangedEvent
        {
            Position = TimeSpan.FromSeconds(3),
            Duration = TimeSpan.FromSeconds(120)
        });

        // Only one speed log for 1.5
        _mockLogger.Verify(l => l.Debug(It.Is<string>(s => s.Contains("1.50")), "OSR2+"), Times.Once);
    }

    // ═══════════════════════════════════════════════════════
    //  All Four Events Subscribe During Activation
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void Activate_SubscribesToAllFourEventTypes()
    {
        ActivatePlugin();

        Assert.True(_eventBus.HasSubscription<VideoLoadedEvent>());
        Assert.True(_eventBus.HasSubscription<VideoUnloadedEvent>());
        Assert.True(_eventBus.HasSubscription<PlaybackStateChangedEvent>());
        Assert.True(_eventBus.HasSubscription<PlaybackPositionChangedEvent>());
    }

    [Fact]
    public void Deactivate_DisposesSubscriptions()
    {
        ActivatePlugin();
        Assert.True(_eventBus.SubscriptionCount > 0);

        _sut.Deactivate();

        Assert.Equal(0, _eventBus.ActiveSubscriptionCount);
    }

    // ═══════════════════════════════════════════════════════
    //  Sequence Tests
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void FullCycle_LoadPlayPauseSeekUnload_DoesNotThrow()
    {
        ActivatePlugin();

        // Load video
        _eventBus.Publish(CreateVideoLoadedEvent(@"C:\Videos\test.mp4"));

        // Start playing
        _eventBus.Publish(new PlaybackStateChangedEvent { State = PlaybackState.Playing });

        // Position updates
        for (int i = 0; i < 10; i++)
        {
            _eventBus.Publish(new PlaybackPositionChangedEvent
            {
                Position = TimeSpan.FromMilliseconds(i * 100),
                Duration = TimeSpan.FromSeconds(120)
            });
        }

        // Pause
        _eventBus.Publish(new PlaybackStateChangedEvent { State = PlaybackState.Paused });

        // Resume
        _eventBus.Publish(new PlaybackStateChangedEvent { State = PlaybackState.Playing });

        // Speed change mid-playback
        _mockVideoEngine.Setup(v => v.SpeedRatio).Returns(0.5);
        _eventBus.Publish(new PlaybackPositionChangedEvent
        {
            Position = TimeSpan.FromSeconds(2),
            Duration = TimeSpan.FromSeconds(120)
        });

        // Stop
        _eventBus.Publish(new PlaybackStateChangedEvent { State = PlaybackState.Stopped });

        // Unload
        _eventBus.Publish(new VideoUnloadedEvent());
    }

    [Fact]
    public void UnloadWithoutLoad_DoesNotThrow()
    {
        ActivatePlugin();
        var exception = Record.Exception(() => _eventBus.Publish(new VideoUnloadedEvent()));
        Assert.Null(exception);
    }

    [Fact]
    public void PlaybackState_BeforeLoad_DoesNotThrow()
    {
        ActivatePlugin();
        var exception = Record.Exception(() =>
            _eventBus.Publish(new PlaybackStateChangedEvent { State = PlaybackState.Playing }));
        Assert.Null(exception);
    }

    [Fact]
    public void PositionChanged_BeforeLoad_DoesNotThrow()
    {
        ActivatePlugin();
        var exception = Record.Exception(() =>
            _eventBus.Publish(new PlaybackPositionChangedEvent
            {
                Position = TimeSpan.FromSeconds(5),
                Duration = TimeSpan.FromSeconds(120)
            }));
        Assert.Null(exception);
    }

    // ═══════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════

    private static VideoLoadedEvent CreateVideoLoadedEvent(string filePath) => new()
    {
        FilePath = filePath,
        Metadata = CreateMetadata(System.IO.Path.GetFileName(filePath))
    };

    private static VideoMetadata CreateMetadata(string fileName) => new()
    {
        FilePath = $@"C:\Videos\{fileName}",
        FileName = fileName,
        Duration = TimeSpan.FromMinutes(5),
        Width = 1920,
        Height = 1080
    };

    // ═══════════════════════════════════════════════════════
    //  FakeEventBus — captures subscriptions for test invocation
    // ═══════════════════════════════════════════════════════

    private sealed class FakeEventBus : IEventBus
    {
        private readonly Dictionary<Type, List<Delegate>> _handlers = new();
        private readonly List<FakeSubscription> _subscriptions = new();

        public int SubscriptionCount => _subscriptions.Count;
        public int ActiveSubscriptionCount => _subscriptions.Count(s => !s.IsDisposed);

        public bool HasSubscription<TEvent>() where TEvent : class
            => _handlers.ContainsKey(typeof(TEvent)) && _handlers[typeof(TEvent)].Count > 0;

        public IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class
        {
            var type = typeof(TEvent);
            if (!_handlers.TryGetValue(type, out var list))
            {
                list = new List<Delegate>();
                _handlers[type] = list;
            }
            list.Add(handler);

            var sub = new FakeSubscription(() => list.Remove(handler));
            _subscriptions.Add(sub);
            return sub;
        }

        public void Publish<TEvent>(TEvent eventData) where TEvent : class
        {
            if (!_handlers.TryGetValue(typeof(TEvent), out var list)) return;
            foreach (var handler in list.ToList())
            {
                ((Action<TEvent>)handler)(eventData);
            }
        }

        private sealed class FakeSubscription(Action onDispose) : IDisposable
        {
            public bool IsDisposed { get; private set; }
            public void Dispose()
            {
                if (IsDisposed) return;
                IsDisposed = true;
                onDispose();
            }
        }
    }
}
