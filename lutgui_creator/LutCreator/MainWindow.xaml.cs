using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace LutCreator
{
    public partial class MainWindow : Window
    {
        private readonly LutParams _params = new LutParams();
        private readonly DispatcherTimer _statusTimer;
        private readonly DispatcherTimer _autoApplyTimer;
        private bool _autoApply;
        private bool _isApplying;
        private string _selectedMonitorPosition = "0,0";

        // Knob-to-param mapping
        private readonly Dictionary<KnobControl, Action<double>> _knobSetters;
        private readonly Dictionary<string, KnobControl> _presetKnobs;

        // Presets
        private static readonly Dictionary<string, Dictionary<string, double>> Presets = new Dictionary<string, Dictionary<string, double>>
        {
            ["warm"] = new Dictionary<string, double> { ["Temperature"] = 40, ["Tint"] = 10, ["Saturation"] = 10, ["Contrast"] = 5 },
            ["cool"] = new Dictionary<string, double> { ["Temperature"] = -35, ["Tint"] = -5, ["Saturation"] = 5, ["Contrast"] = 5 },
            ["vintage"] = new Dictionary<string, double> { ["Saturation"] = -25, ["Contrast"] = 15, ["Temperature"] = 20, ["Gamma"] = 10, ["ShadowsR"] = 10, ["ShadowsG"] = 5, ["ShadowsB"] = -10 },
            ["cinematic"] = new Dictionary<string, double> { ["Contrast"] = 25, ["Saturation"] = -15, ["Temperature"] = -10, ["Tint"] = 5, ["ShadowsB"] = 15, ["HighlightsR"] = 10, ["Gamma"] = 5 },
            ["highContrast"] = new Dictionary<string, double> { ["Contrast"] = 50, ["Saturation"] = 10, ["Vibrance"] = 15 },
            ["desaturated"] = new Dictionary<string, double> { ["Saturation"] = -60, ["Contrast"] = 10, ["Brightness"] = 5 },
            ["nightMode"] = new Dictionary<string, double> { ["Temperature"] = -50, ["Brightness"] = -20, ["Contrast"] = 10, ["Saturation"] = -20, ["RedGamma"] = -30 },
            ["vivid"] = new Dictionary<string, double> { ["Saturation"] = 40, ["Vibrance"] = 30, ["Contrast"] = 15, ["Exposure"] = 5 },
        };

        public MainWindow()
        {
            InitializeComponent();

            // Map knobs to param setters
            _knobSetters = new Dictionary<KnobControl, Action<double>>
            {
                [knobBrightness] = v => _params.Brightness = v,
                [knobContrast] = v => _params.Contrast = v,
                [knobSaturation] = v => _params.Saturation = v,
                [knobVibrance] = v => _params.Vibrance = v,
                [knobExposure] = v => _params.Exposure = v,
                [knobGamma] = v => _params.Gamma = v,
                [knobTemperature] = v => _params.Temperature = v,
                [knobTint] = v => _params.Tint = v,
                [knobHueShift] = v => _params.HueShift = v,
                [knobRedGamma] = v => _params.RedGamma = v,
                [knobGreenGamma] = v => _params.GreenGamma = v,
                [knobBlueGamma] = v => _params.BlueGamma = v,
                [knobShadowsR] = v => _params.ShadowsR = v,
                [knobShadowsG] = v => _params.ShadowsG = v,
                [knobShadowsB] = v => _params.ShadowsB = v,
                [knobHighlightsR] = v => _params.HighlightsR = v,
                [knobHighlightsG] = v => _params.HighlightsG = v,
                [knobHighlightsB] = v => _params.HighlightsB = v,
            };

            // Map param names to knobs (for presets)
            _presetKnobs = new Dictionary<string, KnobControl>
            {
                ["Brightness"] = knobBrightness, ["Contrast"] = knobContrast,
                ["Saturation"] = knobSaturation, ["Vibrance"] = knobVibrance,
                ["Exposure"] = knobExposure, ["Gamma"] = knobGamma,
                ["Temperature"] = knobTemperature, ["Tint"] = knobTint,
                ["HueShift"] = knobHueShift,
                ["RedGamma"] = knobRedGamma, ["GreenGamma"] = knobGreenGamma,
                ["BlueGamma"] = knobBlueGamma,
                ["ShadowsR"] = knobShadowsR, ["ShadowsG"] = knobShadowsG,
                ["ShadowsB"] = knobShadowsB,
                ["HighlightsR"] = knobHighlightsR, ["HighlightsG"] = knobHighlightsG,
                ["HighlightsB"] = knobHighlightsB,
            };

            // Wire up value changed events
            foreach (var kvp in _knobSetters)
            {
                kvp.Key.ValueChanged += Knob_ValueChanged;
                kvp.Key.MouseLeftButtonUp += Knob_MouseUp;
            }

            // Status polling timer
            _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _statusTimer.Tick += (s, e) => UpdateStatus();
            _statusTimer.Start();

            // Auto-apply debounce timer
            _autoApplyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _autoApplyTimer.Tick += AutoApplyTimer_Tick;

            // Enumerate monitors
            EnumerateMonitors();
            UpdateStatus();

            if (Injector.NoDebug)
            {
                SetStatus("Warning: Not running as administrator. Cannot inject into DWM.");
                btnApply.IsEnabled = false;
                btnDisable.IsEnabled = false;
            }
        }

        private void EnumerateMonitors()
        {
            cmbMonitor.Items.Clear();

            var screens = System.Windows.Forms.Screen.AllScreens;
            foreach (var screen in screens.OrderBy(s => s.Bounds.X))
            {
                var pos = screen.Bounds.X + "," + screen.Bounds.Y;
                var name = screen.DeviceName.Replace("\\\\.\\", "");
                var primary = screen.Primary ? " (Primary)" : "";
                var item = new ComboBoxItem
                {
                    Content = name + primary + " - " + screen.Bounds.Width + "x" + screen.Bounds.Height,
                    Tag = pos
                };
                cmbMonitor.Items.Add(item);

                if (screen.Primary)
                    cmbMonitor.SelectedItem = item;
            }

            cmbMonitor.SelectionChanged += CmbMonitor_SelectionChanged;
            UpdateMonitorPosition();
        }

        private void CmbMonitor_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateMonitorPosition();
        }

        private void UpdateMonitorPosition()
        {
            if (cmbMonitor.SelectedItem is ComboBoxItem item)
            {
                _selectedMonitorPosition = (string)item.Tag;
                txtMonitorPos.Text = "Position: " + _selectedMonitorPosition;
            }
        }

        private void UpdateStatus()
        {
            bool active = Injector.IsInjected();
            ActiveStatusText.Text = active ? "Active" : "Inactive";
            ActiveStatusText.Foreground = active
                ? new SolidColorBrush(Color.FromRgb(0x51, 0xCF, 0x66))
                : new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));
            btnDisable.IsEnabled = active;
        }

        private void Knob_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            var knob = (KnobControl)sender;
            if (_knobSetters.TryGetValue(knob, out var setter))
            {
                setter(knob.Value);
            }
        }

        private void Knob_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_autoApply && !_isApplying)
            {
                _autoApplyTimer.Stop();
                _autoApplyTimer.Start();
            }
        }

        private void AutoApplyTimer_Tick(object sender, EventArgs e)
        {
            _autoApplyTimer.Stop();
            ApplyLut();
        }

        private int GetSelectedLutSize()
        {
            if (cmbLutSize.SelectedItem is ComboBoxItem item)
                return int.Parse((string)item.Tag);
            return 33;
        }

        private async void ApplyLut()
        {
            if (_isApplying || Injector.NoDebug) return;

            // Check for DwmLutGUI running - it will conflict
            if (Injector.IsDwmLutGuiRunning())
            {
                var result = MessageBox.Show(
                    "DwmLutGUI.exe is running and will interfere with LUT Creator.\n\n" +
                    "Close DwmLutGUI first for stable operation.\n\n" +
                    "Apply anyway?", "Warning", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes) return;
            }

            _isApplying = true;
            btnApply.IsEnabled = false;

            try
            {
                int size = GetSelectedLutSize();

                if (_params.IsIdentity())
                {
                    SetStatus("Warning: All params are 0 (identity LUT) - adjust knobs first!");
                    return;
                }

                SetStatus($"Generating {size}^3 LUT: Br={_params.Brightness} Co={_params.Contrast} Sa={_params.Saturation} Te={_params.Temperature}...");

                // Generate .cube content on background thread
                string cubeContent = await Task.Run(() => LutEngine.GenerateCubeFile(_params, size));

                SetStatus("Applying to DWM...");

                // Run inject synchronously on UI thread (matching DwmLutGUI)
                // to prevent timing gaps between hook install and screen redraw
                Injector.ReInject(cubeContent, _selectedMonitorPosition);
                RedrawScreens();

                SetStatus($"LUT applied ({size}^3) Br={_params.Brightness} Co={_params.Contrast} Sa={_params.Saturation} Te={_params.Temperature}");
            }
            catch (Exception ex)
            {
                SetStatus("Error: " + ex.Message);
                MessageBox.Show(ex.Message, "Apply Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isApplying = false;
                btnApply.IsEnabled = !Injector.NoDebug;
                UpdateStatus();
            }
        }

        // ==================== Event Handlers ====================

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            ApplyLut();
        }

        private async void BtnDisable_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SetStatus("Disabling...");
                Injector.Uninject();
                RedrawScreens();
                SetStatus("LUT disabled");
                UpdateStatus();
            }
            catch (Exception ex)
            {
                SetStatus("Error: " + ex.Message);
            }
        }

        private void ChkAutoApply_Changed(object sender, RoutedEventArgs e)
        {
            _autoApply = chkAutoApply.IsChecked == true;
        }

        private void BtnExportCube_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Filter = "Cube LUT Files (*.cube)|*.cube|All Files (*.*)|*.*",
                FileName = "lut_" + GetSelectedLutSize() + ".cube",
                DefaultExt = ".cube"
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    int size = GetSelectedLutSize();
                    SetStatus("Exporting " + size + "x" + size + "x" + size + " LUT...");
                    LutEngine.WriteCubeFile(_params, size, dlg.FileName);
                    SetStatus("Exported to " + dlg.FileName);
                }
                catch (Exception ex)
                {
                    SetStatus("Export error: " + ex.Message);
                }
            }
        }

        private void BtnImportCube_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Cube LUT Files (*.cube)|*.cube|All Files (*.*)|*.*"
            };

            if (dlg.ShowDialog() == true)
            {
                SetStatus("Imported: " + System.IO.Path.GetFileName(dlg.FileName) +
                    " - use DwmLutGUI to apply imported files directly");
            }
        }

        private void PresetButton_Click(object sender, RoutedEventArgs e)
        {
            var btn = (Button)sender;
            var presetName = (string)btn.Tag;

            if (!Presets.TryGetValue(presetName, out var preset)) return;

            // Reset all knobs first
            foreach (var knob in _knobSetters.Keys)
                knob.Value = 0;

            // Apply preset values
            foreach (var kvp in preset)
            {
                if (_presetKnobs.TryGetValue(kvp.Key, out var knob))
                    knob.Value = kvp.Value;
            }

            SetStatus("Preset: " + presetName);

            if (_autoApply)
            {
                _autoApplyTimer.Stop();
                _autoApplyTimer.Start();
            }
        }

        private void BtnResetAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var knob in _knobSetters.Keys)
                knob.Value = 0;

            SetStatus("All adjustments reset");
        }

        /// <summary>
        /// Test: Apply a hardcoded red tint LUT to verify the whole pipeline works.
        /// </summary>
        private async void BtnTestRed_Click(object sender, RoutedEventArgs e)
        {
            if (_isApplying || Injector.NoDebug) return;

            if (Injector.IsDwmLutGuiRunning())
            {
                MessageBox.Show("Close DwmLutGUI.exe first! It will override the LUT.",
                    "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _isApplying = true;

            try
            {
                SetStatus("Generating test RED TINT LUT...");
                int size = 17;

                string cubeContent = await Task.Run(() =>
                {
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine("LUT_3D_SIZE " + size);
                    double div = size - 1;
                    for (int b = 0; b < size; b++)
                        for (int g = 0; g < size; g++)
                            for (int r = 0; r < size; r++)
                            {
                                double rv = Math.Min(1.0, r / div + 0.3);
                                double gv = g / div * 0.5;
                                double bv = b / div * 0.5;
                                sb.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                                    "{0:F6} {1:F6} {2:F6}", rv, gv, bv));
                            }
                    return sb.ToString();
                });

                SetStatus("Applying test RED LUT...");
                Injector.ReInject(cubeContent, _selectedMonitorPosition);
                RedrawScreens();
                SetStatus("Test RED LUT applied! Screen should look very red. Click Disable to remove.");
            }
            catch (Exception ex)
            {
                SetStatus("Test error: " + ex.Message);
                MessageBox.Show(ex.Message, "Test Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isApplying = false;
                UpdateStatus();
            }
        }

        /// <summary>
        /// Test: Apply inverted colors LUT.
        /// </summary>
        private async void BtnTestInvert_Click(object sender, RoutedEventArgs e)
        {
            if (_isApplying || Injector.NoDebug) return;

            if (Injector.IsDwmLutGuiRunning())
            {
                MessageBox.Show("Close DwmLutGUI.exe first! It will override the LUT.",
                    "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _isApplying = true;

            try
            {
                SetStatus("Generating test INVERT LUT...");
                int size = 17;

                string cubeContent = await Task.Run(() =>
                {
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine("LUT_3D_SIZE " + size);
                    double div = size - 1;
                    for (int b = 0; b < size; b++)
                        for (int g = 0; g < size; g++)
                            for (int r = 0; r < size; r++)
                            {
                                double rv = 1.0 - r / div;
                                double gv = 1.0 - g / div;
                                double bv = 1.0 - b / div;
                                sb.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                                    "{0:F6} {1:F6} {2:F6}", rv, gv, bv));
                            }
                    return sb.ToString();
                });

                SetStatus("Applying test INVERT LUT...");
                Injector.ReInject(cubeContent, _selectedMonitorPosition);
                RedrawScreens();
                SetStatus("Test INVERT LUT applied! Screen should look inverted. Click Disable to remove.");
            }
            catch (Exception ex)
            {
                SetStatus("Test error: " + ex.Message);
                MessageBox.Show(ex.Message, "Test Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isApplying = false;
                UpdateStatus();
            }
        }

        private void SetStatus(string text)
        {
            StatusText.Text = text;
        }

        /// <summary>
        /// Force DWM to re-render all screens by briefly showing a transparent overlay.
        /// This triggers the LUT hooks on all monitors immediately.
        /// Same technique as DwmLutGUI's RedrawScreens.
        /// </summary>
        private static void RedrawScreens()
        {
            var rect = System.Windows.Forms.Screen.AllScreens
                .Select(x => x.Bounds)
                .Aggregate(System.Drawing.Rectangle.Union);

            var overlay = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Opacity = 0,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = rect.Left,
                Top = rect.Top,
                Width = rect.Width,
                Height = rect.Height,
            };
            overlay.Show();
            System.Threading.Thread.Sleep(50);
            overlay.Close();
        }
    }
}
