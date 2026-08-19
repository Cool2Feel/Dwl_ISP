namespace ResBinManager.ViewModels
{
    using ResBinManager.Core;
    using ResBinManager.Models;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using System.Windows;
    using DialogResult = System.Windows.Forms.DialogResult;
    using FolderBrowserDialog = System.Windows.Forms.FolderBrowserDialog;
    using MessageBox = System.Windows.MessageBox;
    using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
    using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

    public partial class MainViewModel
    {
        private bool CanExecuteLoadConfig(object? parameter)
        {
            return IsDestBinMode && !string.IsNullOrEmpty(_currentFilePath) && _currentFileExists && !IsLoading;
        }

        private async void ExecuteLoadConfig(object? parameter)
        {
            try
            {
                await ExecuteLoadConfigAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ExecuteLoadConfig] Exception: {ex}");
                StatusMessage = $"配置加载失败: {ex.Message}";
                MessageBox.Show($"配置加载失败:\n{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task ExecuteLoadConfigAsync()
        {
            if (IsLoading) return;
            if (!IsDestBinMode || string.IsNullOrEmpty(_currentFilePath))
            {
                MessageBox.Show("请先打开 DestBin.bin 文件", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!File.Exists(_currentFilePath))
            {
                _currentFileExists = false;
                MessageBox.Show("当前文件已不存在，请重新打开", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            IsLoading = true;
            StatusMessage = "正在解析配置区...";

            var projectType = SelectedProjectType;

            FirmwareConfigData = await Task.Run(() =>
                ConfigParser.ParseConfigFromDestBin(_currentFilePath, projectType));

            if (FirmwareConfigData.ConfigAddress == 0)
            {
                StatusMessage = "配置区解析失败";
                MessageBox.Show($"配置区解析失败\n{FirmwareConfigData.StatusMessage}",
                              "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!FirmwareConfigData.IsValid)
            {
                var result = MessageBox.Show(
                    "配置区为空白或无效，请选择处理方式：\n\n" +
                    //"是(Y) - 加载默认配置（基于 Default_Config.xml）\n" +
                    "是(Y) - 从 XML 配置文件加载配置\n" +
                    "否(N) - 保持空白配置",
                    "配置区空白",
                    MessageBoxButton.YesNo,//YesNoCancel,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                //    LoadDefaultConfigFromXml();
                //    RefreshConfigItems();
                //    StatusMessage = $"已加载默认配置 (项目: {SelectedProjectType}, 地址: 0x{FirmwareConfigData.ConfigAddress:X})，请修改后保存";
                //    return;
                //}
                //else if (result == MessageBoxResult.No)
                //{
                    bool loaded = LoadConfigFromXmlFile();
                    if (!loaded && ConfigItems.Count == 0)
                    {
                        RefreshConfigItems();
                        OnPropertyChanged(nameof(ConfigItems));
                    }
                    if (!loaded)
                    {
                        StatusMessage = $"XML 配置加载已取消，已恢复当前配置状态 (项目: {SelectedProjectType})";
                    }
                    return;
                }

                ConfigItems.Clear();
                StatusMessage = $"配置区空白 (项目: {SelectedProjectType}, 地址: 0x{FirmwareConfigData.ConfigAddress:X})";
                return;
            }

            ConfigItems.Clear();
            _loadedXmlFilePath = string.Empty;
            RefreshConfigItems();
            StatusMessage = $"配置加载成功 (项目: {SelectedProjectType}, 地址: 0x{FirmwareConfigData.ConfigAddress:X})";
        }

        private void LoadDefaultConfigFromXml()
        {
            if (FirmwareConfigData == null)
                return;

            _loadedXmlFilePath = string.Empty;

            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                string resourceName = "ResBinManager.Resources.Default_Config.xml";

                ConfigXmlParser.ParseResult parseResult;
                using (var stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                    {
                        StatusMessage = "默认配置资源不存在";
                        MessageBox.Show("默认配置资源 Default_Config.xml 不存在，请检查嵌入式资源配置", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    parseResult = ConfigXmlParser.ParseFromStreamWithConstants(stream);
                }

                var parsedItems = parseResult.Items;

                if (parsedItems.Count == 0)
                {
                    StatusMessage = "默认配置文件为空";
                    MessageBox.Show("默认配置文件为空，请检查文件内容", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (parseResult.StringConstants.Count > 0)
                {
                    ConfigOptionsCache.Clear();
                    var dynamicConstants = FirmwareConstants.CreateDynamic(parseResult.StringConstants, parseResult.RIdTypeStrBase);
                    ConfigOptionsCache.SetDynamicConstants(dynamicConstants);
                }

                bool loadResult = ConfigWriter.ResetFromXmlParsedItems(FirmwareConfigData, parsedItems);
                if (!loadResult)
                {
                    StatusMessage = "从默认配置文件加载配置失败";
                    MessageBox.Show("从默认配置文件加载配置失败，请检查日志", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                IsConfigModified = true;
                (SaveConfigCommand as RelayCommand)?.RaiseCanExecuteChanged();
                StatusMessage = $"已从默认配置文件加载配置 ({parsedItems.Count} 项)，请修改后保存到固件";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LoadDefaultConfigFromXml] Exception: {ex}");
                StatusMessage = "加载默认配置文件失败";
                MessageBox.Show($"加载默认配置文件失败:\n{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool LoadConfigFromXmlFile()
        {
            if (FirmwareConfigData == null)
                return false;

            var openFileDialog = new OpenFileDialog
            {
                Filter = "XML配置文件 (*.xml)|*.xml|所有文件 (*.*)|*.*",
                Title = "选择 XML 配置文件",
                InitialDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config")
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    if (!File.Exists(openFileDialog.FileName))
                    {
                        MessageBox.Show("选择的文件不存在", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                        return false;
                    }

                    _loadedXmlFilePath = openFileDialog.FileName;

                    var parseResult = ConfigXmlParser.ParseFromFileWithConstants(openFileDialog.FileName);
                    var parsedItems = parseResult.Items;

                    if (parsedItems.Count == 0)
                    {
                        MessageBox.Show("未从 XML 文件中解析到任何配置项，请检查文件格式", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return false;
                    }

                    if (parseResult.StringConstants.Count > 0)
                    {
                        ConfigOptionsCache.Clear();
                        var dynamicConstants = FirmwareConstants.CreateDynamic(parseResult.StringConstants, parseResult.RIdTypeStrBase);
                        ConfigOptionsCache.SetDynamicConstants(dynamicConstants);
                    }

                    List<string> validationErrors = new List<string>();
                    List<string> validationWarnings = new List<string>();

                    var invalidIndexItems = parsedItems.Where(x => x.Index < 0).ToList();
                    if (invalidIndexItems.Any())
                    {
                        validationErrors.Add($"以下配置项索引无效(负数): {string.Join(", ", invalidIndexItems.Select(x => x.ConfigName))}");
                    }

                    var outOfRangeItems = parsedItems.Where(x => x.Index >= ConfigParser.SDK_CONFIG_ID_MAX).ToList();
                    if (outOfRangeItems.Any())
                    {
                        validationErrors.Add($"以下配置项索引超出有效范围 [0, {ConfigParser.SDK_CONFIG_ID_MAX - 1}]: {string.Join(", ", outOfRangeItems.Select(x => $"{x.ConfigName}(索引:{x.Index})"))}");
                    }

                    var duplicateIndexItems = parsedItems.GroupBy(x => x.Index).Where(g => g.Count() > 1).ToList();
                    if (duplicateIndexItems.Any())
                    {
                        validationErrors.Add($"发现重复索引: {string.Join(", ", duplicateIndexItems.Select(g => $"索引{g.Key}({g.Count()}个)"))}");
                    }

                    var invalidNameItems = parsedItems.Where(x => string.IsNullOrEmpty(x.ConfigName)).ToList();
                    if (invalidNameItems.Any())
                    {
                        validationErrors.Add($"发现 {invalidNameItems.Count} 个配置项缺少配置名称");
                    }

                    var unknownNameItems = parsedItems.Where(x => !string.IsNullOrEmpty(x.ConfigName) && !Enum.IsDefined(typeof(ConfigId), x.ConfigName)).ToList();
                    if (unknownNameItems.Any())
                    {
                        validationWarnings.Add($"以下配置项名称未在 ConfigId 枚举中定义: {string.Join(", ", unknownNameItems.Select(x => x.ConfigName))}");
                    }

                    if (validationErrors.Any())
                    {
                        string errorMessage = "XML配置文件验证失败:\n\n" + string.Join("\n", validationErrors);
                        MessageBox.Show(errorMessage, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                        return false;
                    }

                    if (validationWarnings.Any())
                    {
                        string warningMessage = "XML配置文件验证警告:\n\n" + string.Join("\n", validationWarnings) + "\n\n是否继续加载?";
                        var result = MessageBox.Show(warningMessage, "验证警告", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                        if (result == MessageBoxResult.No)
                            return false;
                    }

                    bool loadResult = ConfigWriter.ResetFromXmlParsedItems(FirmwareConfigData, parsedItems);
                    if (!loadResult)
                    {
                        MessageBox.Show("从 XML 配置文件加载配置失败，请检查日志", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                        return false;
                    }

                    IsConfigModified = true;
                    (SaveConfigCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    StatusMessage = $"已从 XML 配置文件加载配置 ({parsedItems.Count} 项)，请修改后保存到固件";

                    RefreshConfigItems();

                    // 同步更新资源列表中的 Name 显示（基于 RES.H 资源定义）
                    int updatedCount = UpdateResourceNamesFromDefinitions(parseResult.ResourceDefinitions);
                    if (updatedCount > 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"[LoadConfigFromXmlFile] Updated {updatedCount} resource names from RES.H definitions");
                    }

                    MessageBox.Show($"成功从 XML 配置文件加载配置 ({parsedItems.Count} 项)\n\n您可以在界面上修改配置，然后点击保存按钮将配置写入固件。",
                                  "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    return true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[LoadConfigFromXmlFile] Exception: {ex}");
                    StatusMessage = $"加载 XML 配置文件失败: {ex.Message}";
                    MessageBox.Show($"加载 XML 配置文件失败:\n{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }
            }
            return false;
        }

        private void RefreshConfigItems()
        {
            if (FirmwareConfigData == null)
                return;

            try
            {
                List<FirmwareConfigItem> items;

                if (FirmwareConfigData.XmlParsedItems != null && FirmwareConfigData.XmlParsedItems.Count > 0)
                {
                    items = BuildConfigItemsFromXmlParsed(FirmwareConfigData);
                }
                else
                {
                    items = ConfigParser.BuildConfigItemList(FirmwareConfigData);
                }

                ConfigItems = new System.Collections.ObjectModel.ObservableCollection<FirmwareConfigItem>(items);
                OnPropertyChanged(nameof(ConfigItems));
            }
            catch (Exception ex)
            {
                StatusMessage = $"配置刷新失败: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"[VM] RefreshConfigItems failed: {ex.Message}");
            }
        }

        /// <summary>
        /// 根据从 XML 配置文件中加载的 RES.H 资源定义，同步更新资源列表中各项的 Name 显示。
        /// 当配置文件中包含 ResourceDefinitions 节时，用其中的资源名称覆盖资源列表的 Name 字段，
        /// 使 DataGrid 中 Name 列的显示与当前项目 RES.H 保持一致。
        /// </summary>
        /// <param name="resourceDefinitions">资源 Id -> 资源名称 的映射</param>
        /// <returns>成功更新的资源项数量</returns>
        private int UpdateResourceNamesFromDefinitions(Dictionary<uint, string> resourceDefinitions)
        {
            if (resourceDefinitions == null || resourceDefinitions.Count == 0 || Resources == null || Resources.Count == 0)
                return 0;

            int updatedCount = 0;

            foreach (var resource in Resources)
            {
                if (resource == null)
                    continue;

                if (resourceDefinitions.TryGetValue(resource.Id, out string? newName) && !string.IsNullOrEmpty(newName))
                {
                    if (!string.Equals(resource.Name, newName, StringComparison.Ordinal))
                    {
                        resource.Name = newName;
                        updatedCount++;
                    }
                }
            }

            if (updatedCount > 0)
            {
                // 触发 Resources 集合的刷新通知，确保 DataGrid 同步更新 Name 列显示
                OnPropertyChanged(nameof(Resources));
                System.Diagnostics.Debug.WriteLine($"[UpdateResourceNamesFromDefinitions] Updated {updatedCount} resource names");
            }

            return updatedCount;
        }

        private List<FirmwareConfigItem> BuildConfigItemsFromXmlParsed(FirmwareConfigData configData)
        {
            var items = new List<FirmwareConfigItem>();

            if (configData.XmlParsedItems == null)
                return items;

            foreach (var parsedItem in configData.XmlParsedItems)
            {
                int index = parsedItem.Index;
                if (index < 0)
                {
                    if (Enum.TryParse<ConfigId>(parsedItem.ConfigName, out var configId))
                    {
                        index = (int)configId;
                    }
                    else
                    {
                        continue;
                    }
                }

                if (index < 0 || index >= ConfigParser.CONFIG_FLAGS_COUNT)
                {
                    System.Diagnostics.Debug.WriteLine($"[BuildConfigItemsFromXmlParsed] Skipping item '{parsedItem.ConfigName}' with out-of-range index {index}");
                    continue;
                }

                uint value = parsedItem.Value;
                string configName = string.IsNullOrEmpty(parsedItem.ConfigName) ? $"CONFIG_ID_{index}" : parsedItem.ConfigName;

                ConfigItemType effectiveType = ConfigItemType.Numeric;
                if (!string.IsNullOrEmpty(parsedItem.Type) && Enum.TryParse<ConfigItemType>(parsedItem.Type, out var xmlType))
                {
                    effectiveType = xmlType;
                }
                else
                {
                    var descriptor = ConfigItemRegistry.GetDescriptor(configName);
                    effectiveType = descriptor?.Type ?? ConfigItemType.Numeric;
                }

                var options = new List<ConfigOption>();
                if (parsedItem.Options != null && parsedItem.Options.Count > 0)
                {
                    foreach (var opt in parsedItem.Options)
                    {
                        options.Add(new ConfigOption(opt.Value, opt.DisplayName));
                    }
                }
                else
                {
                    options = ConfigOptionsCache.GetOptions(effectiveType);
                }

                string displayName = string.IsNullOrEmpty(parsedItem.DisplayName)
                    ? ConfigItemRegistry.GetMetadataOrDefault(configName).DisplayName
                    : parsedItem.DisplayName;
                string category = string.IsNullOrEmpty(parsedItem.Category)
                    ? ConfigItemRegistry.GetMetadataOrDefault(configName).Category
                    : parsedItem.Category;

                string displayText;
                if (options != null && options.Count > 0)
                {
                    var matchedOption = options.FirstOrDefault(o => o.Value == value);
                    if (matchedOption != null)
                    {
                        displayText = matchedOption.DisplayName;
                    }
                    else
                    {
                        var formatter = ConfigDisplayFormatters.GetFormatter(effectiveType);
                        displayText = formatter(value);
                        System.Diagnostics.Debug.WriteLine($"[BuildConfigItemsFromXmlParsed] Value {value} for config '{configName}' does not match any option, using formatted display: {displayText}");
                        options.Add(new ConfigOption(value, displayText));
                    }
                }
                else
                {
                    var formatter = ConfigDisplayFormatters.GetFormatter(effectiveType);
                    displayText = formatter(value);
                    options.Add(new ConfigOption(value, displayText));
                }
                items.Add(new FirmwareConfigItem
                {
                    Id = (ConfigId)index,
                    Name = displayName,
                    Value = value,
                    ValueDisplay = displayText,
                    Category = category,
                    Options = options,
                    Enabled = parsedItem.Enabled
                });
            }

            return items;
        }

        private bool CanExecuteLoadXmlConfig(object? parameter)
        {
            return !IsLoading && FirmwareConfigData != null;
        }

        private void ExecuteLoadXmlConfig()
        {
            if (IsLoading)
            {
                MessageBox.Show("正在处理中，请稍候...", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                IsLoading = true;

                bool loaded = LoadConfigFromXmlFile();
                if (!loaded)
                {
                    StatusMessage = "XML 配置加载已取消";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = "XML 配置加载失败";
                MessageBox.Show($"XML 配置加载失败:\n{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void ExecuteRefreshConfig()
        {
            if (FirmwareConfigData == null)
                return;

            try
            {
                IsLoading = true;

                SyncConfigItemsToFlags();

                RefreshConfigItems();

                StatusMessage = "配置显示已刷新";
            }
            catch (Exception ex)
            {
                StatusMessage = "配置刷新失败";
                string errorDetails = $"配置刷新失败:\n\n类型: {ex.GetType().Name}\n消息: {ex.Message}";
                if (ex.InnerException != null)
                {
                    errorDetails += $"\n\n内部异常:\n类型: {ex.InnerException.GetType().Name}\n消息: {ex.InnerException.Message}";
                }
                errorDetails += $"\n\n堆栈跟踪:\n{ex.StackTrace}";
                MessageBox.Show(errorDetails, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private bool CanExecuteRefreshConfig()
        {
            return FirmwareConfigData != null && !IsLoading;
        }

        private void SyncConfigItemsToFlags()
        {
            if (FirmwareConfigData == null || ConfigItems == null)
                return;

            foreach (var item in ConfigItems)
            {
                if (!Enum.IsDefined(typeof(ConfigId), item.Id))
                {
                    System.Diagnostics.Debug.WriteLine($"[SyncConfigItemsToFlags] Skipping item with invalid Id: {item.Id}");
                    continue;
                }

                int index = (int)item.Id;
                if (index >= 0 && index < FirmwareConfigData.Flags.Length)
                {
                    if (FirmwareConfigData.Flags[index] != item.Value)
                    {
                        FirmwareConfigData.Flags[index] = item.Value;
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[SyncConfigItemsToFlags] Skipping item with out-of-range index: {index}");
                }
            }

            FirmwareConfigData.CheckSum = FirmwareConfigData.CalculateCheckSum();
        }

        private void SyncConfigItemsToXmlParsed()
        {
            if (FirmwareConfigData == null || ConfigItems == null || FirmwareConfigData.XmlParsedItems == null)
                return;

            foreach (var item in ConfigItems)
            {
                int index = (int)item.Id;
                var parsedItem = FirmwareConfigData.XmlParsedItems.FirstOrDefault(x => x.Index == index);

                if (parsedItem == null)
                {
                    parsedItem = FirmwareConfigData.XmlParsedItems.FirstOrDefault(x =>
                    {
                        if (Enum.TryParse<ConfigId>(x.ConfigName, out var configId))
                            return (int)configId == index;
                        return false;
                    });
                }

                if (parsedItem != null)
                {
                    parsedItem.Value = item.Value;
                    parsedItem.Enabled = item.Enabled;

                    if (item.Options != null && item.Options.Count > 0)
                    {
                        parsedItem.Options = item.Options.Select(o => new ConfigOption(o.Value, o.DisplayName)).ToList();
                    }
                }
            }
        }

        private void ReloadConfigWithNewProjectType()
        {
            if (string.IsNullOrEmpty(_currentFilePath))
                return;

            var oldConfigData = FirmwareConfigData;

            try
            {
                IsLoading = true;

                var newConfigData = ConfigParser.ParseConfigFromDestBin(_currentFilePath, SelectedProjectType);

                if (newConfigData == null || newConfigData.ConfigAddress == 0)
                {
                    return;
                }

                newConfigData.ConfigAddress = oldConfigData?.ConfigAddress ?? newConfigData.ConfigAddress;

                if (oldConfigData?.IsValid == true)
                {
                    newConfigData.IsValid = true;
                }

                FirmwareConfigData = newConfigData;

                _loadedXmlFilePath = string.Empty;
                ConfigItems.Clear();
                var items = ConfigParser.BuildConfigItemList(FirmwareConfigData);
                foreach (var item in items)
                {
                    ConfigItems.Add(item);
                }

                StatusMessage = $"配置已重新加载 (项目: {SelectedProjectType}, 配置项: {items.Count})";
            }
            catch (Exception ex)
            {
                FirmwareConfigData = oldConfigData;
                StatusMessage = $"配置重新加载失败: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private bool CanExecuteSaveConfig(object? parameter)
        {
            return IsConfigModified && FirmwareConfigData != null && !string.IsNullOrEmpty(_currentFilePath);
        }

        private void ExecuteSaveConfig(object? parameter)
        {
            if (FirmwareConfigData == null || string.IsNullOrEmpty(_currentFilePath))
                return;

            try
            {
                IsLoading = true;
                StatusMessage = "正在保存配置...";

                SyncConfigItemsToFlags();

                string backupPath = _currentFilePath + ".config_backup-" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                if (File.Exists(_currentFilePath))
                {
                    File.Copy(_currentFilePath, backupPath, true);
                }

                byte[]? firmwareDataWithResources = null;

                if (_destBinParser != null && _currentFileData != null)
                {
                    if (_destBinParser.ReplaceResBin(_currentFileData, keepSize: false))
                    {
                        firmwareDataWithResources = _destBinParser.GetDestBinData();
                        uint newConfigAddress = _destBinParser.CalculateConfigAddress();
                        FirmwareConfigData.ConfigAddress = newConfigAddress;
                    }
                    else
                    {
                        throw new InvalidOperationException($"应用资源修改失败: {_destBinParser.ErrorMessage}");
                    }

                    // Apply config AFTER resource replacement to prevent overwrite
                    if (IsConfigModified)
                    {
                        ApplyConfigChangesToDestBin();
                        firmwareDataWithResources = _destBinParser.GetDestBinData();
                    }
                }

                if (ConfigWriter.SaveConfigToDestBin(_currentFilePath, FirmwareConfigData, firmwareDataWithResources))
                {
                    IsConfigModified = false;
                    (SaveConfigCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    StatusMessage = "✅配置保存成功";

                    if (_destBinParser != null)
                    {
                        byte[] savedData = File.ReadAllBytes(_currentFilePath);
                        _destBinParser.UpdateDestBinData(savedData);
                        _currentFileData = _destBinParser.ExtractResBin();
                    }

                    if (!string.IsNullOrEmpty(_loadedXmlFilePath) && FirmwareConfigData.XmlParsedItems != null)
                    {
                        try
                        {
                            SyncConfigItemsToXmlParsed();

                            string xmlBackupPath = _loadedXmlFilePath + ".backup-" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                            if (File.Exists(_loadedXmlFilePath))
                            {
                                File.Copy(_loadedXmlFilePath, xmlBackupPath, true);
                            }

                            ConfigXmlParser.SaveXmlToFile(_loadedXmlFilePath, FirmwareConfigData.XmlParsedItems);
                            StatusMessage += " (XML 已同步)";
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[ExecuteSaveConfig] XML sync failed: {ex.Message}");
                            StatusMessage += $" (XML 同步失败: {ex.Message})";
                        }
                    }

                    MessageBox.Show($"配置已成功保存到固件文件中。\n\n备份文件: {Path.GetFileName(backupPath)}",
                                  "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    StatusMessage = "配置保存失败";
                    MessageBox.Show("配置保存失败。", "错误",
                                  MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = "配置保存异常";
                MessageBox.Show($"配置保存异常:\n{ex.Message}",
                              "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private bool CanExecuteResetConfig(object? parameter)
        {
            return FirmwareConfigData != null;
        }

        private void ExecuteResetConfig(object? parameter)
        {
            if (FirmwareConfigData == null)
                return;

            var template = ConfigTemplateManager.CurrentTemplate;
            var result = MessageBox.Show(
                $"确定要恢复所有配置为默认值吗？\n\n当前方案: {template.Name}\n此操作将所有配置项恢复为出厂默认设置。",
                "确认恢复默认配置",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                ConfigWriter.ResetToDefaults(FirmwareConfigData, _selectedConfigTemplate);

                _loadedXmlFilePath = string.Empty;
                ConfigItems.Clear();
                var items = ConfigParser.BuildConfigItemList(FirmwareConfigData);
                foreach (var item in items)
                {
                    ConfigItems.Add(item);
                }

                IsConfigModified = true;
                (SaveConfigCommand as RelayCommand)?.RaiseCanExecuteChanged();
                StatusMessage = $"✅ 已恢复默认配置 ({template.Name})";
            }
            catch (Exception ex)
            {
                StatusMessage = "恢复默认配置失败";
                MessageBox.Show($"恢复默认配置失败:\n{ex.Message}",
                              "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool CanExecuteExportConfig(object? parameter)
        {
            return FirmwareConfigData != null && ConfigItems.Count > 0;
        }

        private void ExecuteExportConfig(object? parameter)
        {
            if (FirmwareConfigData == null || ConfigItems.Count == 0)
                return;

            var dialog = new SaveFileDialog
            {
                FileName = "firmware_config.txt",
                Filter = "Text files|*.txt|All files|*.*",
                Title = "导出配置文件"
            };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                string configText = ConfigWriter.ExportConfigAsText(FirmwareConfigData, ConfigItems.ToList());
                File.WriteAllText(dialog.FileName, configText, Encoding.UTF8);
                StatusMessage = $"✅ 配置已导出到: {Path.GetFileName(dialog.FileName)}";
            }
            catch (Exception ex)
            {
                StatusMessage = "导出配置失败";
                MessageBox.Show($"导出配置失败:\n{ex.Message}",
                              "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #region Mapping Config Commands

        private void ExecuteLoadMappingConfig(object? parameter)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "JSON files|*.json|All files|*.*",
                Title = "加载映射配置文件"
            };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                var mapping = ProjectConfigMapping.LoadFromJsonFile(dialog.FileName);
                if (mapping != null)
                {
                    ProjectConfigMappingDatabase.AddOrUpdateMapping(mapping);
                    StatusMessage = $"✅ 已加载映射配置: {mapping.ProjectName}";

                    if (FirmwareConfigData != null && FirmwareConfigData.ProjectType == mapping.ProjectType)
                    {
                        if (CanExecuteLoadConfig(null))
                            LoadConfigCommand.Execute(null);
                    }
                }
                else
                {
                    StatusMessage = "❌ 加载映射配置失败";
                    MessageBox.Show("无法加载映射配置文件，请检查文件格式。",
                                  "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = "加载映射配置失败";
                MessageBox.Show($"加载映射配置失败:\n{ex.Message}",
                              "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool CanExecuteSaveMappingConfig(object? parameter)
        {
            return FirmwareConfigData != null;
        }

        private void ExecuteSaveMappingConfig(object? parameter)
        {
            if (FirmwareConfigData?.Mapping == null)
                return;

            var dialog = new SaveFileDialog
            {
                FileName = $"{FirmwareConfigData.ProjectType}_mapping.json",
                Filter = "JSON files|*.json|All files|*.*",
                Title = "保存映射配置文件"
            };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                if (FirmwareConfigData.Mapping.SaveToJsonFile(dialog.FileName))
                {
                    StatusMessage = $"✅ 映射配置已保存: {Path.GetFileName(dialog.FileName)}";
                }
                else
                {
                    StatusMessage = "❌ 保存映射配置失败";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = "保存映射配置失败";
                MessageBox.Show($"保存映射配置失败:\n{ex.Message}",
                              "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExecuteReloadAllMappings(object? parameter)
        {
            try
            {
                ProjectConfigMappingDatabase.ReloadMappings();
                StatusMessage = "✅ 已重新加载所有映射配置";

                if (FirmwareConfigData != null)
                {
                    if (CanExecuteLoadConfig(null))
                        LoadConfigCommand.Execute(null);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = "重新加载映射配置失败";
                MessageBox.Show($"重新加载映射配置失败:\n{ex.Message}",
                              "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool CanExecuteGenerateSampleMapping(object? parameter)
        {
            return FirmwareConfigData != null;
        }

        private void ExecuteGenerateSampleMapping(object? parameter)
        {
            if (FirmwareConfigData == null)
                return;

            var dialog = new SaveFileDialog
            {
                FileName = $"{FirmwareConfigData.ProjectType}_sample.json",
                Filter = "JSON files|*.json|All files|*.*",
                Title = "生成示例映射配置文件"
            };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                if (ProjectMappingConfigLoader.GenerateSampleConfig(FirmwareConfigData.ProjectType, dialog.FileName))
                {
                    StatusMessage = $"✅ 示例配置已生成: {Path.GetFileName(dialog.FileName)}";
                }
                else
                {
                    StatusMessage = "❌ 生成示例配置失败";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = "生成示例配置失败";
                MessageBox.Show($"生成示例配置失败:\n{ex.Message}",
                              "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool CanExecuteGenerateFromSource(object? parameter)
        {
            return !IsLoading;
        }

        private async void ExecuteGenerateFromSource(object? parameter)
        {
            if (IsLoading)
            {
                MessageBox.Show("正在处理中，请稍候...", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var folderDialog = new FolderBrowserDialog
            {
                Description = "选择包含 config.c、config.h 和菜单文件的项目(firmware)目录"
            };

            if (folderDialog.ShowDialog() != DialogResult.OK)
                return;

            string projectPath = folderDialog.SelectedPath;

            string? configCPath = ConfigSourceParser.FindConfigC(projectPath);
            if (string.IsNullOrEmpty(configCPath))
            {
                MessageBox.Show("未找到 config.c 文件", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string? configHPath = ConfigHParser.FindConfigH(projectPath);
            string? menuFilePath = MenuParser.FindMenuFile(projectPath);
            string? userStrHPath = UserStrParser.FindUserStrH(projectPath);
            string? customerHPath = FindCustomerH(projectPath);
            string? versionHPath = FindVersionH(projectPath);
            string? resHPath = FindResH(projectPath);

            var saveDialog = new SaveFileDialog
            {
                FileName = "Project_Config",
                Filter = "XML files|*.xml|JSON files|*.json",
                Title = "保存配置文件"
            };

            if (saveDialog.ShowDialog() != true)
                return;

            try
            {
                IsLoading = true;
                StatusMessage = "正在解析源码...";

                ConfigSourceParser.ParseResult parseResult = await Task.Run(() =>
                {
                    var parser = new ConfigSourceParser();
                    return parser.Parse(configCPath, configHPath, userStrHPath, customerHPath, versionHPath, resHPath);
                });

                if (!parseResult.Success)
                {
                    MessageBox.Show($"解析失败:\n{parseResult.ErrorMessage}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (parseResult.ConfigItems.Count == 0)
                {
                    MessageBox.Show("未从源码中提取到任何配置项，请检查源码格式", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                List<string> validationWarnings = new List<string>();

                var duplicateItems = parseResult.ConfigItems.GroupBy(x => x.Index).Where(g => g.Count() > 1).ToList();
                if (duplicateItems.Any())
                {
                    foreach (var group in duplicateItems)
                    {
                        validationWarnings.Add($"索引 {group.Key} 存在重复配置项: {string.Join(", ", group.Select(x => x.Name))}");
                    }
                }

                var outOfRangeItems = parseResult.ConfigItems.Where(x => x.Index < 0 || x.Index >= ConfigParser.SDK_CONFIG_ID_MAX).ToList();
                if (outOfRangeItems.Any())
                {
                    validationWarnings.Add($"以下配置项索引超出有效范围 [0, {ConfigParser.SDK_CONFIG_ID_MAX - 1}]: {string.Join(", ", outOfRangeItems.Select(x => $"{x.Name}(索引:{x.Index})"))}");
                }

                var unknownNameItems = parseResult.ConfigItems.Where(x => !Enum.IsDefined(typeof(ConfigId), x.Name)).ToList();
                if (unknownNameItems.Any())
                {
                    validationWarnings.Add($"以下配置项名称未在 ConfigId 枚举中定义: {string.Join(", ", unknownNameItems.Select(x => x.Name))}");
                }

                if (validationWarnings.Any())
                {
                    string warningMessage = "解析过程中发现以下问题:\n\n" + string.Join("\n", validationWarnings) + "\n\n是否继续生成配置文件?";
                    var result = MessageBox.Show(warningMessage, "验证警告", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (result == MessageBoxResult.No)
                        return;

                    parseResult.ConfigItems = parseResult.ConfigItems
                        .Where(x => x.Index >= 0 && x.Index < ConfigParser.SDK_CONFIG_ID_MAX)
                        .GroupBy(x => x.Index)
                        .Select(g => g.First())
                        .ToList();
                }

                MenuParser.ParseResult? menuResult = null;
                if (!string.IsNullOrEmpty(menuFilePath))
                {
                    menuResult = await Task.Run(() =>
                    {
                        var menuParser = new MenuParser();
                        return menuParser.Parse(menuFilePath);
                    });
                }

                string extension = Path.GetExtension(saveDialog.FileName).ToLower();
                if (extension == ".xml")
                {
                    //if (File.Exists(saveDialog.FileName))
                    //{
                    //    var overwriteResult = MessageBox.Show($"文件 {Path.GetFileName(saveDialog.FileName)} 已存在，是否覆盖?",
                    //        "文件已存在", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    //    if (overwriteResult == MessageBoxResult.No)
                    //        return;
                    //}

                    var xmlGenerator = new ConfigXmlGenerator();
                    if (xmlGenerator.Generate(parseResult, menuResult, saveDialog.FileName))
                    {
                        StatusMessage = $"✅ XML配置文件已生成: {Path.GetFileName(saveDialog.FileName)}";
                        string menuInfo = menuResult != null && menuResult.Success
                            ? $"\n\n同时解析了 {menuResult.MenuOptions.Count} 个菜单选项"
                            : "";
                        string resHInfo = parseResult.ResourceDefinitions.Count > 0
                            ? $"\n同时保存了 {parseResult.ResourceDefinitions.Count} 个 RES.H 资源定义"
                            : "";
                        MessageBox.Show($"成功生成XML配置文件:\n{saveDialog.FileName}\n\n共提取 {parseResult.ConfigItems.Count} 个配置项{menuInfo}{resHInfo}",
                                      "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        StatusMessage = "❌ 生成XML配置文件失败";
                        MessageBox.Show("生成XML配置文件失败，请检查日志", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    //if (File.Exists(saveDialog.FileName))
                    //{
                    //    var overwriteResult = MessageBox.Show($"文件 {Path.GetFileName(saveDialog.FileName)} 已存在，是否覆盖?",
                    //        "文件已存在", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    //    if (overwriteResult == MessageBoxResult.No)
                    //        return;
                    //}

                    var generator = new ConfigJsonGenerator();
                    if (generator.Generate(parseResult, saveDialog.FileName))
                    {
                        StatusMessage = $"✅ 配置映射已生成: {Path.GetFileName(saveDialog.FileName)}";
                        MessageBox.Show($"成功生成配置映射文件:\n{saveDialog.FileName}\n\n共提取 {parseResult.ConfigItems.Count} 个配置项",
                                      "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        StatusMessage = "❌ 生成配置映射失败";
                        MessageBox.Show("生成配置映射文件失败，请检查日志", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            catch (FileNotFoundException ex)
            {
                StatusMessage = "文件不存在";
                MessageBox.Show($"文件不存在:\n{ex.FileName}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (IOException ex)
            {
                StatusMessage = "文件操作失败";
                MessageBox.Show($"文件操作失败:\n{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (UnauthorizedAccessException ex)
            {
                StatusMessage = "权限不足";
                MessageBox.Show($"权限不足:\n{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                StatusMessage = "生成配置文件失败";
                MessageBox.Show($"生成配置文件失败:\n{ex.GetType().Name}: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        #endregion

        #region Config Item Update

        public void UpdateConfigItemValue(FirmwareConfigItem item, uint newValue)
        {
            if (FirmwareConfigData == null)
                return;

            if (ConfigWriter.UpdateConfigValue(FirmwareConfigData, item.Id, newValue))
            {
                item.Value = newValue;
                item.ValueDisplay = GetConfigValueDisplay(item.Id, newValue);
                IsConfigModified = true;
                (SaveConfigCommand as RelayCommand)?.RaiseCanExecuteChanged();
                StatusMessage = $"已修改 {item.Name} = {item.ValueDisplay}";
            }
        }

        private string GetConfigValueDisplay(ConfigId configId, uint value)
        {
            return configId switch
            {
                ConfigId.CONFIG_ID_LANGUAGE => ConfigParser_BuildConfigItemList_GetLanguageDisplay(value),
                ConfigId.CONFIG_ID_VIDEO_RESOLUTION => ConfigParser_BuildConfigItemList_GetResolutionDisplay(value),
                ConfigId.CONFIG_ID_NETWORK_SPEED => ConfigParser_BuildConfigItemList_GetNetworkSpeedDisplay(value),
                _ => $"0x{value:X8}"
            };
        }

        private string ConfigParser_BuildConfigItemList_GetLanguageDisplay(uint value)
        {
            return value switch
            {
                0 => "中文",
                1 => "English",
                2 => "日本語",
                3 => "한국어",
                _ => $"未知({value})"
            };
        }

        private string ConfigParser_BuildConfigItemList_GetResolutionDisplay(uint value)
        {
            return value switch
            {
                0 => "1080P",
                1 => "720P",
                2 => "4K",
                _ => $"未知({value})"
            };
        }

        private string ConfigParser_BuildConfigItemList_GetNetworkSpeedDisplay(uint value)
        {
            return value switch
            {
                0 => "100Mbps",
                1 => "10Mbps",
                _ => $"未知({value})"
            };
        }

        #endregion
    }
}
