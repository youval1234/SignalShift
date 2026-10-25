using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SonarShift;

internal static class Motion
{
    internal static bool Enabled => SystemParameters.ClientAreaAnimation && SystemParameters.MenuAnimation && System.Windows.Media.RenderCapability.Tier > 0;
    private static void Animate(Animatable target, DependencyProperty property, double value, int milliseconds)
    {
        var from = (double)target.GetValue(property);
        target.SetValue(property, value);
        target.BeginAnimation(property, Enabled ? new DoubleAnimation(from, value, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop } : null, HandoffBehavior.SnapshotAndReplace);
    }
    internal static void Enter(FrameworkElement element)
    {
        if (!Enabled) return;
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(.65, 1, TimeSpan.FromMilliseconds(160)) { FillBehavior = FillBehavior.Stop }, HandoffBehavior.SnapshotAndReplace);
        if (element.RenderTransform is not TranslateTransform) element.RenderTransform = new TranslateTransform();
        var transform = (TranslateTransform)element.RenderTransform;
        transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(6, 0, TimeSpan.FromMilliseconds(180)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop }, HandoffBehavior.SnapshotAndReplace);
    }
    internal static void Wire(Button button)
    {
        if (Equals(button.Tag, "WindowCaption")) return;
        var scale = new ScaleTransform(1, 1); button.RenderTransform = scale; button.RenderTransformOrigin = new Point(.5, .5);
        void SetScale(double value) { Animate(scale, ScaleTransform.ScaleXProperty, value, 100); Animate(scale, ScaleTransform.ScaleYProperty, value, 100); }
        button.MouseEnter += (_, _) => SetScale(1.012);
        button.MouseLeave += (_, _) => SetScale(1);
        button.PreviewMouseLeftButtonDown += (_, _) => SetScale(.98);
        button.PreviewMouseLeftButtonUp += (_, _) => SetScale(button.IsMouseOver ? 1.012 : 1);
        button.LostMouseCapture += (_, _) => SetScale(button.IsMouseOver ? 1.012 : 1);
        button.Unloaded += (_, _) => { scale.BeginAnimation(ScaleTransform.ScaleXProperty, null); scale.BeginAnimation(ScaleTransform.ScaleYProperty, null); };
    }
}
