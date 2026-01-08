using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
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
        private List<int> _stepGrayValues;

        public MainWindow()
        {
            InitializeComponent();
            LoadSerialPorts();
            LoadSettingsFromFile();
            //GenerateStepValues();
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
            GenerateStepValues();
        }
    }
}
