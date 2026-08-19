using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ThunderSE.Ui.MainWindow
{
    public partial class IICConfigWindow : Window
    {
        private DeviceConfigPageViewModel _viewModel;
        private const string IICConfigSaveDir = "IICConfigs";
        private const string IICConfigSaveFile = "IIC_Register_List.txt";

        public IICConfigWindow(DeviceConfigPageViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            this.DataContext = _viewModel;
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            LoadIICRegisterList();
        }

        private void OnWindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            SaveIICRegisterList();
        }

        /// <summary>
        /// 获取保存路径
        /// </summary>
        private string GetSaveFilePath()
        {
            string saveDir = Path.Combine(Directory.GetCurrentDirectory(), IICConfigSaveDir);
            Directory.CreateDirectory(saveDir);
            return Path.Combine(saveDir, IICConfigSaveFile);
        }

        /// <summary>
        /// 保存IIC寄存器列表到文件
        /// </summary>
        private void SaveIICRegisterList()
        {
            try
            {
                if (_viewModel.IICRegisterList.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("[IICConfig] 列表为空，跳过保存");
                    return;
                }

                string filePath = GetSaveFilePath();
                var lines = new List<string>();
                lines.Add("# IIC 寄存器配置列表 - 自动保存");
                lines.Add($"# 保存时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                lines.Add($"# 共 {_viewModel.TotalRegisterCount} 个寄存器");
                lines.Add("# 格式: 地址, 数据");
                lines.Add("# ---------------------------");

                foreach (var item in _viewModel.IICRegisterList)
                {
                    lines.Add($"{item.AddressHex}, {item.DataHex}");
                }

                File.WriteAllLines(filePath, lines, System.Text.Encoding.UTF8);
                System.Diagnostics.Debug.WriteLine($"[IICConfig] 已保存 {lines.Count - 5} 个寄存器配置到: {filePath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[IICConfig] 保存失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 从文件加载IIC寄存器列表
        /// </summary>
        private void LoadIICRegisterList()
        {
            try
            {
                string filePath = GetSaveFilePath();
                if (!File.Exists(filePath))
                {
                    System.Diagnostics.Debug.WriteLine("[IICConfig] 未找到保存文件，跳过加载");
                    return;
                }

                string[] lines = File.ReadAllLines(filePath, System.Text.Encoding.UTF8);
                var loadedData = new List<Tuple<ushort, ushort>>();

                foreach (string line in lines)
                {
                    string trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
                        continue;

                    var parsed = ParseConfigLine(trimmed, 0);
                    if (parsed != null)
                    {
                        loadedData.Add(parsed);
                    }
                }

                if (loadedData.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("[IICConfig] 文件中没有有效数据");
                    return;
                }

                _viewModel.ClearIICRegisterList();
                foreach (var data in loadedData)
                {
                    var item = new IICRegisterItem
                    {
                        Index = _viewModel.IICRegisterList.Count + 1,
                        Address = data.Item1,
                        Data = data.Item2
                    };
                    _viewModel.IICRegisterList.Add(item);
                }

                RaisePropertyChangedEvents();
                System.Diagnostics.Debug.WriteLine($"[IICConfig] 已加载 {loadedData.Count} 个寄存器配置");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[IICConfig] 加载失败: {ex.Message}");
            }
        }

        #region 配置选项事件


        #endregion

        #region 列表操作事件

        private void OnAddRowClick(object sender, RoutedEventArgs e)
        {
            _viewModel.AddIICRegisterItem();
        }

        private void OnDeleteRowClick(object sender, RoutedEventArgs e)
        {
            var selectedItems = DgRegisterList.SelectedItems;
            if (selectedItems.Count == 0)
            {
                MessageBox.Show("请先选择要删除的行！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show($"确定要删除选中的 {selectedItems.Count} 行吗？", "确认删除",
                                        MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                var itemsToDelete = new List<IICRegisterItem>();
                foreach (var item in selectedItems)
                {
                    if (item is IICRegisterItem regItem)
                    {
                        itemsToDelete.Add(regItem);
                    }
                }

                foreach (var item in itemsToDelete)
                {
                    _viewModel.RemoveIICRegisterItem(item);
                }
            }
        }

        private void OnDeleteThisRowClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is IICRegisterItem item)
            {
                var result = MessageBox.Show("确定要删除这一行吗？", "确认删除",
                                            MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    _viewModel.RemoveIICRegisterItem(item);
                }
            }
        }

        private void OnClearAllClick(object sender, RoutedEventArgs e)
        {
            if (_viewModel.IICRegisterList.Count == 0)
            {
                MessageBox.Show("列表已经是空的！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show("确定要清空所有寄存器配置吗？此操作不可撤销！", "确认清空",
                                        MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                _viewModel.ClearIICRegisterList();
            }
        }

        private void OnApplyThisItemClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is IICRegisterItem item)
            {
                button.IsEnabled = false;
                string message = $"✓ 应用寄存器配置\n\n" +
                               $"序号: #{item.Index}\n" +
                               $"地址: {item.AddressHex}\n" +
                               $"数据: {item.DataHex}\n" +
                               $"打包结果: {item.PackedHex}\n" +
                               $"字节长度: {item.PackedBytesCount} 字节\n\n" +
                               $"字节流详情:\n";
                try
                {
                    var bytes = item.PackedBytes;
                    for (int i = 0; i < bytes.Length; i++)
                    {
                        message += $"  [{i}] = 0x{bytes[i]:X2}\n";
                    }

                    message += "\n确定要应用此配置吗？";

                    var result = MessageBox.Show(message, "应用单个寄存器配置",
                                                MessageBoxButton.YesNo,
                                                MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        bool writeOk = _viewModel.WriteIICConfigsToDevice(bytes);
                        RaisePropertyChangedEvents();

                        if (writeOk)
                        {
                            MessageBox.Show($"已成功写入第 {item.Index} 行配置！\n\n" +
                                           $"寄存器地址: {item.AddressHex}\n" +
                                           $"写入数据: {item.DataHex}\n" +
                                           $"发送字节: {item.PackedHex}",
                                           "写入成功",
                                           MessageBoxButton.OK,
                                           MessageBoxImage.Information);
                        }
                        else
                        {
                            MessageBox.Show($"写入第 {item.Index} 行配置失败！\n\n" +
                                           $"寄存器地址: {item.AddressHex}\n" +
                                           $"请检查设备连接是否正常。",
                                           "写入失败",
                                           MessageBoxButton.OK,
                                           MessageBoxImage.Error);
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"写入第 {item.Index} 行配置时发生错误！\n\n" +
                                   $"错误信息: {ex.Message}",
                                   "写入错误",
                                   MessageBoxButton.OK,
                                   MessageBoxImage.Error);
                }
                finally
                {
                    // 恢复按钮状态
                    button.IsEnabled = true;
                }
            }
        }

        private async void OnReadThisItemClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is IICRegisterItem item)
            {
                // 禁用按钮防止重复点击
                button.IsEnabled = false;
                var originalContent = button.Content;
                button.Content = "读取中...";

                try
                {
                    // 参考SensorAdjust: 根据地址/数据位宽构建读取缓冲区
                    // SensorAdjust通过CDB发送地址(4字节大端)，这里根据位宽模式构建
                    bool isTwoByteAddr = _viewModel.AddrWidthMode == "TwoByte" || item.Address > 0xFF;
                    bool isTwoByteData = _viewModel.DataWidthMode == "TwoByte";
                    int addrByteCount = isTwoByteAddr ? 2 : 1;
                    int dataByteCount = isTwoByteData ? 2 : 1;
                    int totalBytes = addrByteCount + dataByteCount;

                    // 构建读取请求缓冲区: [地址字节(s), 数据字节(s)占位]
                    // 数据字节初始为0(占位)，设备读取后会填充实际值
                    byte[] readBuffer = new byte[totalBytes];
                    if (isTwoByteAddr)
                    {
                        readBuffer[0] = (byte)((item.Address >> 8) & 0xFF);
                        readBuffer[1] = (byte)(item.Address & 0xFF);
                    }
                    else
                    {
                        readBuffer[0] = (byte)(item.Address & 0xFF);
                    }

                    // 保存发送的地址字节用于响应验证(类似SensorAdjust的0xcb/0xf2头部校验)
                    byte[] sentBytes = (byte[])readBuffer.Clone();

                    // 异步执行读取操作，避免阻塞UI线程
                    var readResult = await Task.Run(() => _viewModel.ReadIICConfigFromDevice(readBuffer));

                    // 参考SensorAdjust ReadReg: 区分设备API失败(null)和空数据
                    if (readResult == null)
                    {
                        MessageBox.Show($"读取第 {item.Index} 行配置失败！\n\n" +
                                       $"寄存器地址: {item.AddressHex}\n" +
                                       $"地址位宽: {(isTwoByteAddr ? "16位" : "8位")}\n" +
                                       $"数据位宽: {(isTwoByteData ? "16位" : "8位")}\n\n" +
                                       "设备API返回失败，请检查设备连接是否正常。",
                                       "读取失败",
                                       MessageBoxButton.OK,
                                       MessageBoxImage.Error);
                        return;
                    }

                    if (readResult.Length == 0)
                    {
                        MessageBox.Show($"读取第 {item.Index} 行配置失败！\n\n" +
                                       $"寄存器地址: {item.AddressHex}\n" +
                                       "读取返回空数据。",
                                       "读取失败",
                                       MessageBoxButton.OK,
                                       MessageBoxImage.Error);
                        return;
                    }

                    // 参考SensorAdjust ReadReg: 验证响应中的地址字节是否匹配
                    // SensorAdjust校验 data[0]==0xcb && data[1]==0xf2
                    // 这里校验响应中的地址字节与发送的地址字节一致
                    bool addrValidated = true;
                    int validateLen = Math.Min(addrByteCount, Math.Min(sentBytes.Length, readResult.Length));
                    for (int i = 0; i < validateLen; i++)
                    {
                        if (sentBytes[i] != readResult[i])
                        {
                            addrValidated = false;
                            break;
                        }
                    }

                    // 提取数据值: 数据紧跟在地址字节之后，大端序
                    // 参考SensorAdjust: data[2]<<24 | data[3]<<16 | data[4]<<8 | data[5]
                    ushort readValue = 0;
                    bool dataExtracted = false;
                    int dataOffset = addrByteCount;

                    if (readResult.Length >= dataOffset + dataByteCount)
                    {
                        // 完整提取: 按数据位宽读取
                        if (dataByteCount == 2)
                        {
                            readValue = (ushort)((readResult[dataOffset] << 8) | readResult[dataOffset + 1]);
                        }
                        else
                        {
                            readValue = readResult[dataOffset];
                        }
                        dataExtracted = true;
                    }
                    else if (readResult.Length > dataOffset)
                    {
                        // 部分提取(数据不完整，仅取可用字节)
                        readValue = readResult[dataOffset];
                        dataExtracted = true;
                    }
                    else if (readResult.Length > 0)
                    {
                        // 兜底: 取最后一个字节(兼容设备不回显地址的情况)
                        readValue = readResult[readResult.Length - 1];
                        dataExtracted = true;
                    }

                    if (dataExtracted)
                    {
                        item.Data = readValue;
                    }

                    // 构建诊断信息(参考SensorAdjust的调试风格: 显示发送与接收对比)
                    string sentHex = BitConverter.ToString(sentBytes).Replace("-", " ");
                    string resultHex = BitConverter.ToString(readResult).Replace("-", " ");

                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine($"已读取第 {item.Index} 行配置！");
                    sb.AppendLine();
                    sb.AppendLine($"寄存器地址: {item.AddressHex}");
                    sb.AppendLine($"地址位宽: {(isTwoByteAddr ? "16位(双字节)" : "8位(单字节)")}");
                    sb.AppendLine($"数据位宽: {(isTwoByteData ? "16位(双字节)" : "8位(单字节)")}");
                    sb.AppendLine();
                    sb.AppendLine($"发送字节: {sentHex}");
                    sb.AppendLine($"接收字节: {resultHex}");
                    sb.AppendLine($"地址校验: {(addrValidated ? "通过" : "不匹配")}");
                    sb.AppendLine($"数据值: 0x{readValue:X2}");

                    if (!addrValidated)
                    {
                        sb.AppendLine();
                        sb.AppendLine("注意: 响应中的地址字节与请求不匹配，");
                        sb.AppendLine("  可能设备使用了不同的响应格式。");
                        sb.AppendLine("  以上数据值基于偏移量提取，请核实。");
                    }

                    MessageBox.Show(sb.ToString(),
                                   addrValidated ? "读取成功" : "读取完成(需验证)",
                                   MessageBoxButton.OK,
                                   addrValidated ? MessageBoxImage.Information : MessageBoxImage.Warning);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"读取第 {item.Index} 行配置时发生错误！\n\n" +
                                   $"错误信息: {ex.Message}",
                                   "读取错误",
                                   MessageBoxButton.OK,
                                   MessageBoxImage.Error);
                }
                finally
                {
                    // 恢复按钮状态
                    button.IsEnabled = true;
                    button.Content = originalContent;
                }
            }
        }

        private void OnLoadExampleClick(object sender, RoutedEventArgs e)
        {
            if (_viewModel.IICRegisterList.Count > 0)
            {
                var result = MessageBox.Show(
                    "当前列表已有数据。加载示例将替换现有内容，是否继续？",
                    "确认加载",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes) return;

                _viewModel.ClearIICRegisterList();
            }

            var exampleData = new List<Tuple<ushort, ushort>>
            {
                Tuple.Create((ushort)0x12, (ushort)0x40),
                Tuple.Create((ushort)0x48, (ushort)0x85),
                Tuple.Create((ushort)0x48, (ushort)0x05),
                Tuple.Create((ushort)0x0E, (ushort)0x11),
                Tuple.Create((ushort)0x0F, (ushort)0x84),
                Tuple.Create((ushort)0x10, (ushort)0x20),
                Tuple.Create((ushort)0x11, (ushort)0x80),
                Tuple.Create((ushort)0x57, (ushort)0x60),
                Tuple.Create((ushort)0x58, (ushort)0x18),
                Tuple.Create((ushort)0x61, (ushort)0x10),
                Tuple.Create((ushort)0x46, (ushort)0x00),
                Tuple.Create((ushort)0x0D, (ushort)0xA0),
            };

            foreach (var data in exampleData)
            {
                var item = new IICRegisterItem
                {
                    Index = _viewModel.IICRegisterList.Count + 1,
                    Address = data.Item1,
                    Data = data.Item2
                };
                _viewModel.IICRegisterList.Add(item);
            }

            RaisePropertyChangedEvents();

            MessageBox.Show($"已成功加载 {exampleData.Count} 个示例寄存器配置！\n\n" +
                           "示例数据（含1字节和2字节地址）：\n" +
                           "0x12, 0x40 → 打包: 0x12, 0x40 (2字节)\n" +
                           "0x48, 0x85 → 打包: 0x48, 0x85 (2字节)\n" +
                           "...\n" +
                           "0x1234, 0x40 → 打包: 0x12, 0x34, 0x40 (3字节)\n" +
                           $"... 共{exampleData.Count}行",
                           "加载成功",
                           MessageBoxButton.OK,
                           MessageBoxImage.Information);
        }

        private void OnImportClick(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog openDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "配置文件 (*.txt;*.csv)|*.txt;*.csv|文本文件 (*.txt)|*.txt|CSV文件 (*.csv)|*.csv|所有文件 (*.*)|*.*",
                DefaultExt = "txt",
                Title = "导入IIC寄存器配置文件"
            };

            if (openDialog.ShowDialog() != true) return;

            try
            {
                string[] lines = System.IO.File.ReadAllLines(openDialog.FileName, System.Text.Encoding.UTF8);

                List<Tuple<ushort, ushort>> importedData = new List<Tuple<ushort, ushort>>();
                int lineNumber = 0;
                int successCount = 0;
                int skipCount = 0;
                List<string> errorLines = new List<string>();

                foreach (string line in lines)
                {
                    lineNumber++;
                    string trimmedLine = line.Trim();

                    if (string.IsNullOrEmpty(trimmedLine)) continue;
                    if (trimmedLine.StartsWith("#") || trimmedLine.StartsWith("//") || trimmedLine.StartsWith(";")) continue;

                    Tuple<ushort, ushort> parsed = ParseConfigLine(trimmedLine, lineNumber);

                    if (parsed != null)
                    {
                        importedData.Add(parsed);
                        successCount++;
                    }
                    else
                    {
                        errorLines.Add($"第{lineNumber}行: {trimmedLine}");
                        skipCount++;
                    }
                }

                if (importedData.Count == 0)
                {
                    MessageBox.Show("文件中没有找到有效的寄存器配置数据！\n\n" +
                                   "支持的格式：\n" +
                                   "• 0x12, 0x40\n" +
                                   "• 12, 64 (十进制)\n" +
                                   "• 0x1234, 0x40\n" +
                                   "• Address:0x12 Data:0x40",
                                   "导入失败",
                                   MessageBoxButton.OK,
                                   MessageBoxImage.Warning);
                    return;
                }

                if (_viewModel.IICRegisterList.Count > 0)
                {
                    var result = MessageBox.Show(
                        $"当前列表已有 {_viewModel.TotalRegisterCount} 个配置。\n\n" +
                        $"从文件中解析到 {successCount} 个有效配置。\n\n" +
                        "选择操作：\n" +
                        "[是] 追加到现有列表\n" +
                        "[否] 替换现有列表",
                        "确认导入",
                        MessageBoxButton.YesNoCancel,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Cancel) return;

                    if (result == MessageBoxResult.No)
                    {
                        _viewModel.ClearIICRegisterList();
                    }
                }

                foreach (var data in importedData)
                {
                    var item = new IICRegisterItem
                    {
                        Index = _viewModel.IICRegisterList.Count + 1,
                        Address = data.Item1,
                        Data = data.Item2
                    };
                    _viewModel.IICRegisterList.Add(item);
                }

                RaisePropertyChangedEvents();

                string message = $"✓ 导入成功！\n\n";
                message += $"文件: {System.IO.Path.GetFileName(openDialog.FileName)}\n";
                message += $"总行数: {lineNumber}\n";
                message += $"成功导入: {successCount} 个配置\n";
                message += $"当前总数: {_viewModel.TotalRegisterCount} 个配置";

                if (skipCount > 0)
                {
                    message += $"\n\n⚠️ 跳过 {skipCount} 行无效数据:\n";
                    int showErrors = Math.Min(errorLines.Count, 5);
                    for (int i = 0; i < showErrors; i++)
                    {
                        message += $"• {errorLines[i]}\n";
                    }
                    if (errorLines.Count > 5)
                    {
                        message += $"... 还有 {errorLines.Count - 5} 行\n";
                    }
                }

                MessageBox.Show(message,
                               "导入完成",
                               MessageBoxButton.OK,
                               skipCount > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导入文件时发生错误：\n\n{ex.Message}",
                              "导入错误",
                              MessageBoxButton.OK,
                              MessageBoxImage.Error);
            }
        }

        private Tuple<ushort, ushort> ParseConfigLine(string line, int lineNumber)
        {
            try
            {
                string processedLine = line;

                if (processedLine.Contains(":"))
                {
                    var parts = processedLine.Split(new char[] { ':', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    ushort? addrValue = null;
                    ushort? dataValue = null;

                    for (int i = 0; i < parts.Length - 1; i++)
                    {
                        string key = parts[i].Trim().ToLower();
                        string value = parts[i + 1].Trim();

                        if ((key == "address" || key == "addr" || key == "地址") && !addrValue.HasValue)
                        {
                            addrValue = ParseHexOrDecimal(value);
                        }
                        else if ((key == "data" || key == "数据") && !dataValue.HasValue)
                        {
                            dataValue = ParseHexOrDecimal(value);
                        }
                    }

                    if (addrValue.HasValue && dataValue.HasValue)
                    {
                        return Tuple.Create(addrValue.Value, dataValue.Value);
                    }
                }
                else
                {
                    string[] separators = { ",", ";", "\t", " ", "|" };
                    string[] values = processedLine.Split(separators, StringSplitOptions.RemoveEmptyEntries);

                    if (values.Length >= 2)
                    {
                        ushort addrValue = ParseHexOrDecimal(values[0].Trim());
                        ushort dataValue = ParseHexOrDecimal(values[1].Trim());

                        if (addrValue <= 0xFFFF && dataValue <= 0xFF)
                        {
                            return Tuple.Create(addrValue, dataValue);
                        }
                    }
                }
            }
            catch (Exception)
            {

            }

            return null;
        }

        private ushort ParseHexOrDecimal(string valueStr)
        {
            string trimmed = valueStr.Trim();

            if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("&H", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("0X", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring(2);
                return Convert.ToUInt16(trimmed, 16);
            }
            else if (trimmed.StartsWith("0b", StringComparison.OrdinalIgnoreCase) ||
                     trimmed.StartsWith("&B", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring(2);
                return Convert.ToUInt16(trimmed, 2);
            }
            else
            {
                bool isAllHex = System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^[0-9a-fA-F]+$");

                if (isAllHex && trimmed.Length >= 2 && trimmed.Length <= 4)
                {
                    try
                    {
                        return Convert.ToUInt16(trimmed, 16);
                    }
                    catch
                    {

                    }
                }

                return Convert.ToUInt16(trimmed);
            }
        }

        #endregion

        #region DataGrid 编辑优化事件

        private IICRegisterItem _lastEditedItem = null;
        private string _lastEditedProperty = null;
        private object _lastEditedValue = null;

        private void OnDataGridCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction == DataGridEditAction.Commit)
            {
                if (e.Row.Item is IICRegisterItem editedItem)
                {
                    _lastEditedItem = editedItem;
                    _lastEditedProperty = e.Column.SortMemberPath;

                    if (e.EditingElement is TextBox textBox)
                    {
                        _lastEditedValue = textBox.Text;
                    }

                    try
                    {
                        string message = $"✓ 已保存修改\n\n";
                        message += $"行号: #{editedItem.Index}\n";

                        if (_lastEditedProperty == "Address")
                        {
                            message += $"修改字段: 寄存器地址\n";
                            message += $"新值: {editedItem.AddressHex}\n";
                            message += $"打包结果: {editedItem.PackedHex}";
                        }
                        else if (_lastEditedProperty == "Data")
                        {
                            message += $"修改字段: 数据值\n";
                            message += $"新值: {editedItem.DataHex}\n";
                            message += $"打包结果: {editedItem.PackedHex}";
                        }
                        else
                        {
                            message += $"已更新配置项";
                        }

                        System.Diagnostics.Debug.WriteLine($"[IICConfig] CellEditEnding: 行#{editedItem.Index}, 字段:{_lastEditedProperty}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[IICConfig] CellEditEnding Error: {ex.Message}");
                    }
                }
            }
            else if (e.EditAction == DataGridEditAction.Cancel)
            {
                System.Diagnostics.Debug.WriteLine("[IICConfig] CellEditEnding: 编辑已取消");
                _lastEditedItem = null;
                _lastEditedProperty = null;
                _lastEditedValue = null;
            }
        }

        private void OnDataGridCurrentCellChanged(object sender, EventArgs e)
        {
            if (sender is DataGrid dataGrid && dataGrid.CurrentItem is IICRegisterItem currentItem)
            {
                if (_lastEditedItem != null && _lastEditedItem != currentItem)
                {
                    try
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[IICConfig] 切换到行#{currentItem.Index}，" +
                            $"上一编辑: 行#{_lastEditedItem?.Index ?? 0} ({_lastEditedProperty})");

                        HighlightRowTemporarily(_lastEditedItem);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[IICConfig] CurrentCellChanged Error: {ex.Message}");
                    }

                    _lastEditedItem = currentItem;
                }

                UpdateStatusBar(currentItem);
            }
        }

        private void OnDataGridSelectedCellsChanged(object sender, SelectedCellsChangedEventArgs e)
        {
            if (DgRegisterList.SelectedItem is IICRegisterItem selectedItem)
            {
                HighlightRow(selectedItem);
            }
        }

        private void HighlightRowTemporarily(IICRegisterItem item)
        {
            if (item == null || DgRegisterList == null) return;

            try
            {
                var rowContainer = GetRowFromItem(item);
                if (rowContainer != null)
                {
                    SolidColorBrush brush = new SolidColorBrush(Colors.LightGreen);
                    rowContainer.Background = brush;

                    ColorAnimation animation = new ColorAnimation
                    {
                        From = Colors.LightGreen,
                        To = Colors.White,
                        Duration = TimeSpan.FromSeconds(1.5),
                        AutoReverse = false
                    };

                    brush.BeginAnimation(SolidColorBrush.ColorProperty, animation);

                    System.Diagnostics.Debug.WriteLine($"[IICConfig] 高亮显示行#{item.Index}（保存成功）");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[IICConfig] HighlightRow Error: {ex.Message}");
            }
        }

        private void HighlightRow(IICRegisterItem item)
        {
            if (item == null) return;

            try
            {
                var rowContainer = GetRowFromItem(item);
                if (rowContainer != null)
                {
                    rowContainer.Background = new SolidColorBrush(Color.FromArgb(30, 33, 150, 243));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[IICConfig] HighlightRow Error: {ex.Message}");
            }
        }

        private DataGridRow GetRowFromItem(IICRegisterItem item)
        {
            if (item == null || DgRegisterList == null) return null;

            try
            {
                DgRegisterList.UpdateLayout();
                return DgRegisterList.ItemContainerGenerator.ContainerFromItem(item) as DataGridRow;
            }
            catch
            {
                return null;
            }
        }

        private void UpdateStatusBar(IICRegisterItem currentItem)
        {

        }

        private void OnDataGridPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is DataGrid dataGrid)
            {
                if (IsDataGridEditing(dataGrid))
                {
                    var hitTestResult = VisualTreeHelper.HitTest(dataGrid, e.GetPosition(dataGrid));

                    if (hitTestResult != null && hitTestResult.VisualHit != null)
                    {
                        bool isClickOnCell = false;
                        DependencyObject current = hitTestResult.VisualHit;

                        while (current != null && current != dataGrid)
                        {
                            if (current is DataGridCell || current is DataGridRow)
                            {
                                isClickOnCell = true;
                                break;
                            }
                            current = VisualTreeHelper.GetParent(current);
                        }

                        if (!isClickOnCell)
                        {
                            System.Diagnostics.Debug.WriteLine("[IICConfig] 点击空白区域 - 强制提交编辑");

                            try
                            {
                                dataGrid.CommitEdit(DataGridEditingUnit.Row, true);
                                HighlightRowTemporarily(_lastEditedItem);
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"[IICConfig] CommitEdit Error: {ex.Message}");
                                try
                                {
                                    dataGrid.CancelEdit(DataGridEditingUnit.Row);
                                    System.Diagnostics.Debug.WriteLine("[IICConfig] 已取消编辑");
                                }
                                catch (Exception cancelEx)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[IICConfig] CancelEdit Error: {cancelEx.Message}");
                                }
                            }
                        }
                    }
                }
            }
        }

        private void OnDataGridLostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is DataGrid dataGrid)
            {
                if (IsDataGridEditing(dataGrid))
                {
                    System.Diagnostics.Debug.WriteLine("[IICConfig] DataGrid失去焦点 - 提交当前编辑");

                    try
                    {
                        dataGrid.CommitEdit(DataGridEditingUnit.Row, true);

                        if (_lastEditedItem != null)
                        {
                            HighlightRowTemporarily(_lastEditedItem);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[IICConfig] LostFocus CommitEdit Error: {ex.Message}");
                        try
                        {
                            dataGrid.CancelEdit(DataGridEditingUnit.Row);
                        }
                        catch (Exception cancelEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"[IICConfig] LostFocus CancelEdit Error: {cancelEx.Message}");
                        }
                    }
                }
            }
        }

        private bool IsDataGridEditing(DataGrid dataGrid)
        {
            if (dataGrid == null) return false;

            try
            {
                if (dataGrid.CurrentItem == null || dataGrid.CurrentColumn == null)
                    return false;

                DependencyObject focusedElement = FocusManager.GetFocusedElement(dataGrid) as DependencyObject;

                if (focusedElement is TextBox || focusedElement is ComboBox || focusedElement is CheckBox)
                    return true;

                foreach (var item in dataGrid.Items)
                {
                    DataGridRow row = dataGrid.ItemContainerGenerator.ContainerFromItem(item) as DataGridRow;
                    if (row != null && row.IsEditing)
                        return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region 输入验证

        private void OnDataGridInputPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                string proposedText = textBox.Text.Remove(textBox.SelectionStart, textBox.SelectionLength)
                                       .Insert(textBox.CaretIndex, e.Text);

                if (!string.IsNullOrEmpty(proposedText))
                {
                    string trimmed = proposedText.Trim();

                    bool isHexFormat = trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
                                       trimmed.StartsWith("&H", StringComparison.OrdinalIgnoreCase);

                    string valueToParse = trimmed;
                    if (isHexFormat)
                    {
                        valueToParse = trimmed.Substring(2);
                        if (string.IsNullOrEmpty(valueToParse))
                        {
                            return;
                        }
                    }

                    bool isValidHex = System.Text.RegularExpressions.Regex.IsMatch(
                        valueToParse,
                        @"^[0-9a-fA-F]*$");

                    if (!isValidHex)
                    {
                        e.Handled = true;
                        return;
                    }

                    try
                    {
                        ushort parsedValue = Convert.ToUInt16(valueToParse,
                            isHexFormat ? 16 : 10);

                        if (parsedValue > 0xFFFF)
                        {
                            e.Handled = true;
                            MessageBox.Show("请输入0-65535范围内的数值 (0x0000-0xFFFF)\n" +
                                          "当前值: " + trimmed + " (" + parsedValue + ")",
                                          "输入范围错误",
                                          MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                    }
                    catch
                    {
                        e.Handled = true;
                    }
                }
            }
        }

        #endregion

        #region 底部按钮事件

        private void OnApplyClick(object sender, RoutedEventArgs e)
        {
            if (_viewModel.IICRegisterList.Count == 0)
            {
                MessageBox.Show("当前没有配置任何寄存器！请先添加寄存器配置。", "提示",
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            List<byte> bytesList = new List<byte>();
            foreach (var item in _viewModel.IICRegisterList)
            {
                bytesList.AddRange(item.PackedBytes);
            }
            byte[] bytes = bytesList.ToArray();

            RaisePropertyChangedEvents();

            bool writeOk = _viewModel.WriteIICConfigsToDevice(bytes);

            if (writeOk)
            {
                string message = $"成功写入 {_viewModel.TotalRegisterCount} 个寄存器配置！\n\n";
                message += "配置摘要：\n";
                message += _viewModel.AllRegistersSummary;

                MessageBox.Show(message, "写入成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show($"批量写入 {_viewModel.TotalRegisterCount} 个寄存器配置失败！\n\n" +
                               "请检查设备连接是否正常。",
                               "写入失败",
                               MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnExportClick(object sender, RoutedEventArgs e)
        {
            if (_viewModel.IICRegisterList.Count == 0)
            {
                MessageBox.Show("当前没有可导出的数据！请先添加寄存器配置。", "提示",
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                Microsoft.Win32.SaveFileDialog saveDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "文本文件 (*.txt)|*.txt|CSV文件 (*.csv)|*.csv|所有文件 (*.*)|*.*",
                    DefaultExt = "txt",
                    Title = "导出IIC寄存器配置",
                    FileName = $"IIC_Config_{DateTime.Now:yyyyMMdd_HHmmss}"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    System.IO.StreamWriter writer = new System.IO.StreamWriter(saveDialog.FileName);
                    writer.WriteLine("# IIC 寄存器配置文件");
                    writer.WriteLine("# 导出时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    writer.WriteLine("# 寄存器地址, 数据值");
                    writer.WriteLine("# ---------------------------");

                    foreach (var item in _viewModel.IICRegisterList)
                    {
                        writer.WriteLine($"{item.AddressHex}, {item.DataHex}");
                    }

                    writer.Close();

                    MessageBox.Show($"配置已成功导出到:\n{saveDialog.FileName}\n\n" +
                                   $"共 {_viewModel.TotalRegisterCount} 个寄存器",
                                   "导出成功",
                                   MessageBoxButton.OK,
                                   MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导出失败：\n{ex.Message}", "错误",
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            this.Close();
        }


        #endregion

        #region 辅助方法

        private void RaisePropertyChangedEvents()
        {
            _viewModel.RaisePropertyChanged("IICRegisterList");
            _viewModel.RaisePropertyChanged("TotalRegisterCount");
            _viewModel.RaisePropertyChanged("AllRegistersSummary");
        }

        #endregion
    }
}