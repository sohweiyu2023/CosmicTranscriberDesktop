using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace CosmicTranscriber.Utilities;

/// <summary>
/// Records selector open/close and checks that the closed field's mouse hit target still covers
/// the field. A collapsed hit target is otherwise silent: keyboard opening keeps working while
/// mouse clicks fall through, which is exactly the 1.0.27 field defect.
/// </summary>
internal static class SelectorDiagnostics
{
    internal const string HitTargetPartName = "DropDownToggle";

    internal static void Attach(ComboBox comboBox)
    {
        ArgumentNullException.ThrowIfNull(comboBox);
        comboBox.DropDownOpened += (_, _) => Write(comboBox, "opened");
        comboBox.DropDownClosed += (_, _) => Write(comboBox, "closed");
        comboBox.Loaded += (_, _) => ScheduleInspection(comboBox);
    }

    /// <summary>Inspects after layout so theme changes and first render are measured, not guessed.</summary>
    internal static void ScheduleInspection(ComboBox comboBox)
    {
        if (comboBox.Dispatcher.HasShutdownStarted) return;
        comboBox.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => Inspect(comboBox)));
    }

    internal static bool HitTargetCoversField(double targetWidth, double targetHeight, double fieldWidth, double fieldHeight)
        => double.IsFinite(targetWidth) && double.IsFinite(targetHeight) && double.IsFinite(fieldWidth) && double.IsFinite(fieldHeight)
           && fieldWidth > 0 && fieldHeight > 0 && targetWidth >= fieldWidth - 1 && targetHeight >= fieldHeight - 1;

    private static void Inspect(ComboBox comboBox)
    {
        try
        {
            if (!comboBox.IsLoaded || comboBox.ActualWidth <= 0) return;
            comboBox.ApplyTemplate();
            var target = comboBox.Template?.FindName(HitTargetPartName, comboBox) as FrameworkElement;
            var stage = target is null ? "hit_target_missing"
                : HitTargetCoversField(target.ActualWidth, target.ActualHeight, comboBox.ActualWidth, comboBox.ActualHeight)
                    ? "hit_target_ok" : "hit_target_collapsed";
            Write(comboBox, stage, target);
        }
        catch (InvalidOperationException)
        {
            // Diagnostics must never affect the control.
        }
    }

    private static void Write(ComboBox comboBox, string stage, FrameworkElement? target = null)
    {
        target ??= comboBox.Template?.FindName(HitTargetPartName, comboBox) as FrameworkElement;
        StartupDiagnostics.WriteDropDown(comboBox.Name, stage, comboBox.SelectedIndex,
            target?.ActualWidth ?? double.NaN, target?.ActualHeight ?? double.NaN, comboBox.ActualWidth, comboBox.ActualHeight);
    }
}
