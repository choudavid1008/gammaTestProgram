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
        private const string ConfigFileName = "Config.txt";
        private const string CsvFileName = "graylevelsrgbw.csv";
        private List<int> _stepGrayValues;
        private Thread _workerThread;
        private volatile bool _isStopRequested;

        public MainWindow()
        {
            InitializeComponent();
            LoadSerialPorts();
            LoadSettingsFromFile();
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
            SetComboBoxValue(CmbColorimeterStopBits, "1");

            // DUT 預設值
            SetComboBoxValue(CmbDutBaudRate, "115200");
            SetComboBoxValue(CmbDutDataBits, "8");
            SetComboBoxValue(CmbDutParity, "None");
            SetComboBoxValue(CmbDutStopBits, "1");

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

        private void Execute_Click(object sender, RoutedEventArgs e)
        {
            TxtTestData.Clear(); // 清除先前的測試數據
            GenerateStepValues(); // 在執行前，根據當前UI設定產生數值

            BtnExecute.IsEnabled = false;
            BtnStop.IsEnabled = true;
            _isStopRequested = false;

            _workerThread = new Thread(MeasurementLoop);
            _workerThread.Start();
        }

        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            if (_workerThread != null && _workerThread.IsAlive)
            {
                _isStopRequested = true;
                BtnStop.IsEnabled = false; // 防止重複點擊
            }
        }

        private void MeasurementLoop()
        {
            try
            {
                // 1. 確認連線
                if (!ConnectToColorimeter() || !ConnectToDut())
                {
                    ShowMessageBoxOnUi("無法連線到 COM Port，請檢查設定。", "連線失敗", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 2. 準備資料陣列
                double[] getBrightness = new double[_stepGrayValues.Count];

                // 在迴圈外解析延遲時間
                int interval = 200; // 預設值
                Dispatcher.Invoke(() =>
                {
                    if (int.TryParse(TxtIntervalTime.Text, out int parsedInterval))
                    {
                        interval = parsedInterval;
                    }
                });

                // 3. 執行主迴圈
                for (int i = 0; i < _stepGrayValues.Count; i++)
                {
                    // 檢查是否被要求停止
                    if (_isStopRequested)
                    {
                        ShowMessageBoxOnUi("操作已被使用者停止。", "已停止", MessageBoxButton.OK, MessageBoxImage.Information);
                        return; // 提前退出
                    }

                    // a. 跟DUT 發送 stepGrayValues[i]
                    // (此處應加入實際的 DUT 通訊程式碼)

                    // b. 從色度計取得 Brightness
                    // (此處應加入實際的色度計通訊程式碼)
                    // 以下為模擬數據
                    getBrightness[i] = new Random().NextDouble() * 200;

                    // 將目前結果顯示在 UI 上
                    Dispatcher.Invoke(() =>
                    {
                        TxtTestData.AppendText($"Gray: {_stepGrayValues[i]}, Brightness: {getBrightness[i]:F3}\n");
                        TxtTestData.ScrollToEnd();
                    });

                    // c. delay IntervalTime
                    Thread.Sleep(interval);
                }

                // 4. 儲存結果 (如果沒有被停止)
                SaveResultsToCsv(getBrightness);
                ShowMessageBoxOnUi("測量完成並已儲存結果。", "完成", MessageBoxButton.OK, MessageBoxImage.Information);

            }
            catch (Exception ex)
            {
                ShowMessageBoxOnUi($"執行時發生未預期的錯誤: {ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // 無論如何，都要在 UI 執行緒上還原按鈕狀態
                Dispatcher.Invoke(() =>
                {
                    BtnExecute.IsEnabled = true;
                    BtnStop.IsEnabled = false;
                });
            }
        }

        private bool ConnectToColorimeter()
        {
            /*
            try
            {
                string portName = "";
                int baudRate = 115200;
                int dataBits = 8;
                Parity parity = Parity.None;
                StopBits stopBits = StopBits.One;

                // 從 UI 執行緒安全地讀取設定
                Dispatcher.Invoke(() =>
                {
                    portName = CmbColorimeterPort.SelectedItem as string;
                    baudRate = int.Parse((CmbColorimeterBaudRate.SelectedItem as ComboBoxItem).Content as string);
                    dataBits = int.Parse((CmbColorimeterDataBits.SelectedItem as ComboBoxItem).Content as string);
                    parity = (Parity)Enum.Parse(typeof(Parity), (CmbColorimeterParity.SelectedItem as ComboBoxItem).Content as string, true);
                    stopBits = (StopBits)Enum.Parse(typeof(StopBits), (CmbColorimeterStopBits.SelectedItem as ComboBoxItem).Content as string, true);
                });

                if (string.IsNullOrEmpty(portName))
                {
                    ShowMessageBoxOnUi("色度計通訊埠未選擇。", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }

                SerialPort colorimeterPort = new SerialPort
                {
                    PortName = portName,
                    BaudRate = baudRate,
                    DataBits = dataBits,
                    Parity = parity,
                    StopBits = stopBits,
                    ReadTimeout = 500,
                    WriteTimeout = 500
                };

                // colorimeterPort.Open();
                // (此處應加入驗證連線是否成功的程式碼)

                // colorimeterPort.Close(); // 如果只是為了測試連線，可以立刻關閉
            }
            catch (Exception ex)
            {
                ShowMessageBoxOnUi($"連接色度計時發生錯誤: {ex.Message}", "連線錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            */
            return true; // 佔位符
        }

        private bool ConnectToDut()
        {
            /*
            try
            {
                string portName = "";
                int baudRate = 115200;
                int dataBits = 8;
                Parity parity = Parity.None;
                StopBits stopBits = StopBits.One;

                // 從 UI 執行緒安全地讀取設定
                Dispatcher.Invoke(() =>
                {
                    portName = CmbDutPort.SelectedItem as string;
                    baudRate = int.Parse((CmbDutBaudRate.SelectedItem as ComboBoxItem).Content as string);
                    dataBits = int.Parse((CmbDutDataBits.SelectedItem as ComboBoxItem).Content as string);
                    parity = (Parity)Enum.Parse(typeof(Parity), (CmbDutParity.SelectedItem as ComboBoxItem).Content as string, true);
                    stopBits = (StopBits)Enum.Parse(typeof(StopBits), (CmbDutStopBits.SelectedItem as ComboBoxItem).Content as string, true);
                });

                if (string.IsNullOrEmpty(portName))
                {
                    ShowMessageBoxOnUi("DUT 通訊埠未選擇。", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }

                SerialPort dutPort = new SerialPort
                {
                    PortName = portName,
                    BaudRate = baudRate,
                    DataBits = dataBits,
                    Parity = parity,
                    StopBits = stopBits,
                    ReadTimeout = 500,
                    WriteTimeout = 500
                };

                // dutPort.Open();
                // (此處應加入驗證連線是否成功的程式碼)

                // dutPort.Close(); // 如果只是為了測試連線，可以立刻關閉
            }
            catch (Exception ex)
            {
                ShowMessageBoxOnUi($"連接 DUT 時發生錯誤: {ex.Message}", "連線錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            */
            return true; // 佔位符
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
    }
}
