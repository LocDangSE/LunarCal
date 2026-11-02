using System.Windows;
using System.Windows.Controls;

namespace LunarCal.App.Behaviors;

/// <summary>
/// Attached properties for <see cref="ScrollViewer"/>. <c>InitialVerticalOffset</c> scrolls the time grid of the
/// Week and Day views to 07:00 the first time it is shown (Figma S06/S07), without code-behind in the views.
/// </summary>
public static class ScrollViewerBehavior
{
    /// <summary>Vertical offset applied once, when the scroll viewer is first laid out and visible.</summary>
    public static readonly DependencyProperty InitialVerticalOffsetProperty = DependencyProperty.RegisterAttached(
        "InitialVerticalOffset",
        typeof(double),
        typeof(ScrollViewerBehavior),
        new PropertyMetadata(double.NaN, OnInitialVerticalOffsetChanged));

    private static readonly DependencyProperty _appliedProperty = DependencyProperty.RegisterAttached(
        "Applied", typeof(bool), typeof(ScrollViewerBehavior), new PropertyMetadata(false));

    public static double GetInitialVerticalOffset(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (double)element.GetValue(InitialVerticalOffsetProperty);
    }

    public static void SetInitialVerticalOffset(DependencyObject element, double value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(InitialVerticalOffsetProperty, value);
    }

    private static void OnInitialVerticalOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer viewer)
        {
            return;
        }

        viewer.Loaded -= OnLoadedOrShown;
        viewer.IsVisibleChanged -= OnVisibleChanged;
        viewer.Loaded += OnLoadedOrShown;
        viewer.IsVisibleChanged += OnVisibleChanged;
        TryApply(viewer);
    }

    private static void OnLoadedOrShown(object sender, RoutedEventArgs e) => TryApply((ScrollViewer)sender);

    private static void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) => TryApply((ScrollViewer)sender);

    private static void TryApply(ScrollViewer viewer)
    {
        var offset = GetInitialVerticalOffset(viewer);
        if (double.IsNaN(offset) || (bool)viewer.GetValue(_appliedProperty) || !viewer.IsLoaded || !viewer.IsVisible)
        {
            return;
        }

        viewer.SetValue(_appliedProperty, true);

        // Wait for the first layout pass so the extent is known.
        viewer.Dispatcher.BeginInvoke(() => viewer.ScrollToVerticalOffset(offset), System.Windows.Threading.DispatcherPriority.Loaded);
    }
}
