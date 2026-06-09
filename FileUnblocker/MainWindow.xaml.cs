using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using FileUnblocker.ViewModels;
using Microsoft.Win32;

namespace FileUnblocker;

public partial class MainWindow : Window
{
    // ?? ViewModel ???????????????????????????????????????????????????????????
    private readonly MainViewModel _vm = new();

    // ?? Status brushes (UI only) ????????????????????????????????????????????
    private static readonly SolidColorBrush _statusReady = new(Color.FromRgb(0x10, 0xB9, 0x81));
    private static readonly SolidColorBrush _statusBusy  = new(Color.FromRgb(0xA7, 0x8B, 0xFA));
    private static readonly SolidColorBrush _statusDone  = new(Color.FromRgb(0x60, 0xA5, 0xFA));

    private bool _isLightTheme;

    // ?? Drag & drop state ???????????????????????????????????????????????????
    /// <summary>
    /// Prevents SetDropVisualState from being re-entered by overlapping
    /// DragEnter / DragLeave / Drop events that fire on multiple elements.
    /// Accessed only on the UI thread.
/// </summary>
    private bool _dropVisualActive;

    // ?? Animation lifetime guard ????????????????????????????????????????????
    /// <summary>
    /// Set to true when the window begins closing so that any in-flight
    /// CompositionTarget.Rendering ticks are no-ops instead of touching
    /// a partially-torn-down visual tree.
    /// </summary>
    private bool _windowClosing;

    // ?? Constructor ?????????????????????????????????????????????????????????
    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        _vm.PropertyChanged += OnVmPropertyChanged;
        _vm.ProcessingCompleted += OnProcessingCompleted;

        // Guard all CompositionTarget ticks once the window starts closing.
        Closing += (_, _) => _windowClosing = true;

        PlayWindowEntrance();
        ApplyTheme(false);
    }

    // ?? VM ? UI bridge: animate counters whenever ViewModel props change ????
    private int _dispTotal, _dispUnblocked, _dispFailed;

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
      case nameof(MainViewModel.TotalScanned):
                AnimateCounter(CardTotal, _dispTotal, _vm.TotalScanned);
      _dispTotal = _vm.TotalScanned; break;
            case nameof(MainViewModel.UnblockedCount):
    AnimateCounter(CardUnblocked, _dispUnblocked, _vm.UnblockedCount);
              _dispUnblocked = _vm.UnblockedCount; break;
    case nameof(MainViewModel.FailedCount):
                AnimateCounter(CardFailed, _dispFailed, _vm.FailedCount, TimeSpan.FromMilliseconds(700));
 _dispFailed = _vm.FailedCount; break;
            case nameof(MainViewModel.ProgressPercent):
           AnimateProgressBar(_vm.ProgressPercent);
break;
            case nameof(MainViewModel.HeaderStatus):
    var colour = _vm.IsBusy ? _statusBusy
    : _vm.ProgressPercent == 100 ? _statusDone
         : _statusReady;
     SetHeaderStatus(_vm.HeaderStatus, colour); break;
        }
    }

    // ?? Window entrance fade-in ?????????????????????????????????????????????
    private void PlayWindowEntrance()
    {
   Opacity = 0;
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(380))
 {
        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
      });
  }

    // ?? Animated counter ????????????????????????????????????????????????????
    /// <summary>
    /// Subscribes a single render-tick closure to CompositionTarget.Rendering.
    /// Guards: (a) null TextBlock, (b) window-closing flag so the static event
    /// handler never fires against a torn-down visual tree.
    /// </summary>
    private void AnimateCounter(TextBlock? tb, int from, int to, TimeSpan? duration = null)
 {
        if (tb is null) return;   // guard: named element not yet created

        var dur   = duration ?? TimeSpan.FromMilliseconds(600);
        var start = DateTime.UtcNow;
double ms = dur.TotalMilliseconds;

        EventHandler? tick = null;
        tick = (_, _) =>
        {
            // Stop immediately if the window is closing — prevents operating on
   // a partially-disposed visual tree and avoids COMException / IOEX.
  if (_windowClosing)
      {
   CompositionTarget.Rendering -= tick;
      return;
   }

       double t     = Math.Min((DateTime.UtcNow - start).TotalMilliseconds / ms, 1.0);
      double eased = 1 - Math.Pow(1 - t, 3);
      tb.Text = ((int)Math.Round(from + (to - from) * eased)).ToString();
   if (t >= 1.0) CompositionTarget.Rendering -= tick;
        };
        CompositionTarget.Rendering += tick;
    }

    // ?? Animated progress bar ???????????????????????????????????????????????
    private void AnimateProgressBar(double to)
    {
        ProgressBar.BeginAnimation(
   System.Windows.Controls.Primitives.RangeBase.ValueProperty,
            new DoubleAnimation(to, TimeSpan.FromMilliseconds(400))
            {
 EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
});
    }

    // ?? Header status indicator ?????????????????????????????????????????????
    private void SetHeaderStatus(string text, SolidColorBrush colour)
    {
     TxtAppStatus.Text  = text;
        TxtAppStatus.Foreground = colour;
        StatusDot.Fill = new RadialGradientBrush(colour.Color,
            Color.FromArgb(180, colour.Color.R, colour.Color.G, colour.Color.B));
        StatusDot.Effect = new System.Windows.Media.Effects.DropShadowEffect
     { Color = colour.Color, BlurRadius = 6, ShadowDepth = 0, Opacity = 0.9 };
    }

    // ?? Title bar / window controls ?????????????????????????????????????????
    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
  {
    if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

  private void BtnMinimize_Click(object sender, RoutedEventArgs e) =>
    WindowState = WindowState.Minimized;

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(180));
   fade.Completed += (_, _) => Close();
 BeginAnimation(OpacityProperty, fade);
    }

    // ?? Theme toggle ????????????????????????????????????????????????????????
    private void ThemeToggle_Checked(object sender, RoutedEventArgs e)   => ApplyTheme(true);
    private void ThemeToggle_Unchecked(object sender, RoutedEventArgs e) => ApplyTheme(false);

    private static void SetBrush(string key, Color color) =>
        Application.Current.Resources[key] = new SolidColorBrush(color);

    private void ApplyTheme(bool light)
    {
 _isLightTheme = light;
        if (!light)
      {
       SetBrush("TextPrimaryBrush",      Color.FromRgb(0xED, 0xE9, 0xFE));
        SetBrush("TextSecondaryBrush",    Color.FromRgb(0xC4, 0xB5, 0xFD));
    SetBrush("TextMutedBrush",        Color.FromRgb(0x7C, 0x7C, 0xA0));
          SetBrush("BgSurfaceBrush",        Color.FromRgb(0x1A, 0x1A, 0x2C));
       SetBrush("BgElevatedBrush",       Color.FromRgb(0x21, 0x21, 0x36));
    SetBrush("BgInputBrush", Color.FromRgb(0x25, 0x25, 0x40));
            SetBrush("BorderDefaultBrush",    Color.FromRgb(0x3A, 0x3A, 0x55));
            SetBrush("BorderHoverBrush",      Color.FromRgb(0x5A, 0x5A, 0x80));
            SetBrush("SecondaryDefaultBrush", Color.FromRgb(0x25, 0x25, 0x40));
         SetBrush("SecondaryHoverBrush",   Color.FromRgb(0x2E, 0x2E, 0x50));
   SetBrush("SecondaryActiveBrush",  Color.FromRgb(0x38, 0x38, 0x5E));
        SetBrush("PrimaryDefaultBrush",   Color.FromRgb(0x7C, 0x3A, 0xED));
   SetBrush("PrimaryHoverBrush", Color.FromRgb(0x8B, 0x5C, 0xF6));
    SetBrush("PrimaryFocusBrush",     Color.FromRgb(0x9D, 0x70, 0xFF));
          SetBrush("PrimaryDisabledBrush",  Color.FromRgb(0x3A, 0x2E, 0x55));
   SetBrush("AccentDefaultBrush",    Color.FromRgb(0xA7, 0x8B, 0xFA));
       SetBrush("StatusSuccessBrush",    Color.FromRgb(0x10, 0xB9, 0x81));
        SetBrush("StatusErrorBrush",    Color.FromRgb(0xF8, 0x71, 0x71));
SetBrush("StatusInfoBrush",       Color.FromRgb(0x60, 0xA5, 0xFA));
        }
 else
        {
   SetBrush("TextPrimaryBrush",      Color.FromRgb(0x1E, 0x1B, 0x2E));
   SetBrush("TextSecondaryBrush",    Color.FromRgb(0x4B, 0x46, 0x60));
        SetBrush("TextMutedBrush",    Color.FromRgb(0x6C, 0x6A, 0x80));
         SetBrush("BgSurfaceBrush",        Color.FromRgb(0xF7, 0xF7, 0xFB));
            SetBrush("BgElevatedBrush",       Color.FromRgb(0xFF, 0xFF, 0xFF));
            SetBrush("BgInputBrush",          Color.FromRgb(0xEE, 0xF0, 0xF8));
            SetBrush("BorderDefaultBrush",  Color.FromRgb(0xD0, 0xD5, 0xE5));
            SetBrush("BorderHoverBrush",      Color.FromRgb(0xB6, 0xBC, 0xD3));
    SetBrush("SecondaryDefaultBrush", Color.FromRgb(0xEE, 0xF0, 0xF8));
          SetBrush("SecondaryHoverBrush",   Color.FromRgb(0xE2, 0xE6, 0xF2));
            SetBrush("SecondaryActiveBrush",  Color.FromRgb(0xD5, 0xDA, 0xEA));
       SetBrush("PrimaryDefaultBrush",   Color.FromRgb(0x6D, 0x28, 0xD9));
    SetBrush("PrimaryHoverBrush",     Color.FromRgb(0x7C, 0x3A, 0xED));
        SetBrush("PrimaryFocusBrush",     Color.FromRgb(0x8B, 0x5C, 0xF6));
     SetBrush("PrimaryDisabledBrush",  Color.FromRgb(0xB8, 0xC0, 0xD8));
            SetBrush("AccentDefaultBrush",    Color.FromRgb(0x6D, 0x28, 0xD9));
            SetBrush("StatusSuccessBrush",    Color.FromRgb(0x16, 0xA3, 0x4A));
            SetBrush("StatusErrorBrush",      Color.FromRgb(0xDC, 0x26, 0x26));
   SetBrush("StatusInfoBrush",       Color.FromRgb(0x25, 0x63, 0xEB));
        }
    }

    // ?? Row actions (UI only – Tag binding) ?????????????????????????????????
    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string dir } && Directory.Exists(dir))
            System.Diagnostics.Process.Start("explorer.exe", dir);
    }

    private void CopyPath_Click(object sender, RoutedEventArgs e)
    {
  if (sender is Button { Tag: string path })
    Clipboard.SetText(path);
    }

    // ?? Row count badge ?????????????????????????????????????????????????????
  private void LogList_CollectionChanged(object? sender,
        System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
TxtRowCount.Text   = _vm.Items.Count.ToString();
        BadgeCount.Visibility = _vm.Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
  TxtLogHeader.Text     = _vm.Items.Count > 0 ? $"Results ({_vm.Items.Count})" : "Results";
    }

    // ?? Filter chip click (RadioButton tag ? ViewModel.SelectedFilter) ??????
    private void FilterChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Primitives.ToggleButton { Tag: string tag })
            _vm.SelectedFilter = tag;
    }

    // ?? Drag & Drop ?????????????????????????????????????????????????????????

    /// <summary>
    /// Returns true only when the payload contains at least one file/folder path.
    /// Wrapped in try/catch because accessing e.Data from a cross-process drag
    /// (e.g. network shares, locked paths) can throw COMException.
    /// </summary>
    private static bool HasFileDrop(DragEventArgs e)
    {
        try   { return e.Data.GetDataPresent(DataFormats.FileDrop); }
  catch { return false; }
    }

    private void DropZone_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = HasFileDrop(e) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void DropZone_DragEnter(object sender, DragEventArgs e)
    {
        if (!HasFileDrop(e)) { e.Effects = DragDropEffects.None; return; }
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
        // Notify ViewModel — it transitions Idle ? Dragging (or stays put if busy).
        _vm.NotifyDragEnter();
        SetDropVisualState(active: true);
}

    private void DropZone_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement referenceElement) return;

        var pos  = e.GetPosition(referenceElement);
     var size = referenceElement.RenderSize;
        bool stillInside = pos.X >= 0 && pos.Y >= 0
            && pos.X <= size.Width && pos.Y <= size.Height;
        if (stillInside) return;

        // Notify ViewModel — it transitions Dragging ? Idle.
   _vm.NotifyDragLeave();
    SetDropVisualState(active: false);
    }

    private void DropZone_Drop(object sender, DragEventArgs e)
    {
        // Reset visual state immediately — even if validation fails the UI looks idle.
   SetDropVisualState(active: false);
  e.Handled = true;

        if (!HasFileDrop(e))
    {
         _vm.NotifyDragLeave();
            return;
  }

     string[]? paths = null;
     try
        {
paths = e.Data.GetData(DataFormats.FileDrop) as string[];
        }
 catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Drop] GetData failed: {ex.Message}");
       _vm.NotifyDragLeave();
     return;
        }

// Delegate all validation, queuing, and state transitions to the ViewModel.
  _vm.NotifyDrop(paths);
    }

    // ?? Processing-complete handler ??????????????????????????????????????????
    /// <summary>
    /// Invoked by the ViewModel when an unblock run enters the Finalizing state.
    /// Shows the summary dialog modally; the ViewModel always returns to Idle in
    /// its own finally block regardless of what happens here.
    /// </summary>
    private void OnProcessingCompleted(ResultSummaryViewModel summary)
    {
        if (_windowClosing) return;   // window is tearing down — do not show dialog
  try
        {
         var result = ResultSummaryDialog.Show(this, summary);
   if (result == true) _vm.SelectedFilter = "All";
        }
        catch (Exception ex)
        {
            // Dialog failure is non-fatal — the ViewModel's finally already handles Idle.
       System.Diagnostics.Debug.WriteLine($"[ResultSummaryDialog] {ex.Message}");
        }
    }

    // ?? SetDropVisualState ???????????????????????????????????????????????????
    /// <summary>
/// Toggles the drop-zone visual between idle and drag-over states.
    /// Re-entrancy is prevented via <see cref="_dropVisualActive"/>.
    /// Entire method is wrapped in try/catch so no animation failure crashes the app.
    /// </summary>
    private void SetDropVisualState(bool active)
    {
   if (_dropVisualActive == active) return;
        _dropVisualActive = active;

        try
        {
       if (DropIconRing?.Effect is System.Windows.Media.Effects.DropShadowEffect glow)
      {
    glow.BeginAnimation(
        System.Windows.Media.Effects.DropShadowEffect.BlurRadiusProperty,
    new DoubleAnimation(active ? 28 : 0, TimeSpan.FromMilliseconds(active ? 150 : 200)));
           glow.BeginAnimation(
         System.Windows.Media.Effects.DropShadowEffect.OpacityProperty,
      new DoubleAnimation(active ? 0.75 : 0, TimeSpan.FromMilliseconds(active ? 150 : 200)));
  }

        if (DropIconText != null)    DropIconText.Text = active ? "??" : "??";
            if (DropHeadline != null)    DropHeadline.Visibility    = active ? Visibility.Collapsed : Visibility.Visible;
  if (DropHoverHint != null)   DropHoverHint.Visibility   = active ? Visibility.Visible   : Visibility.Collapsed;
     if (DropInstruction != null) DropInstruction.Opacity    = active ? 0.4 : 1.0;

 if (DropZoneBorder != null)
    {
       DropZoneBorder.BorderBrush = active
    ? new LinearGradientBrush(Color.FromRgb(0xA7, 0x8B, 0xFA), Color.FromRgb(0x60, 0xA5, 0xFA), 45)
     : new LinearGradientBrush(Color.FromRgb(0x4A, 0x4A, 0x70), Color.FromRgb(0x2A, 0x2A, 0x45), 45);
 }
        }
        catch (Exception ex)
        {
            _dropVisualActive = !active;   // reset flag so next call can retry
       System.Diagnostics.Debug.WriteLine($"[SetDropVisualState] {ex.Message}");
        }
    }
}