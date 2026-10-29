using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace FinanceTracker.Controls;

/// <summary>
/// Smooth mouse-wheel scrolling for every ScrollViewer. Each wheel notch moves a target offset and a per-frame
/// easing loop glides toward it, so quick spins accumulate and there is no snap-back at the top or bottom.
/// Nested scrollers (tables, lists) scroll first and hand over to the page when they reach their edge.
/// </summary>
public static class SmoothScroll
{
    private const double PixelsPerDelta = 1.0;   // wheel delta 120 -> 120 px
    private const double Stiffness = 15.0;       // higher = snappier glide

    private sealed class State
    {
        public double Target;
        public double Current;
        public bool Active;
    }

    private static readonly ConditionalWeakTable<ScrollViewer, State> States = new();
    private static readonly List<WeakReference<ScrollViewer>> Running = new();
    private static TimeSpan _lastFrame;
    private static bool _hooked;

    public static void Install()
    {
        EventManager.RegisterClassHandler(typeof(ScrollViewer), UIElement.PreviewMouseWheelEvent, new MouseWheelEventHandler(OnWheel));
        EventManager.RegisterClassHandler(typeof(ScrollViewer), UIElement.PreviewMouseDownEvent, new MouseButtonEventHandler(OnPress));
        EventManager.RegisterClassHandler(typeof(ScrollViewer), UIElement.PreviewKeyDownEvent, new KeyEventHandler((s, _) => Stop((ScrollViewer)s)));
    }

    private static double Position(ScrollViewer sv)
    {
        var st = States.GetOrCreateValue(sv);
        return st.Active ? st.Target : sv.VerticalOffset;
    }

    private static bool CanScroll(ScrollViewer sv, int delta)
    {
        if (sv.ScrollableHeight <= 0 || sv.VerticalScrollBarVisibility == ScrollBarVisibility.Disabled) return false;
        var pos = Position(sv);
        return delta < 0 ? pos < sv.ScrollableHeight - 0.5 : pos > 0.5;
    }

    private static ScrollViewer? NearestScroller(object? source)
    {
        var node = source as DependencyObject;
        while (node != null)
        {
            if (node is ScrollViewer sv) return sv;
            node = node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        }
        return null;
    }

    /// <summary>True when the scroller measures in items (virtualised lists), where pixel offsets do not apply.</summary>
    private static bool IsItemScrolling(ScrollViewer sv) => sv.CanContentScroll && sv.Content is ItemsPresenter;

    private static void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || sender is not ScrollViewer sv || Keyboard.Modifiers != ModifierKeys.None) return;

        // A nested scroller that can still move takes the wheel first.
        var nearest = NearestScroller(e.OriginalSource);
        if (nearest != null && !ReferenceEquals(nearest, sv) && CanScroll(nearest, e.Delta)) return;
        if (IsItemScrolling(sv)) return;

        var st = States.GetOrCreateValue(sv);
        if (!CanScroll(sv, e.Delta))
        {
            // At an edge while still gliding: swallow the notch so WPF's default handler cannot jolt the offset.
            if (st.Active && ReferenceEquals(nearest ?? sv, sv)) e.Handled = true;
            return;
        }

        if (!st.Active) { st.Current = sv.VerticalOffset; st.Target = st.Current; }
        st.Target = Math.Clamp(st.Target - e.Delta * PixelsPerDelta, 0, sv.ScrollableHeight);
        e.Handled = true;
        Start(sv, st);
    }

    private static void OnPress(object sender, MouseButtonEventArgs e)
    {
        // Dragging the scrollbar thumb or clicking its track should take over immediately.
        if (e.OriginalSource is DependencyObject d && FindAncestor<ScrollBar>(d) != null) Stop((ScrollViewer)sender);
    }

    private static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node != null)
        {
            if (node is T t) return t;
            node = node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        }
        return null;
    }

    private static void Start(ScrollViewer sv, State st)
    {
        if (!st.Active)
        {
            st.Active = true;
            Running.Add(new WeakReference<ScrollViewer>(sv));
        }
        if (_hooked) return;
        _hooked = true;
        _lastFrame = TimeSpan.Zero;
        CompositionTarget.Rendering += OnFrame;
    }

    private static void Stop(ScrollViewer sv)
    {
        if (States.TryGetValue(sv, out var st)) st.Active = false;
    }

    private static void OnFrame(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        var dt = _lastFrame == TimeSpan.Zero ? 1 / 60.0 : Math.Clamp((now - _lastFrame).TotalSeconds, 0.001, 0.05);
        _lastFrame = now;
        var blend = 1 - Math.Exp(-Stiffness * dt);

        for (var i = Running.Count - 1; i >= 0; i--)
        {
            if (!Running[i].TryGetTarget(out var sv) || !States.TryGetValue(sv, out var st) || !st.Active)
            {
                Running.RemoveAt(i);
                continue;
            }
            var target = Math.Min(st.Target, sv.ScrollableHeight);
            var diff = target - st.Current;
            if (Math.Abs(diff) < 0.35)
            {
                st.Current = target;
                sv.ScrollToVerticalOffset(target);
                st.Active = false;
                Running.RemoveAt(i);
                continue;
            }
            st.Current += diff * blend;
            sv.ScrollToVerticalOffset(st.Current);
        }

        if (Running.Count == 0)
        {
            CompositionTarget.Rendering -= OnFrame;
            _hooked = false;
        }
    }
}
