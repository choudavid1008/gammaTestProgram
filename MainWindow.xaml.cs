using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace WpfAppGui
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private class ComPortSettings
        {
            public string PortName { get; set; }
            public int BaudRate { get; set; }
            public int DataBits { get; set; }
            public Parity Parity { get; set; }
            public StopBits StopBits { get; set; }
        }

        private readonly object _dutLock = new object();

        private const string ConfigFileName = "Config.txt";
        private const string CsvFileName = "graylevelsrgbw.csv";
        private List<int> _stepGrayValues;
        private CancellationTokenSource _cts;
        private Random _random = new Random();
        private SerialPort _colorimeterPort;
        private SerialPort _dutPort;

        public MainWindow()
        {
            InitializeComponent();
            LoadSerialPorts();
            LoadSettingsFromFile();
            Execute_Click2();
        }

        private async void Execute_Click2()
        {
            // 1. Measure luminance values by interacting with hardware
            double[] measuredLuminances = await MeasureLuminanceAsync();

            // 2. Perform calibration with the measured data
            try
            {
                BacklightCalibrator calibrator = new BacklightCalibrator();
                // Pass the measured data to the calibrator.
                // If measuredLuminances is null (due to an error), the calibrator will use its default values.
                CalibrationResult result = calibrator.PerformCalibration(measuredLuminances);

                StringBuilder sb = new StringBuilder();

                if (result.IsSuccess)
                {
                    sb.AppendLine("--- 數據擬合摘要 ---");
                    sb.AppendLine($"原始數據點數: {result.OriginalDataCount}");
                    sb.AppendLine($"用於擬合的有效數據點數 (測量值 > 0.001): {result.ValidDataCount}");
                    sb.AppendLine();
                    sb.AppendLine("--- 校準成功 ---");
                    sb.AppendLine("背光曲線擬合完成 (y = a * e^(b * x)):");
                    sb.AppendLine($"Fitted Coefficient (a): {result.A_Coeff:F6}");
                    sb.AppendLine($"Fitted Coefficient (b): {result.B_Coeff:F6}");
                    sb.AppendLine($"最終公式: Measured Value ≈ {result.A_Coeff:F6} * e^({result.B_Coeff:F6} * PWM)");
                    sb.AppendLine();
                    sb.AppendLine("--- 預測背光響應曲線 (y = a * e^(b * x)) ---");
                    sb.AppendLine($"{"PWM Input (x)",-15} | {"Predicted Value (y)",-20}");
                    sb.AppendLine($"{new string('-', 15)} | {new string('-', 20)}");

                    foreach (var entry in result.PredictionTable)
                    {
                        if (entry.Item1 == 0)
                        {
                            sb.AppendLine($"{entry.Item1,-15:F0} | {entry.Item2,-20:F3} (物理零點)");
                        }
                        else
                        {
                            sb.AppendLine($"{entry.Item1,-15:F0} | {entry.Item2,-20:F3}");
                        }
                    }

                    sb.AppendLine();
                    sb.AppendLine($"[驗證] 實際 100% 測量值: {result.MeasuredMax:F3}");
                    sb.AppendLine($"[驗證] 擬合公式 100% 預測值: {result.PredictedMax:F3}");
                }
                else
                {
                    sb.AppendLine("--- 校準失敗 ---");
                    sb.AppendLine(result.ErrorMessage);
                }

                TxtTestData.Text = sb.ToString();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"執行背光校準時發生未預期的錯誤: {ex.Message}", "校準失敗", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadSerialPorts()
        {
            string[] portNames = SerialPort.GetPortNames();
            CmbColorimeterPort.ItemsSource = portNames;
            CmbDutPort.ItemsSource = portNames;
        }

        private void LoadSettingsFromFile()
        {
            string configFilePath = FindConfigFilePath();
            if (string.IsNullOrEmpty(configFilePath))
            {
                // 如果找不到設定檔，則載入程式中定義的預設值
                ApplyDefaultSettings();
                return;
            }

            try
            {
                var settings = File.ReadAllLines(configFilePath)
                    .Where(line => !string.IsNullOrWhiteSpace(line) && line.Contains(":"))
                    .Select(line => line.Split(new[] { ':' }, 2))
                    .ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim(), StringComparer.OrdinalIgnoreCase);

                // 更新色度計設定
                if (settings.TryGetValue("ColorimeterPort", out string colorimeterPort))
                {
                    // 驗證 COM port 是否存在於系統列表中
                    if (CmbColorimeterPort.ItemsSource is string[] availablePorts && availablePorts.Contains(colorimeterPort))
                    {
                        CmbColorimeterPort.SelectedItem = colorimeterPort;
                    }
                }
                if (settings.TryGetValue("ColorimeterBaudRate", out string colorimeterBaudRate))
                {
                    SetComboBoxValue(CmbColorimeterBaudRate, colorimeterBaudRate);
                }
                if (settings.TryGetValue("ColorimeterDataBits", out string colorimeterDataBits))
                {
                    SetComboBoxValue(CmbColorimeterDataBits, colorimeterDataBits);
                }
                if (settings.TryGetValue("ColorimeterParity", out string colorimeterParity))
                {
                    SetComboBoxValue(CmbColorimeterParity, colorimeterParity);
                }
                if (settings.TryGetValue("ColorimeterStopBits", out string colorimeterStopBits))
                {
                    SetComboBoxValue(CmbColorimeterStopBits, colorimeterStopBits);
                }

                // 更新 DUT 設定
                if (settings.TryGetValue("DutPort", out string dutPort))
                {
                    // 驗證 COM port 是否存在於系統列表中
                    if (CmbDutPort.ItemsSource is string[] availablePorts && availablePorts.Contains(dutPort))
                    {
                        CmbDutPort.SelectedItem = dutPort;
                    }
                }
                if (settings.TryGetValue("DutBaudRate", out string dutBaudRate))
                {
                    SetComboBoxValue(CmbDutBaudRate, dutBaudRate);
                }
                if (settings.TryGetValue("DutDataBits", out string dutDataBits))
                {
                    SetComboBoxValue(CmbDutDataBits, dutDataBits);
                }
                if (settings.TryGetValue("DutParity", out string dutParity))
                {
                    SetComboBoxValue(CmbDutParity, dutParity);
                }
                if (settings.TryGetValue("DutStopBits", out string dutStopBits))
                {
                    SetComboBoxValue(CmbDutStopBits, dutStopBits);
                }

                // 更新基本功能設定
                if (settings.TryGetValue("Steps", out string steps))
                {
                    SetComboBoxValue(CmbSteps, steps);
                }
                if (settings.TryGetValue("IntervalTime", out string intervalTime))
                {
                    TxtIntervalTime.Text = intervalTime;
                }
            }
            catch (Exception ex)
            {
                // 如果讀取或解析檔案時發生錯誤，顯示錯誤訊息但繼續執行
                MessageBox.Show($"讀取設定檔時發生錯誤: {ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 從執行目錄開始向上層尋找設定檔。
        /// </summary>
        /// <returns>如果找到檔案，則傳回完整路徑；否則傳回 null。</returns>
        private string FindConfigFilePath()
        {
            string currentDir = AppDomain.CurrentDomain.BaseDirectory;
            int maxLevels = 5; // 設定向上搜尋的最大層數，以避免無限循環

            for (int i = 0; i < maxLevels; i++)
            {
                string filePath = System.IO.Path.Combine(currentDir, ConfigFileName);
                if (File.Exists(filePath))
                {
                    return filePath;
                }

                DirectoryInfo parentDir = Directory.GetParent(currentDir);
                if (parentDir == null)
                {
                    break;
                }
                currentDir = parentDir.FullName;
            }

            return null;
        }

        /// <summary>
        /// 安全地設定 ComboBox 的選定值。
        /// </summary>
        /// <param name="comboBox">要設定的 ComboBox。</param>
        /// <param name="value">要選定的值。</param>
        private void SetComboBoxValue(ComboBox comboBox, string value)
        {
            foreach (var item in comboBox.Items)
            {
                if (item is ComboBoxItem comboBoxItem && comboBoxItem.Content.ToString().Equals(value, StringComparison.OrdinalIgnoreCase))
                {
                    comboBox.SelectedItem = item;
                    return;
                }
            }
        }

        /// <summary>
        /// 將 UI 控制項設定為程式中定義的預設值。
        /// </summary>
        private void ApplyDefaultSettings()
        {
            // 色度計預設值
            SetComboBoxValue(CmbColorimeterBaudRate, "115200");
            SetComboBoxValue(CmbColorimeterDataBits, "8");
            SetComboBoxValue(CmbColorimeterParity, "None");
            SetComboBoxValue(CmbColorimeterStopBits, "One");

            // DUT 預設值
            SetComboBoxValue(CmbDutBaudRate, "115200");
            SetComboBoxValue(CmbDutDataBits, "8");
            SetComboBoxValue(CmbDutParity, "None");
            SetComboBoxValue(CmbDutStopBits, "One");

            // 基本功能預設值
            SetComboBoxValue(CmbSteps, "64");
            TxtIntervalTime.Text = "200";
        }

        /// <summary>
        /// 根據選擇的階數產生對應的數值陣列。
        /// </summary>
        private void GenerateStepValues()
        {
            if (CmbSteps.SelectedItem == null || !(CmbSteps.SelectedItem is ComboBoxItem selectedItem))
            {
                _stepGrayValues = new List<int>();
                return;
            }

            if (int.TryParse(selectedItem.Content.ToString(), out int steps) && steps > 0)
            {
                _stepGrayValues = new List<int>(steps + 1);
                int increment = 256 / steps;
                int currentValue = 0;

                for (int i = 0; i < steps; i++)
                {
                    _stepGrayValues.Add(currentValue);
                    currentValue += increment;
                }

                // 移除任何可能超過255的值
                _stepGrayValues.RemoveAll(val => val > 255);

                // 確保最後一個值是 255
                if (!_stepGrayValues.Contains(255))
                {
                    _stepGrayValues.Add(255);
                }
            }
        }

        private void CmbSteps_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // The logic has been moved to the Execute_Click event
        }

        private ComPortSettings ReadComPortSettingsFromUI(ComboBox cmbPort, ComboBox cmbBaudRate, ComboBox cmbDataBits, ComboBox cmbParity, ComboBox cmbStopBits)
        {
            if (cmbPort.SelectedItem == null)
            {
                return null;
            }

            return new ComPortSettings
            {
                PortName = cmbPort.SelectedItem as string,
                BaudRate = int.Parse((cmbBaudRate.SelectedItem as ComboBoxItem).Content as string),
                DataBits = int.Parse((cmbDataBits.SelectedItem as ComboBoxItem).Content as string),
                Parity = (Parity)Enum.Parse(typeof(Parity), (cmbParity.SelectedItem as ComboBoxItem).Content as string, true),
                StopBits = (StopBits)Enum.Parse(typeof(StopBits), (cmbStopBits.SelectedItem as ComboBoxItem).Content as string, true)
            };
        }

        private async void Execute_Click(object sender, RoutedEventArgs e)
        {
            TxtTestData.Clear(); // 清除先前的測試數據
            GenerateStepValues(); // 在執行前，根據當前UI設定產生數值

            var colorimeterSettings = ReadComPortSettingsFromUI(CmbColorimeterPort, CmbColorimeterBaudRate, CmbColorimeterDataBits, CmbColorimeterParity, CmbColorimeterStopBits);
            var dutSettings = ReadComPortSettingsFromUI(CmbDutPort, CmbDutBaudRate, CmbDutDataBits, CmbDutParity, CmbDutStopBits);

            if (colorimeterSettings == null || dutSettings == null)
            {
                ShowMessageBoxOnUi("請選擇有效的 COM Port。", "設定錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _cts = new CancellationTokenSource();
            BtnExecute.IsEnabled = false;
            BtnStop.IsEnabled = true;

            try
            {
                await MeasurementLoopAsync(_cts.Token, colorimeterSettings, dutSettings);
                // 只有在沒有被取消的情況下才顯示完成訊息
                ShowMessageBoxOnUi("測量完成並已儲存結果。", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (OperationCanceledException)
            {
                ShowMessageBoxOnUi("操作已被使用者停止。", "已停止", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                ShowMessageBoxOnUi($"執行時發生未預期的錯誤: {ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnExecute.IsEnabled = true;
                BtnStop.IsEnabled = false;
                _cts.Dispose();
                _cts = null;
            }
        }

        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            BtnStop.IsEnabled = false; // 防止重複點擊
        }

        private async Task MeasurementLoopAsync(CancellationToken token, ComPortSettings colorimeterSettings, ComPortSettings dutSettings)
        {
            try
            {
                // 1. 確認連線 (在 UI 執行緒外執行，避免阻塞)
                bool connected = await Task.Run(() => ConnectToColorimeter(colorimeterSettings) && ConnectToDut(dutSettings), token);
                if (!connected)
                {
                    // ShowMessageBoxOnUi 已處理執行緒切換，可以直接呼叫
                    ShowMessageBoxOnUi("無法連線到 COM Port，請檢查設定。", "連線失敗", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 2. 準備資料陣列
                double[] getBrightness = new double[_stepGrayValues.Count];

                // 在迴圈外解析延遲時間 (現在可以在 UI 執行緒直接讀取)
                int interval = 200; // 預設值
                if (int.TryParse(TxtIntervalTime.Text, out int parsedInterval))
                {
                    interval = parsedInterval;
                }

                // 3. 執行主迴圈
                for (int i = 0; i < _stepGrayValues.Count; i++)
                {
                    token.ThrowIfCancellationRequested();

                    // a. 跟DUT 發送 stepGrayValues[i]
                    SendBacklightBrightnessCommand(_stepGrayValues[i]);

                    // b. 從色度計取得 Brightness
                    // 實際應用中，您可以在此處發送讀取指令並解析回傳值
                    // 例如: _colorimeterPort.WriteLine("READ_BRIGHTNESS");
                    //       string response = _colorimeterPort.ReadLine();
                    //       getBrightness[i] = ParseBrightness(response);

                    // 以下為模擬數據
                    getBrightness[i] = _random.NextDouble() * 200;

                    // 將目前結果顯示在 UI 上
                    TxtTestData.AppendText($"Gray: {_stepGrayValues[i]}, Brightness: {getBrightness[i]:F3}\n");
                    TxtTestData.ScrollToEnd();

                    // c. delay IntervalTime
                    await Task.Delay(interval, token);
                }

                // 4. 儲存結果
                SaveResultsToCsv(getBrightness);
            }
            finally
            {
                // 斷開連線
                DisconnectFromColorimeter();
                DisconnectFromDut();
            }
        }

        private bool ConnectToColorimeter(ComPortSettings settings)
        {
            try
            {
                if (string.IsNullOrEmpty(settings.PortName))
                {
                    ShowMessageBoxOnUi("色度計通訊埠未選擇。", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }

                _colorimeterPort = new SerialPort
                {
                    PortName = settings.PortName,
                    BaudRate = settings.BaudRate,
                    DataBits = settings.DataBits,
                    Parity = settings.Parity,
                    StopBits = settings.StopBits,
                    ReadTimeout = 500,
                    WriteTimeout = 500
                };

                _colorimeterPort.Open();
                // (可以加入一個握手指令來驗證連線)
            }
            catch (Exception ex)
            {
                ShowMessageBoxOnUi($"連接色度計時發生錯誤: {ex.Message}", "連線錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            return true;
        }

        private bool ConnectToDut(ComPortSettings settings)
        {
            try
            {
                if (string.IsNullOrEmpty(settings.PortName))
                {
                    ShowMessageBoxOnUi("DUT 通訊埠未選擇。", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }

                _dutPort = new SerialPort
                {
                    PortName = settings.PortName,
                    BaudRate = settings.BaudRate,
                    DataBits = settings.DataBits,
                    Parity = settings.Parity,
                    StopBits = settings.StopBits,
                    ReadTimeout = 500,
                    WriteTimeout = 500
                };

                _dutPort.NewLine = "\r\n";
                _dutPort.Open();
                 // (可以加入一個握手指令來驗證連線)
            }
            catch (Exception ex)
            {
                ShowMessageBoxOnUi($"連接 DUT 時發生錯誤: {ex.Message}", "連線錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            return true;
        }

        private void SaveResultsToCsv(double[] brightnessValues)
        {
            try
            {
                StringBuilder csvContent = new StringBuilder();
                csvContent.AppendLine("Gray,Brightness");

                for (int i = 0; i < _stepGrayValues.Count; i++)
                {
                    // 確保 brightnessValues 的索引不會超出範圍
                    if (i < brightnessValues.Length)
                    {
                        csvContent.AppendLine($"{_stepGrayValues[i]},{brightnessValues[i]:F3}");
                    }
                }

                File.WriteAllText(CsvFileName, csvContent.ToString());
            }
            catch (Exception ex)
            {
                ShowMessageBoxOnUi($"儲存 CSV 檔案時發生錯誤: {ex.Message}", "存檔失敗", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ShowMessageBoxOnUi(string message, string caption, MessageBoxButton button, MessageBoxImage icon)
        {
            Dispatcher.Invoke(() =>
            {
                MessageBox.Show(this, message, caption, button, icon);
            });
        }

        public void DisconnectFromColorimeter()
        {
            try
            {
                if (_colorimeterPort != null && _colorimeterPort.IsOpen)
                {
                    _colorimeterPort.Close();
                }
                _colorimeterPort?.Dispose();
            }
            catch (Exception ex)
            {
                ShowMessageBoxOnUi($"關閉色度計連接時發生錯誤: {ex.Message}", "關閉失敗", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void DisconnectFromDut()
        {
            try
            {
                if (_dutPort != null && _dutPort.IsOpen)
                {
                    _dutPort.Close();
                }
                _dutPort?.Dispose();
            }
            catch (Exception ex)
            {
                ShowMessageBoxOnUi($"關閉 DUT 連接時發生錯誤: {ex.Message}", "關閉失敗", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void SendLcdFillCommand(string hexColor)
        {
            SendDutCommand($"lcd fill {hexColor}");
        }

        public void SendBacklightBrightnessCommand(int brightness)
        {
            SendDutCommand($"fct-bl set_brightness {brightness}");
        }

        /// <summary>
        /// Reads luminance from the colorimeter.
        /// NOTE: This is a placeholder and needs to be replaced with actual hardware communication.
        /// </summary>
        /// <returns>A simulated luminance value.</returns>
        public double ReadLuminanceFromColorimeter()
        {
            // TODO: Implement actual communication with the colorimeter.
            // e.g.,
            // lock(_colorimeterLock) // if you add a lock for the colorimeter
            // {
            //     _colorimeterPort.WriteLine("READ_LUMINANCE");
            //     string response = _colorimeterPort.ReadLine();
            //     return double.Parse(response);
            // }

            // For now, return a random value for simulation
            return _random.NextDouble() * 100.0;
        }

        private async Task<double[]> MeasureLuminanceAsync()
        {
            var colorimeterSettings = ReadComPortSettingsFromUI(CmbColorimeterPort, CmbColorimeterBaudRate, CmbColorimeterDataBits, CmbColorimeterParity, CmbColorimeterStopBits);
            var dutSettings = ReadComPortSettingsFromUI(CmbDutPort, CmbDutBaudRate, CmbDutDataBits, CmbDutParity, CmbDutStopBits);

            if (colorimeterSettings == null || dutSettings == null)
            {
                ShowMessageBoxOnUi("請選擇有效的 COM Port。", "設定錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }

            try
            {
                bool connected = await Task.Run(() => ConnectToColorimeter(colorimeterSettings) && ConnectToDut(dutSettings));
                if (!connected)
                {
                    ShowMessageBoxOnUi("無法連線到 COM Port，請檢查設定。", "連線失敗", MessageBoxButton.OK, MessageBoxImage.Error);
                    return null;
                }

                double[] pwm_inputs = { 0, 10, 25, 50, 75, 85, 95, 97, 98, 100 };
                var measuredLuminances = new List<double>();

                foreach (var pwm in pwm_inputs)
                {
                    SendDutCommand($"fct-bl set_current_percent {pwm}");
                    // Add a small delay for the hardware to respond
                    await Task.Delay(100);
                    double luminance = ReadLuminanceFromColorimeter();
                    measuredLuminances.Add(luminance);
                }

                return measuredLuminances.ToArray();
            }
            catch (Exception ex)
            {
                ShowMessageBoxOnUi($"執行亮度測量時發生錯誤: {ex.Message}", "測量失敗", MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }
            finally
            {
                DisconnectFromColorimeter();
                DisconnectFromDut();
            }
        }

        public void SendDutCommand(string command)
        {
            try
            {
                lock (_dutLock)
                {
                    if (_dutPort != null && _dutPort.IsOpen)
                    {
                        _dutPort.WriteLine(command);
                    }
                    else
                    {
                        ShowMessageBoxOnUi("DUT 未連接，無法發送指令。", "指令失敗", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                ShowMessageBoxOnUi($"發送 DUT 指令 '{command}' 時發生錯誤: {ex.Message}", "指令失敗", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
