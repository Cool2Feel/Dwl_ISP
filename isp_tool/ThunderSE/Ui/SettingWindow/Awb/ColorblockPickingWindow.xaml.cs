using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ThunderSE.DeviceConfig.Isp;
using ThunderSE.Model;
using ThunderSE.Ui.CommonCustomControl;

namespace ThunderSE.Ui.SettingWindow.Awb
{
    /// <summary>
    /// ColorblockPickingWindow - AWB色块选择窗口
    /// 用于在多张RAW图像上选择色块区域，计算AWB白平衡增益值
    /// </summary>
    /// 
    public partial class ColorblockPickingWindow : Window
    {
        private string[] _imgPaths;
        private Dictionary<string, byte[]> _rawImageBufferList = new Dictionary<string, byte[]>();
        //private Dictionary<string, List<RubberBandData>> _rubberBandDataList = new Dictionary<string, List<RubberBandData>>();
        private Dictionary<string, ObservableCollection<RubberBandData>> _rubberBandDataList = new Dictionary<string, ObservableCollection<RubberBandData>>();  // 修改这一行

        private Processor _ispProcessor = null;

        public ColorblockPickingWindow(Processor ispProcessor, string[] rawImgs)
        {
            _ispProcessor = ispProcessor;
            _imgPaths = rawImgs;
            InitializeComponent();
            
            SetupWindowEvents();
            SetupKeyboardShortcuts();
            SetupCategorySelector(); // 添加类别选择器设置
        }

        #region 窗口初始化与事件设置

        private void SetupWindowEvents()
        {
            this.SizeChanged += OnWindowSizeChanged;
            this.Closing += OnWindowClosing;
        }

        private void SetupKeyboardShortcuts()
        {
            // Ctrl+Left: 上一张图片
            InputBindings.Add(new KeyBinding(new RelayCommand(() => BeforePicButton_Click(null, null)), 
                Key.Left, ModifierKeys.Control));
            
            // Ctrl+Right: 下一张图片
            InputBindings.Add(new KeyBinding(new RelayCommand(() => NextPicButton_Click(null, null)), 
                Key.Right, ModifierKeys.Control));
            
            // Ctrl+Z: 撤销选区
            InputBindings.Add(new KeyBinding(new RelayCommand(() => UndoButton_Click(null, null)), 
                Key.Z, ModifierKeys.Control));
            
            // Ctrl+Enter: 确认计算
            InputBindings.Add(new KeyBinding(new RelayCommand(() => OkButton_Click(null, null)), 
                Key.Enter, ModifierKeys.Control));
            
            // Esc: 取消关闭
            InputBindings.Add(new KeyBinding(new RelayCommand(() => CancelButton_Click(null, null)), 
                Key.Escape, ModifierKeys.Control));
        }

        // 设置类别选择器
        private void SetupCategorySelector()
        {
            if (CategoryCombo != null)
            {
                CategoryCombo.SelectionChanged += (sender, e) =>
                {
                    if (RawImgsTab.SelectedItem != null)
                    {
                        var currentTabItem = (TabItem)RawImgsTab.SelectedItem;
                        var imgControl = (ImageWithRubberBandControl)currentTabItem.Content;

                        var selectedCategory = CategoryCombo.SelectedItem as ComboBoxItem;
                        if (selectedCategory != null)
                        {
                            imgControl.CurrentCategory = selectedCategory.Content.ToString();
                        }
                    }
                };
            }
        }

        private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateStatus($"窗口尺寸已调整: {this.ActualWidth:F0}×{this.ActualHeight:F0}");
        }

        private void OnWindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            //int totalSelections = GetTotalSelectionCount();
            //if (totalSelections > 0)
            //{
            //    var result = MessageBox.Show(this,
            //        $"当前已选择 {totalSelections} 个色块区域，确定要取消操作吗？\n\n未保存的选择数据将丢失。",
            //        "确认关闭",
            //        MessageBoxButton.YesNo,
            //        MessageBoxImage.Question);

            //    if (result == MessageBoxResult.No)
            //    {
            //        e.Cancel = true;
            //        UpdateStatus("已取消关闭操作");
            //        return;
            //    }
            //}

            UpdateStatus("正在关闭色块选择窗口...");
        }

        #endregion

        #region UI状态更新方法

        private void UpdateStatus(string message)
        {
            if (StatusBarText != null)
            {
                StatusBarText.Text = message;
            }
        }

        private void UpdateImageCountDisplay(int totalCount)
        {
            if (TxtImageCount != null)
            {
                TxtImageCount.Text = $"(共 {totalCount} 张图像)";
            }
        }

        private void UpdateCurrentImageDisplay(string imageName, int currentIndex, int totalCount)
        {
            if (TxtCurrentImage != null)
            {
                TxtCurrentImage.Text = $"当前图像: {imageName} ({currentIndex + 1}/{totalCount})";
            }
        }

        /// <summary>
        /// 获取指定类别的最大选区数
        /// </summary>
        /// <param name="category">类别名称</param>
        /// <returns>该类别允许的最大选区数</returns>
        private int GetMaxBandsForCategory(string category)
        {
            switch (category)
            {
                case "白点":
                    return 6;    // 白点类别最多6个选区
                case "非白点":
                    return 24;   // 非白点类别最多24个选区
                default:
                    return 6;    // 默认6个选区
            }
        }

        private void UpdateSelectionCountDisplay(int currentCount, int maxCount = 24)
        {
            if (TxtSelectionCount != null)
            {
                // 获取当前类别的选区数量
                int currentCategoryCount = 0;
                string currentCategory = "白点";
                int categoryMaxCount = 6; // 默认最大值

                if (CategoryCombo?.SelectedItem is ComboBoxItem selectedCategory)
                {
                    currentCategory = selectedCategory.Content.ToString();
                    // 根据类别设置不同的最大选区数
                    categoryMaxCount = GetMaxBandsForCategory(currentCategory);
                }

                if (RawImgsTab.SelectedItem != null)
                {
                    var currentTabItem = (TabItem)RawImgsTab.SelectedItem;
                    var imgControl = (ImageWithRubberBandControl)currentTabItem.Content;

                    if (imgControl.DataContext is ObservableCollection<RubberBandData> rubberBandData)
                    {
                        currentCategoryCount = rubberBandData.Count(d => d.Category == currentCategory);
                    }
                }

                TxtSelectionCount.Text = $"已选色块: {currentCount}/{maxCount}";
                
                // 根据选择数量改变颜色提示
                if (currentCategoryCount >= maxCount)
                {
                    TxtSelectionCount.Foreground = System.Windows.Media.Brushes.Green;
                }
                else if (currentCategoryCount > 0)
                {
                    TxtSelectionCount.Foreground = System.Windows.Media.Brushes.Blue;     // 进行中
                }
                else
                {
                    TxtSelectionCount.Foreground = System.Windows.Media.Brushes.Gray;
                }
            }
        }

        private void UpdateTotalStatsDisplay()
        {
            //if (TxtTotalStats != null)
            //{
            //    int totalImages = _rawImageBufferList.Count;
            //    int totalSelections = GetTotalSelectionCount();
            //    TxtTotalStats.Text = $"总图像数: {totalImages} | 总选区数: {totalSelections}";
            //}
            if (TxtTotalStats != null)
            {
                int totalImages = _rawImageBufferList.Count;
                int totalSelections = GetTotalSelectionCount();

                // 统计每个类别的总数
                var categoryCounts = new Dictionary<string, int>();
                foreach (var item in _rubberBandDataList)
                {
                    foreach (var data in item.Value)
                    {
                        if (!string.IsNullOrEmpty(data.Category))
                        {
                            if (!categoryCounts.ContainsKey(data.Category))
                                categoryCounts[data.Category] = 0;
                            categoryCounts[data.Category]++;
                        }
                    }
                }

                string categoryInfo = string.Join(" | ", categoryCounts.Select(kv => $"{kv.Key}:{kv.Value}"));
                TxtTotalStats.Text = $"总图像数: {totalImages} | 总选区数: {totalSelections}";

                // 如果有类别信息，添加到状态栏
                if (!string.IsNullOrEmpty(categoryInfo))
                {
                    UpdateStatus($"各类别选区统计: {categoryInfo}");
                }
            }
        }

        private void UpdateProgressInfo(string info)
        {
            if (TxtProgressInfo != null)
            {
                TxtProgressInfo.Text = info;
            }
        }

        private int GetTotalSelectionCount()
        {
            int total = 0;
            foreach (var item in _rubberBandDataList)
            {
                total += item.Value.Count;
            }
            return total;
        }

        private void RefreshCurrentTabSelectionInfo()
        {
            if (RawImgsTab.SelectedItem != null)
            {
                var currentTabItem = (TabItem)RawImgsTab.SelectedItem;
                var imgControl = (ImageWithRubberBandControl)currentTabItem.Content;

                if (imgControl.DataContext is ObservableCollection<RubberBandData> rubberBandData)
                {
                    UpdateSelectionCountDisplay(rubberBandData.Count);
                }

                // 同时更新当前图像显示信息
                string imageName;
                if (currentTabItem.Header is TextBlock textBlock)
                {
                    imageName = textBlock.Text;
                }
                else
                {
                    imageName = currentTabItem.Header.ToString();
                }
                int currentIndex = RawImgsTab.SelectedIndex;
                int totalCount = RawImgsTab.Items.Count;

                UpdateCurrentImageDisplay(imageName, currentIndex, totalCount);
            }
        }

        #endregion

        #region 图像加载与显示

        private void AddImages(string rawImgPath)
        {
            if(!File.Exists(rawImgPath))
            {
                return;
            }
            
            string fileName = System.IO.Path.GetFileName(rawImgPath);
            string tabName = System.IO.Path.GetFileNameWithoutExtension(rawImgPath);
            
            TabItem tabItem = new TabItem();
            TextBlock tabItemHeaderText = new TextBlock();
            tabItemHeaderText.Width = 100;  // 增加宽度以适应更长的文件名
            tabItemHeaderText.TextTrimming = TextTrimming.CharacterEllipsis;
            tabItemHeaderText.ToolTip = $"文件: {fileName}\n路径: {rawImgPath}";
            tabItemHeaderText.Text = tabName;
            tabItemHeaderText.FontWeight = FontWeights.SemiBold;
            
            tabItem.Header = tabItemHeaderText;
            
            try
            {
                byte[] rawImageBuffer = File.ReadAllBytes(rawImgPath);
                _rawImageBufferList.Add(Path.GetFileNameWithoutExtension(fileName), rawImageBuffer);

                UpdateProgressInfo($"正在加载: {fileName}...");

                var imgControl = new ImageWithRubberBandControl();
                imgControl.MaxBands = 6;
                // 设置不同类别的最大选区数
                imgControl.SetMaxBandsForCategory("白点", 6);     // 白点最多6个选区
                imgControl.SetMaxBandsForCategory("非白点", 24);  // 非白点最多24个选区

                imgControl.DisplayImageSource = _ispProcessor.GenerateBitmapUsingRaw(rawImageBuffer, IspModule.Awb, false);

                //var rubberBandData = new List<RubberBandData>();
                //_rubberBandDataList.Add(Path.GetFileNameWithoutExtension(fileName), rubberBandData);
                var rubberBandData = new ObservableCollection<RubberBandData>(); // 使用ObservableCollection替代List
                _rubberBandDataList.Add(Path.GetFileNameWithoutExtension(fileName), rubberBandData);

                // 添加集合变更监听器，以实时更新选区计数
                rubberBandData.CollectionChanged += (sender, e) =>
                {
                    RefreshCurrentTabSelectionInfo();
                    UpdateTotalStatsDisplay();  // 添加这一行以更新总统计数据
                };

                imgControl.DataContext = rubberBandData;
                
                tabItem.Content = imgControl;
                
                RawImgsTab.Items.Add(tabItem);
                RawImgsTab.SelectedIndex = 0;

                UpdateProgressInfo($"✓ 已加载: {fileName}");
                UpdateTotalStatsDisplay();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    $"加载图像失败:\n{ex.Message}\n\n文件: {rawImgPath}",
                    "加载错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                UpdateProgressInfo($"✗ 加载失败: {fileName}");
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            InitializeUI();
            
            UpdateStatus("开始加载RAW图像...");
            UpdateProgressInfo($"准备加载 {_imgPaths.Length} 张图像...");

            /*
            int loadedCount = 0;
            foreach (var rawImgPath in _imgPaths)
	        {
                AddImages(rawImgPath);
                loadedCount++;
                UpdateImageCountDisplay(loadedCount);
            }

            UpdateCurrentImageDisplay(
                RawImgsTab.SelectedIndex >= 0 ? 
                    ((TabItem)RawImgsTab.SelectedItem).Header.ToString() : "--",
                RawImgsTab.SelectedIndex,
                RawImgsTab.Items.Count);
            
            RefreshCurrentTabSelectionInfo();
            
            UpdateStatus($"✓ 图像加载完成 - 共 {loadedCount} 张图像就绪");
            UpdateProgressInfo($"所有图像已加载完成 | 请框选色块区域");

            // 订阅TabControl选择变化事件以更新UI
            RawImgsTab.SelectionChanged += OnRawImgsTab_SelectionChanged;

            // 当选项卡切换时更新当前控件的类别
            RawImgsTab.SelectionChanged += (s, r) =>
            {
                if (CategoryCombo != null && RawImgsTab.SelectedItem != null)
                {
                    var currentTabItem = (TabItem)RawImgsTab.SelectedItem;
                    var imgControl = (ImageWithRubberBandControl)currentTabItem.Content;

                    var selectedCategory = CategoryCombo.SelectedItem as ComboBoxItem;
                    if (selectedCategory != null)
                    {
                        string categoryName = selectedCategory.Content.ToString();
                        imgControl.CurrentCategory = categoryName;

                        // 根据类别动态调整最大选区数提示
                        int maxBands = GetMaxBandsForCategory(categoryName);
                        UpdateSelectionCountDisplay(0, maxBands);
                    }
                }
            };

            */

            // 使用异步方法加载图像，避免阻塞UI
            _ = LoadImagesAsync();
        }

        private void InitializeUI()
        {
            UpdateTotalStatsDisplay();
            UpdateStatus("AWB色块选择工具初始化完成");
        }

        private void OnRawImgsTab_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (RawImgsTab.SelectedItem != null)
            {
                var selectedTab = (TabItem)RawImgsTab.SelectedItem;
                string imageName;
                // 检查Header是否为TextBlock类型，并获取其Text属性
                if (selectedTab.Header is TextBlock textBlock)
                {
                    imageName = textBlock.Text;
                }
                else
                {
                    imageName = selectedTab.Header.ToString();
                }
                int currentIndex = RawImgsTab.SelectedIndex;
                int totalCount = RawImgsTab.Items.Count;

                UpdateCurrentImageDisplay(imageName, currentIndex, totalCount);
                RefreshCurrentTabSelectionInfo();

                //UpdateStatus($"已切换到图像: {imageName} ({currentIndex + 1}/{totalCount})");
                // 显示当前图片的各类别统计
                if (selectedTab.Content is ImageWithRubberBandControl imgControl &&
                    imgControl.DataContext is ObservableCollection<RubberBandData> rubberBandData)
                {
                    var categoryStats = rubberBandData.GroupBy(d => d.Category)
                        .ToDictionary(g => g.Key, g => g.Count());

                    string statsText = string.Join(", ", categoryStats.Select(kv => $"{kv.Key}:{kv.Value}"));
                    UpdateStatus($"已切换到图像: {imageName} ({currentIndex + 1}/{totalCount}) | 各类别: {statsText}");
                }
                else
                {
                    UpdateStatus($"已切换到图像: {imageName} ({currentIndex + 1}/{totalCount})");
                }
            }
        }

        /// <summary>
        /// 异步并行加载所有图像（优化版本）
        /// </summary>
        private async Task LoadImagesAsync()
        {
            int totalImages = _imgPaths.Length;
            int loadedCount = 0;
            var loadTasks = new List<Task>();

            // 显示加载进度窗口
            //var progressWindow = new LoadingProgressWindow(totalImages);
            //progressWindow.Show();

            try
            {
                // 第一阶段：并行读取文件到内存（I/O密集型）
                UpdateStatus("正在读取图像文件...");

                var fileReadTasks = _imgPaths.Select(async (path, index) =>
                {
                    string fileName = Path.GetFileNameWithoutExtension(path);

                    try
                    {
                        // 异步读取文件
                        byte[] buffer = await Task.Run(() => File.ReadAllBytes(path));

                        // 存储到字典（需要加锁保证线程安全）
                        lock (_rawImageBufferList)
                        {
                            _rawImageBufferList[fileName] = buffer;
                        }

                        return new ImageLoadResult(index, fileName, buffer, true);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"读取文件失败: {path}, 错误: {ex.Message}"); 
                        return new ImageLoadResult(index, fileName, null, false);
                    }
                });

                var fileResults = await Task.WhenAll(fileReadTasks);
                int successCount = fileResults.Count(r => r.Success);

                UpdateProgressInfo($"✓ 文件读取完成: {successCount}/{totalImages}");
                //progressWindow.UpdateProgress(successCount, "正在生成预览图...");

                // 第二阶段：串行创建UI控件并生成Bitmap（UI线程操作）
                UpdateStatus("正在生成图像预览...");

                for (int i = 0; i < fileResults.Length; i++)
                {
                    var result = fileResults[i];

                    if (!result.Success || result.Buffer == null)
                    {
                        continue;
                    }

                    // 在UI线程创建控件和生成Bitmap
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            AddImagesFast(result.FileName, result.Buffer);
                            loadedCount++;

                            // 更新进度
                            UpdateImageCountDisplay(loadedCount);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"创建图像控件失败: {result.FileName}, 错误: {ex.Message}");
                        }
                    }, System.Windows.Threading.DispatcherPriority.Background);
                }

                // 第三阶段：初始化完成
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    UpdateCurrentImageDisplay(
                        RawImgsTab.SelectedIndex >= 0 ?
                            ((TabItem)RawImgsTab.SelectedItem).Header.ToString() : "--",
                        RawImgsTab.SelectedIndex,
                        RawImgsTab.Items.Count);

                    RefreshCurrentTabSelectionInfo();

                    UpdateStatus($"✓ 图像加载完成 - 共 {loadedCount} 张图像就绪");
                    UpdateProgressInfo($"所有图像已加载完成 | 请框选色块区域");

                    // 订阅TabControl选择变化事件
                    RawImgsTab.SelectionChanged += OnRawImgsTab_SelectionChanged;

                    // 当选项卡切换时更新当前控件的类别
                    RawImgsTab.SelectionChanged += (s, r) =>
                    {
                        if (CategoryCombo != null && RawImgsTab.SelectedItem != null)
                        {
                            var currentTabItem = (TabItem)RawImgsTab.SelectedItem;
                            var imgControl = (ImageWithRubberBandControl)currentTabItem.Content;

                            var selectedCategory = CategoryCombo.SelectedItem as ComboBoxItem;
                            if (selectedCategory != null)
                            {
                                imgControl.CurrentCategory = selectedCategory.Content.ToString();
                            }
                        }
                    };
                });

            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    $"加载图像过程中发生错误:\n{ex.Message}",
                    "加载错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                UpdateStatus("❌ 图像加载失败");
                UpdateProgressInfo($"错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 快速添加图像（假设数据已在内存中）
        /// </summary>
        private void AddImagesFast(string imageName, byte[] rawImageBuffer)
        {
            if (rawImageBuffer == null || string.IsNullOrEmpty(imageName))
            {
                return;
            }

            TabItem tabItem = new TabItem();
            TextBlock tabItemHeaderText = new TextBlock();
            tabItemHeaderText.Width = 100;
            tabItemHeaderText.TextTrimming = TextTrimming.CharacterEllipsis;
            tabItemHeaderText.ToolTip = $"文件: {imageName}.raw";
            tabItemHeaderText.Text = imageName;
            tabItemHeaderText.FontWeight = FontWeights.SemiBold;

            tabItem.Header = tabItemHeaderText;

            try
            {
                UpdateProgressInfo($"正在生成预览: {imageName}...");

                var imgControl = new ImageWithRubberBandControl();

                // 设置不同类别的最大选区数
                imgControl.SetMaxBandsForCategory("白点", 6);     // 白点最多6个选区
                imgControl.SetMaxBandsForCategory("非白点", 24);  // 非白点最多24个选区

                // 生成Bitmap（这是最耗时的操作）
                imgControl.DisplayImageSource = _ispProcessor.GenerateBitmapUsingRaw(rawImageBuffer, IspModule.Awb, false);

                var rubberBandData = new ObservableCollection<RubberBandData>();
                _rubberBandDataList[imageName] = rubberBandData;

                // 添加集合变更监听器
                rubberBandData.CollectionChanged += (sender, e) =>
                {
                    RefreshCurrentTabSelectionInfo();
                    UpdateTotalStatsDisplay();
                };

                imgControl.DataContext = rubberBandData;

                tabItem.Content = imgControl;

                RawImgsTab.Items.Add(tabItem);

                // 只在第一张图片时设置为选中
                if (RawImgsTab.Items.Count == 1)
                {
                    RawImgsTab.SelectedIndex = 0;
                }

                UpdateProgressInfo($"✓ 已加载: {imageName}");
                UpdateTotalStatsDisplay();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载图像失败: {imageName}, 错误: {ex.Message}");
                UpdateProgressInfo($"✗ 加载失败: {imageName}");
            }
        }

        #endregion

        #region 操作按钮事件处理

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            int totalSelections = GetTotalSelectionCount();
            
            if (totalSelections == 0)
            {
                var result = MessageBox.Show(this,
                    "尚未在任何图像上选择色块区域。\n\n是否直接关闭窗口？（不会计算AWB增益值）",
                    "无选区确认",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    DialogResult = false;
                    Close();
                }
                return;
            }

            UpdateStatus("正在计算AWB增益值...");

            /*
            try
            {
                // 获取所有唯一的类别
                var allCategories = new HashSet<string>();
                foreach (var item in _rubberBandDataList)
                {
                    foreach (var data in item.Value)
                    {
                        if (!string.IsNullOrEmpty(data.Category))
                        {
                            allCategories.Add(data.Category);
                        }
                    }
                }

                if (allCategories.Count == 0)
                {
                    // 如果没有设置类别，使用默认方式计算
                    CalculateAWBByDefaultMode();
                    return;
                }

                // 按类别分别计算AWB增益
                var correctionData = (Dictionary<string, KeyValuePair<int, int>>)DataContext;
                correctionData.Clear(); // 清空之前的数据

                int totalProcessedItems = 0;
                var categoryResults = new Dictionary<string, List<string>>(); // 记录每个类别的处理结果

                foreach (var category in allCategories.OrderBy(c => c)) // 按类别名称排序
                {
                    UpdateProgressInfo($"正在处理类别: {category}...");
                    categoryResults[category] = new List<string>();

                    int categoryProcessedCount = 0;

                    // 判断是否为白点类别
                    bool isWhitePointCategory = (category == "白点");

                    foreach (var item in _rawImageBufferList)
                    {
                        var dataItem = _rubberBandDataList[item.Key];

                        // 过滤出当前类别的选区
                        var categoryDataItems = dataItem.Where(d => d.Category == category).ToList();

                        if (categoryDataItems.Count > 0)
                        {
                            if (isWhitePointCategory)
                            {
                                // 准备数组，最多取6个选区
                                int[] XArray = new int[6];
                                int[] YArray = new int[6];
                                int[] HeightArray = new int[6];
                                int[] WidthArray = new int[6];

                                for (int j = 0; j < Math.Min(categoryDataItems.Count, 6); j++)
                                {
                                    XArray[j] = categoryDataItems[j].x;
                                    YArray[j] = categoryDataItems[j].y;
                                    HeightArray[j] = categoryDataItems[j].height;
                                    WidthArray[j] = categoryDataItems[j].width;
                                }

                                int bgain = 0;
                                int rgain = 0;

                                byte[] tmpBuffer = (byte[])item.Value.Clone();

                                // 应用 BLC 黑电平校正（如果启用）
                                if (_ispProcessor.IspCommonConfig.ProcessorStepsEnables.Any(s => s.Key == IspModule.Blc && s.Value))
                                {
                                    _ispProcessor.AllProcessSteps[IspModule.Blc].ProcessRawBuffer(ref tmpBuffer);
                                }

                                // 应用 LSC 镜头阴影校正（如果启用）
                                if (_ispProcessor.IspCommonConfig.ProcessorStepsEnables.Any(s => s.Key == IspModule.Lsc && s.Value))
                                {
                                    _ispProcessor.AllProcessSteps[IspModule.Lsc].ProcessRawBuffer(ref tmpBuffer);
                                }

                                // 调用AWB计算API
                                IspApi.AWBCal(tmpBuffer,
                                    _ispProcessor.IspCommonConfig.ResolutionWidth,
                                    _ispProcessor.IspCommonConfig.ResolutionHeight,
                                    (int)_ispProcessor.IspCommonConfig.Bayer,
                                    XArray, YArray, WidthArray, HeightArray,
                                    ref bgain, ref rgain);

                                // 存储结果，键名为 "图像名_类别名"
                                string resultKey = $"{item.Key}_{category}";
                                correctionData[resultKey] = new KeyValuePair<int, int>(rgain, bgain / 4);

                                categoryProcessedCount++;
                                totalProcessedItems++;
                                categoryResults[category].Add($"{resultKey}: R={rgain}, B={bgain / 4}");

                                UpdateProgressInfo($"✓ {category} - {item.Key}: RGain={rgain}, BGain={bgain / 4} (合并{categoryDataItems.Count}个选区)");
                            }
                            else
                            {
                                // 非白点类别：每个选区独立计算为一个增益值
                                for (int selectionIndex = 0; selectionIndex < categoryDataItems.Count; selectionIndex++)
                                {
                                    var currentSelection = categoryDataItems[selectionIndex];

                                    // 准备数组，只传入当前选区
                                    int[] XArray = new int[6];
                                    int[] YArray = new int[6];
                                    int[] HeightArray = new int[6];
                                    int[] WidthArray = new int[6];

                                    XArray[0] = currentSelection.x;
                                    YArray[0] = currentSelection.y;
                                    HeightArray[0] = currentSelection.height;
                                    WidthArray[0] = currentSelection.width;

                                    int bgain = 0;
                                    int rgain = 0;

                                    byte[] tmpBuffer = (byte[])item.Value.Clone();

                                    // 应用 BLC 黑电平校正（如果启用）
                                    if (_ispProcessor.IspCommonConfig.ProcessorStepsEnables.Any(s => s.Key == IspModule.Blc && s.Value))
                                    {
                                        _ispProcessor.AllProcessSteps[IspModule.Blc].ProcessRawBuffer(ref tmpBuffer);
                                    }

                                    // 应用 LSC 镜头阴影校正（如果启用）
                                    if (_ispProcessor.IspCommonConfig.ProcessorStepsEnables.Any(s => s.Key == IspModule.Lsc && s.Value))
                                    {
                                        _ispProcessor.AllProcessSteps[IspModule.Lsc].ProcessRawBuffer(ref tmpBuffer);
                                    }

                                    // 调用AWB计算API（只传入当前单个选区）
                                    IspApi.AWBCal(tmpBuffer,
                                        _ispProcessor.IspCommonConfig.ResolutionWidth,
                                        _ispProcessor.IspCommonConfig.ResolutionHeight,
                                        (int)_ispProcessor.IspCommonConfig.Bayer,
                                        XArray, YArray, WidthArray, HeightArray,
                                        ref bgain, ref rgain);

                                    // 存储结果，键名为 "图像名_类别名_选区索引"
                                    string resultKey = $"{item.Key}_{category}_#{selectionIndex + 1}";
                                    correctionData[resultKey] = new KeyValuePair<int, int>(rgain, bgain / 4);

                                    categoryProcessedCount++;
                                    totalProcessedItems++;
                                    categoryResults[category].Add($"{resultKey}: R={rgain}, B={bgain / 4}");

                                    UpdateProgressInfo($"✓ {category} - {item.Key}[#{selectionIndex + 1}]: RGain={rgain}, BGain={bgain / 4}");
                                }
                            }
                        }
                        else
                        {
                            // 如果该图像没有此类别的选区，可以选择不存储或存储默认值
                            // 这里选择不存储，只在有数据的图像上计算
                        }
                    }

                    UpdateStatus($"✓ 类别 '{category}' 计算完成！处理了 {categoryProcessedCount} 个项目");
                }

                // 显示详细的计算结果
                StringBuilder resultSummary = new StringBuilder();
                resultSummary.AppendLine("AWB增益计算完成！");
                resultSummary.AppendLine($"总处理项目数: {totalProcessedItems}");
                resultSummary.AppendLine();

                foreach (var category in categoryResults.Keys.OrderBy(k => k))
                {
                    bool isWhitePoint = (category == "白点");
                    string modeDesc = isWhitePoint ? "（每图合并计算）" : "（每选区独立计算）";

                    resultSummary.AppendLine($"【{category}】{modeDesc} 共 {categoryResults[category].Count} 个结果:");
                    foreach (var result in categoryResults[category])
                    {
                        resultSummary.AppendLine($"  • {result}");
                    }
                    resultSummary.AppendLine();
                }

                MessageBox.Show(this,
                    resultSummary.ToString(),
                    "计算完成",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                UpdateStatus($"✓ AWB增益计算全部完成！共处理 {totalProcessedItems} 个项目");
                UpdateProgressInfo($"计算结果已返回 | 共 {GetTotalSelectionCount()} 个选区参与计算");
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    $"计算过程中发生错误:\n{ex.Message}\n\n请检查图像数据和选区是否有效。",
                    "计算错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                
                UpdateStatus("❌ AWB增益计算失败");
                UpdateProgressInfo($"错误: {ex.Message}");
            }
            */


            // 使用异步方法执行并行计算
            _ = CalculateAWBAsync();
        }


        /// <summary>
        /// 异步并行计算AWB增益值
        /// </summary>
        private async Task CalculateAWBAsync()
        {
            try
            {
                // 获取所有唯一的类别
                var allCategories = new HashSet<string>();
                foreach (var item in _rubberBandDataList)
                {
                    foreach (var data in item.Value)
                    {
                        if (!string.IsNullOrEmpty(data.Category))
                        {
                            allCategories.Add(data.Category);
                        }
                    }
                }

                if (allCategories.Count == 0)
                {
                    // 如果没有设置类别，使用默认方式计算
                    await CalculateAWBByDefaultModeAsync();
                    return;
                }

                // 按类别分别计算AWB增益
                var correctionData = (Dictionary<string, KeyValuePair<int, int>>)DataContext;
                correctionData.Clear(); // 清空之前的数据

                int totalProcessedItems = 0;
                var categoryResults = new Dictionary<string, List<string>>();
                var processedCountLock = new object(); // 用于线程安全的计数器

                // 为每个类别创建并行任务
                var tasks = allCategories.OrderBy(c => c).Select(async category =>
                {
                    UpdateProgressInfo($"正在处理类别: {category}...");

                    var localResults = new List<string>();
                    int localProcessedCount = 0;

                    // 判断是否为白点类别
                    bool isWhitePointCategory = (category == "白点");

                    // 准备该类别需要处理的图像列表
                    // 准备该类别需要处理的图像列表
                    var imagesToProcess = new List<ImageProcessData>();

                    foreach (var item in _rawImageBufferList)
                    {
                        var dataItem = _rubberBandDataList[item.Key];
                        var categoryDataItems = dataItem.Where(d => d.Category == category).ToList();

                        if (categoryDataItems.Count > 0)
                        {
                            imagesToProcess.Add(new ImageProcessData(item.Key, item.Value, categoryDataItems));
                        }
                    }

                    // 并行处理该类别的所有图像
                    var imageTasks = imagesToProcess.Select(async imageInfo =>
                    {
                        if (isWhitePointCategory)
                        {
                            // 白点类别：一张图像的所有选区合并计算为一个增益值
                            int[] XArray = new int[6];
                            int[] YArray = new int[6];
                            int[] HeightArray = new int[6];
                            int[] WidthArray = new int[6];

                            for (int j = 0; j < Math.Min(imageInfo.Selections.Count, 6); j++)
                            {
                                XArray[j] = imageInfo.Selections[j].x;
                                YArray[j] = imageInfo.Selections[j].y;
                                HeightArray[j] = imageInfo.Selections[j].height;
                                WidthArray[j] = imageInfo.Selections[j].width;
                            }

                            int bgain = 0;
                            int rgain = 0;

                            // 在后台线程执行CPU密集型计算
                            await Task.Run(() =>
                            {
                                // 1. 复制原始 buffer（避免修改原始数据）
                                byte[] processBuffer = (byte[])imageInfo.Buffer.Clone();

                                // 2. 应用 BLC 黑电平校正（如果启用）
                                if (_ispProcessor.IspCommonConfig.ProcessorStepsEnables.Any(s => s.Key == IspModule.Blc && s.Value))
                                {
                                    _ispProcessor.AllProcessSteps[IspModule.Blc].ProcessRawBuffer(ref processBuffer);
                                }

                                // 3. 应用 LSC 镜头阴影校正（如果启用）
                                if (_ispProcessor.IspCommonConfig.ProcessorStepsEnables.Any(s => s.Key == IspModule.Lsc && s.Value))
                                {
                                    _ispProcessor.AllProcessSteps[IspModule.Lsc].ProcessRawBuffer(ref processBuffer);
                                }

                                // 4. 使用预处理后的数据进行 AWB 计算
                                IspApi.AWBCal(processBuffer,
                                    _ispProcessor.IspCommonConfig.ResolutionWidth,
                                    _ispProcessor.IspCommonConfig.ResolutionHeight,
                                    (int)_ispProcessor.IspCommonConfig.Bayer,
                                    XArray, YArray, WidthArray, HeightArray,
                                    ref bgain, ref rgain);
                            });

                            string resultKey = $"{imageInfo.ImageName}_{category}";
                            var result = new KeyValuePair<int, int>(rgain, bgain / 4);

                            lock (correctionData)
                            {
                                correctionData[resultKey] = result;
                            }

                            localResults.Add($"{resultKey}: RGain={rgain}, BGain={bgain / 4} ({imageInfo.Selections.Count}个选区合并)");

                            // 线程安全地更新进度
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                UpdateProgressInfo($"✓ {category} - {imageInfo.ImageName}: RGain={rgain}, BGain={bgain / 4} (合并{imageInfo.Selections.Count}个选区)");
                            });

                            return 1; // 返回处理计数
                        }
                        else
                        {
                            // 非白点类别：每个选区独立计算
                            int selectionCount = 0;

                            for (int selectionIndex = 0; selectionIndex < imageInfo.Selections.Count; selectionIndex++)
                            {
                                var currentSelection = imageInfo.Selections[selectionIndex];

                                int[] XArray = new int[6];
                                int[] YArray = new int[6];
                                int[] HeightArray = new int[6];
                                int[] WidthArray = new int[6];

                                XArray[0] = currentSelection.x;
                                YArray[0] = currentSelection.y;
                                HeightArray[0] = currentSelection.height;
                                WidthArray[0] = currentSelection.width;

                                int bgain = 0;
                                int rgain = 0;

                                // 在后台线程执行CPU密集型计算
                                await Task.Run(() =>
                                {
                                    // 1. 复制原始 buffer（避免修改原始数据）
                                    byte[] processBuffer = (byte[])imageInfo.Buffer.Clone();

                                    // 2. 应用 BLC 黑电平校正（如果启用）
                                    if (_ispProcessor.IspCommonConfig.ProcessorStepsEnables.Any(s => s.Key == IspModule.Blc && s.Value))
                                    {
                                        _ispProcessor.AllProcessSteps[IspModule.Blc].ProcessRawBuffer(ref processBuffer);
                                    }

                                    // 3. 应用 LSC 镜头阴影校正（如果启用）
                                    if (_ispProcessor.IspCommonConfig.ProcessorStepsEnables.Any(s => s.Key == IspModule.Lsc && s.Value))
                                    {
                                        _ispProcessor.AllProcessSteps[IspModule.Lsc].ProcessRawBuffer(ref processBuffer);
                                    }

                                    // 4. 使用预处理后的数据进行 AWB 计算
                                    IspApi.AWBCal(processBuffer,
                                        _ispProcessor.IspCommonConfig.ResolutionWidth,
                                        _ispProcessor.IspCommonConfig.ResolutionHeight,
                                        (int)_ispProcessor.IspCommonConfig.Bayer,
                                        XArray, YArray, WidthArray, HeightArray,
                                        ref bgain, ref rgain);
                                });

                                string resultKey = $"{imageInfo.ImageName}_{category}_#{selectionIndex + 1}";
                                var result = new KeyValuePair<int, int>(rgain, bgain / 4);

                                lock (correctionData)
                                {
                                    correctionData[resultKey] = result;
                                }

                                localResults.Add($"{resultKey}: RGain={rgain}, BGain={bgain / 4}");
                                selectionCount++;

                                // 线程安全地更新进度
                                Application.Current.Dispatcher.Invoke(() =>
                                {
                                    UpdateProgressInfo($"✓ {category} - {imageInfo.ImageName}[#{selectionIndex + 1}]: RGain={rgain}, BGain={bgain / 4}");
                                });
                            }

                            return selectionCount;
                        }
                    });

                    // 等待该类别的所有图像处理完成
                    var counts = await Task.WhenAll(imageTasks);
                    localProcessedCount = counts.Sum();

                    // 线程安全地汇总结果
                    lock (categoryResults)
                    {
                        categoryResults[category] = localResults;
                    }

                    lock (processedCountLock)
                    {
                        totalProcessedItems += localProcessedCount;
                    }

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        UpdateStatus($"✓ 类别 '{category}' 计算完成！处理了 {localProcessedCount} 个项目");
                    });
                });

                // 等待所有类别处理完成
                await Task.WhenAll(tasks);


                // 显示详细的计算结果
                //StringBuilder resultSummary = new StringBuilder();
                //resultSummary.AppendLine("AWB增益计算完成！");
                //resultSummary.AppendLine($"总处理项目数: {totalProcessedItems}");
                //resultSummary.AppendLine();

                //foreach (var category in categoryResults.Keys.OrderBy(k => k))
                //{
                //    bool isWhitePoint = (category == "白点");
                //    string modeDesc = isWhitePoint ? "（每图合并计算）" : "（每选区独立计算）";

                //    resultSummary.AppendLine($"【{category}】{modeDesc} 共 {categoryResults[category].Count} 个结果:");
                //    foreach (var result in categoryResults[category])
                //    {
                //        resultSummary.AppendLine($"  • {result}");
                //    }
                //    resultSummary.AppendLine();
                //}

                //MessageBox.Show(this,
                //    resultSummary.ToString(),
                //    "计算完成",
                //    MessageBoxButton.OK,
                //    MessageBoxImage.Information);

                // 智能显示计算结果（根据数据量选择最佳展示方式）
                DisplayCalculationResults(categoryResults, totalProcessedItems);

                UpdateStatus($"✓ AWB增益计算全部完成！共处理 {totalProcessedItems} 个项目");
                UpdateProgressInfo($"计算结果已返回 | 共 {GetTotalSelectionCount()} 个选区参与计算");

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    $"计算过程中发生错误:\n{ex.Message}\n\n请检查图像数据和选区是否有效。",
                    "计算错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                UpdateStatus("❌ AWB增益计算失败");
                UpdateProgressInfo($"错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 异步默认模式计算AWB增益值
        /// </summary>
        private async Task CalculateAWBByDefaultModeAsync()
        {
            UpdateStatus("使用默认模式计算AWB增益值...");

            int processedCount = 0;
            var correctionData = (Dictionary<string, KeyValuePair<int, int>>)DataContext;
            correctionData.Clear();

            // 并行处理所有图像
            var tasks = _rawImageBufferList.Select(async item =>
            {
                int[] XArray = new int[6];
                int[] YArray = new int[6];
                int[] HeightArray = new int[6];
                int[] WidthArray = new int[6];

                var dataItem = _rubberBandDataList[item.Key];

                if (dataItem.Count > 0)
                {
                    for (int j = 0; j < Math.Min(dataItem.Count, 6); j++)
                    {
                        XArray[j] = dataItem[j].x;
                        YArray[j] = dataItem[j].y;
                        HeightArray[j] = dataItem[j].height;
                        WidthArray[j] = dataItem[j].width;
                    }

                    int bgain = 0;
                    int rgain = 0;

                    // 在后台线程执行计算
                    await Task.Run(() =>
                    {
                        // 1. 复制原始 buffer（避免修改原始数据）
                        byte[] processBuffer = (byte[])item.Value.Clone();

                        // 2. 应用 BLC 黑电平校正（如果启用）
                        if (_ispProcessor.IspCommonConfig.ProcessorStepsEnables.Any(s => s.Key == IspModule.Blc && s.Value))
                        {
                            _ispProcessor.AllProcessSteps[IspModule.Blc].ProcessRawBuffer(ref processBuffer);
                        }

                        // 3. 应用 LSC 镜头阴影校正（如果启用）
                        if (_ispProcessor.IspCommonConfig.ProcessorStepsEnables.Any(s => s.Key == IspModule.Lsc && s.Value))
                        {
                            _ispProcessor.AllProcessSteps[IspModule.Lsc].ProcessRawBuffer(ref processBuffer);
                        }

                        // 4. 使用预处理后的数据进行 AWB 计算
                        IspApi.AWBCal(processBuffer,
                            _ispProcessor.IspCommonConfig.ResolutionWidth,
                            _ispProcessor.IspCommonConfig.ResolutionHeight,
                            (int)_ispProcessor.IspCommonConfig.Bayer,
                            XArray, YArray, WidthArray, HeightArray,
                            ref bgain, ref rgain);
                    });

                    var result = new KeyValuePair<int, int>(rgain, bgain / 4);

                    lock (correctionData)
                    {
                        correctionData[item.Key] = result;
                    }

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        UpdateProgressInfo($"✓ 已计算: {item.Key} (RGain={rgain}, BGain={bgain / 4})");
                    });

                    return 1;
                }
                else
                {
                    lock (correctionData)
                    {
                        correctionData[item.Key] = new KeyValuePair<int, int>(-1, -1);
                    }

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        UpdateProgressInfo($"⚠ 跳过: {item.Key} (无选区)");
                    });

                    return 0;
                }
            });

            var counts = await Task.WhenAll(tasks);
            processedCount = counts.Sum();

            UpdateStatus($"✓ AWB增益计算完成！成功处理 {processedCount}/{_rawImageBufferList.Count} 张图像");
            UpdateProgressInfo($"计算结果已返回 | 共 {GetTotalSelectionCount()} 个选区参与计算");

            MessageBox.Show(this,
                $"AWB增益计算完成！\n成功处理 {processedCount}/{_rawImageBufferList.Count} 张图像\n共 {GetTotalSelectionCount()} 个选区参与计算",
                "计算完成",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            DialogResult = true;
            Close();
        }

        // 默认的AWB计算模式（不分类别）
        private void CalculateAWBByDefaultMode()
        {
            UpdateStatus("使用默认模式计算AWB增益值...");

            int processedCount = 0;
            var correctionData = (Dictionary<string, KeyValuePair<int, int>>)DataContext;
            correctionData.Clear();

            foreach (var item in _rawImageBufferList)
            {
                int[] XArray = new int[6];
                int[] YArray = new int[6];
                int[] HeightArray = new int[6];
                int[] WidthArray = new int[6];

                var dataItem = _rubberBandDataList[item.Key];

                UpdateProgressInfo($"正在处理: {item.Key}...");

                if (dataItem.Count > 0)
                {
                    for (int j = 0; j < Math.Min(dataItem.Count, 6); j++)
                    {
                        XArray[j] = dataItem[j].x;
                        YArray[j] = dataItem[j].y;
                        HeightArray[j] = dataItem[j].height;
                        WidthArray[j] = dataItem[j].width;
                    }

                    int bgain = 0;
                    int rgain = 0;

                    byte[] tmpBuffer = (byte[])item.Value.Clone();

                    // 应用 BLC 黑电平校正（如果启用）
                    if (_ispProcessor.IspCommonConfig.ProcessorStepsEnables.Any(s => s.Key == IspModule.Blc && s.Value))
                    {
                        _ispProcessor.AllProcessSteps[IspModule.Blc].ProcessRawBuffer(ref tmpBuffer);
                    }

                    // 应用 LSC 镜头阴影校正（如果启用）
                    if (_ispProcessor.IspCommonConfig.ProcessorStepsEnables.Any(s => s.Key == IspModule.Lsc && s.Value))
                    {
                        _ispProcessor.AllProcessSteps[IspModule.Lsc].ProcessRawBuffer(ref tmpBuffer);
                    }

                    IspApi.AWBCal(tmpBuffer, _ispProcessor.IspCommonConfig.ResolutionWidth, _ispProcessor.IspCommonConfig.ResolutionHeight,
                        (int)_ispProcessor.IspCommonConfig.Bayer, XArray, YArray, WidthArray, HeightArray, ref bgain, ref rgain);

                    correctionData[item.Key] = new KeyValuePair<int, int>(rgain, bgain / 4);
                    processedCount++;

                    UpdateProgressInfo($"✓ 已计算: {item.Key} (RGain={rgain}, BGain={bgain / 4})");
                }
                else
                {
                    correctionData[item.Key] = new KeyValuePair<int, int>(-1, -1);
                    UpdateProgressInfo($"⚠ 跳过: {item.Key} (无选区)");
                }
            }

            UpdateStatus($"✓ AWB增益计算完成！成功处理 {processedCount}/{_rawImageBufferList.Count} 张图像");
            UpdateProgressInfo($"计算结果已返回 | 共 {GetTotalSelectionCount()} 个选区参与计算");

            MessageBox.Show(this,
                $"AWB增益计算完成！\n成功处理 {processedCount}/{_rawImageBufferList.Count} 张图像\n共 {GetTotalSelectionCount()} 个选区参与计算",
                "计算完成",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            int totalSelections = GetTotalSelectionCount();
            
            if (totalSelections > 0)
            {
                var result = MessageBox.Show(this,
                    $"确定要取消操作吗？\n\n当前已选择 {totalSelections} 个色块区域将被丢弃。",
                    "确认取消",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.No)
                {
                    return;
                }
            }

            UpdateStatus("用户取消了操作");
            DialogResult = false;
            Close();
        }

        private void BeforePicButton_Click(object sender, RoutedEventArgs e)
        {
            if (RawImgsTab.SelectedIndex > 0)
            {
                RawImgsTab.SelectedIndex--;
                
                string prevImageName = ((TabItem)RawImgsTab.SelectedItem).Header.ToString();
                UpdateStatus($"⬅️ 切换到上一张: {prevImageName}");
            }
            else
            {
                UpdateStatus("已经是第一张图像");
            }
        }

        private void NextPicButton_Click(object sender, RoutedEventArgs e)
        {
            if (RawImgsTab.SelectedIndex < RawImgsTab.Items.Count - 1)
            {
                RawImgsTab.SelectedIndex++;
                
                string nextImageName = ((TabItem)RawImgsTab.SelectedItem).Header.ToString();
                UpdateStatus($"➡️ 切换到下一张: {nextImageName}");
            }
            else
            {
                UpdateStatus("已经是最后一张图像");
            }
        }


        // 撤销特定类别按钮点击事件
        private void UndoCategoryButton_Click(object sender, RoutedEventArgs e)
        {
            if (RawImgsTab.SelectedItem == null || CategoryCombo.SelectedItem == null)
            {
                return;
            }

            var currentTabItem = (TabItem)RawImgsTab.SelectedItem;
            var imgControl = (ImageWithRubberBandControl)currentTabItem.Content;

            var selectedCategory = CategoryCombo.SelectedItem as ComboBoxItem;
            if (selectedCategory != null)
            {
                string categoryName = selectedCategory.Content.ToString();

                // 获取撤销前的数量
                int countBefore = 0;
                if (imgControl.DataContext is ObservableCollection<RubberBandData> rubberBandData)
                {
                    countBefore = rubberBandData.Count(d => d.Category == categoryName);
                }

                imgControl.UndoDrawRubberBandByCategoryAll(categoryName);

                // 更新选择计数
                RefreshCurrentTabSelectionInfo();
                UpdateTotalStatsDisplay();

                string imageName = currentTabItem.Header.ToString();
                UpdateStatus($"🗑️ 已撤销 '{imageName}' 中 '{categoryName}' 类别的选区 ({countBefore} 个)");
                UpdateProgressInfo($"撤销成功 | 当前选区: {GetTotalSelectionCount()}");
            }
        }

        // 清空全部按钮点击事件
        private void ClearAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (RawImgsTab.SelectedItem == null)
            {
                return;
            }

            var currentTabItem = (TabItem)RawImgsTab.SelectedItem;
            var imgControl = (ImageWithRubberBandControl)currentTabItem.Content;

            // 获取清空前的数量
            int countBefore = 0;
            if (imgControl.DataContext is ObservableCollection<RubberBandData> rubberBandData)
            {
                countBefore = rubberBandData.Count;
            }

            imgControl.ClearRubberBands();

            // 更新选择计数
            RefreshCurrentTabSelectionInfo();
            UpdateTotalStatsDisplay();

            string imageName = currentTabItem.Header.ToString();
            UpdateStatus($"🧹 已清空 '{imageName}' 中的所有选区 ({countBefore} 个)");
            UpdateProgressInfo($"清空完成 | 当前选区: {GetTotalSelectionCount()}");
        }

        private void UndoButton_Click(object sender, RoutedEventArgs e)
        {
            if (RawImgsTab.SelectedItem == null)
            {
                return;
            }

            var currentTabItem = (TabItem)RawImgsTab.SelectedItem;
            var imgControl = (ImageWithRubberBandControl)currentTabItem.Content;

            //imgControl.UndoDrawRubberBand();

            // 获取当前选择的类别
            string selectedCategory = null;
            if (CategoryCombo.SelectedItem != null)
            {
                var comboItem = CategoryCombo.SelectedItem as ComboBoxItem;
                selectedCategory = comboItem?.Content.ToString();
            }

            // 如果选择了特定类别，则只撤销该类别的最后一条记录
            if (!string.IsNullOrEmpty(selectedCategory))
            {
                imgControl.UndoDrawRubberBandByCategory(selectedCategory);
            }
            else
            {
                // 否则撤销最后一条记录（不管类别）
                imgControl.UndoDrawRubberBand();
            }

            // 更新选择计数
            RefreshCurrentTabSelectionInfo();
            UpdateTotalStatsDisplay();

            string imageName = currentTabItem.Header.ToString();
            UpdateStatus($"↩️ 已撤销 '{imageName}' 的最后一次选区操作");
            UpdateProgressInfo($"撤销成功 | 当前选区: {GetTotalSelectionCount()}");
        }


        /// <summary>
        /// 保存选区数据 - 右键菜单
        /// </summary>
        private void SaveSelections_MenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_rawImageBufferList == null || _rawImageBufferList.Count == 0)
            {
                MessageBox.Show("当前没有选区数据可以保存！", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 创建保存文件对话框
            Microsoft.Win32.SaveFileDialog saveDialog = new Microsoft.Win32.SaveFileDialog();
            saveDialog.Filter = "JSON文件 (*.json)|*.json|所有文件 (*.*)|*.*";
            saveDialog.Title = "保存选区数据";
            saveDialog.FileName = $"selections_{DateTime.Now:yyyyMMdd_HHmmss}.json";

            TabItem currentTab = RawImgsTab.SelectedItem as TabItem;
            if (currentTab == null) return;

            var imgControl = (ImageWithRubberBandControl)currentTab.Content;

            if (saveDialog.ShowDialog() == true)
            {
                bool success = imgControl.SaveSelectionsToFile(saveDialog.FileName);
                if (success)
                {
                    MessageBox.Show($"选区数据保存成功！\n\n共保存 {_rawImageBufferList.Count} 个选区。\n文件位置：{saveDialog.FileName}",
                        "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("选区数据保存失败！", "错误",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// 导入选区数据 - 右键菜单
        /// </summary>
        private void ImportSelections_MenuItem_Click(object sender, RoutedEventArgs e)
        {
            // 创建打开文件对话框
            Microsoft.Win32.OpenFileDialog openDialog = new Microsoft.Win32.OpenFileDialog();
            openDialog.Filter = "JSON文件 (*.json)|*.json|所有文件 (*.*)|*.*";
            openDialog.Title = "导入选区数据";


            TabItem currentTab = RawImgsTab.SelectedItem as TabItem;
            if (currentTab == null) return;

            var imgControl = (ImageWithRubberBandControl)currentTab.Content;

            if (openDialog.ShowDialog() == true)
            {
                bool success = imgControl.LoadSelectionsFromFile(openDialog.FileName);
                if (success)
                {
                    MessageBox.Show($"选区数据导入成功！\n\n共导入 {_rawImageBufferList.Count} 个选区。\n文件位置：{openDialog.FileName}",
                        "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("选区数据导入失败！\n请检查文件格式是否正确。",
                        "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// 清空所有选区 - 右键菜单
        /// </summary>
        private void ClearAll_MenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_rawImageBufferList == null || _rawImageBufferList.Count == 0)
            {
                MessageBox.Show("当前没有选区数据！", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            TabItem currentTab = RawImgsTab.SelectedItem as TabItem;
            if (currentTab == null) return;

            var imgControl = (ImageWithRubberBandControl)currentTab.Content;

            MessageBoxResult result = MessageBox.Show(
                $"确定要清空所有选区吗？\n\n当前共有 {_rawImageBufferList.Count} 个选区将被删除。\n此操作不可撤销！",
                "确认清空",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                imgControl.ClearRubberBands();
            }
        }


        /// <summary>
        /// 批量导入选区数据 - 从文件导入并应用到所有图像或仅当前图像
        /// </summary>
        private void BatchImportSelectionsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_imgPaths == null || _imgPaths.Length == 0)
            {
                MessageBox.Show("没有可导入的图像！", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog();
            openFileDialog.Filter = "JSON文件 (*.json)|*.json|所有文件 (*.*)|*.*";
            openFileDialog.Title = "选择选区数据文件";
            openFileDialog.Multiselect = false;

            if (openFileDialog.ShowDialog() == true)
            {
                string selectedFile = openFileDialog.FileName;

                if (!File.Exists(selectedFile))
                {
                    MessageBox.Show("选择的文件不存在！", "错误",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var applyOption = ShowApplyOptionsDialog();

                if (applyOption == ApplyOption.Cancel)
                {
                    UpdateStatus("已取消操作");
                    return;
                }

                int successCount = 0;
                int failCount = 0;
                var failedImages = new List<string>();

                try
                {
                    if (applyOption == ApplyOption.CurrentOnly)
                    {
                        if (RawImgsTab.SelectedItem is TabItem currentTab)
                        {
                            string imageName = ((TextBlock)currentTab.Header).Text;

                            var imgControl = (ImageWithRubberBandControl)currentTab.Content;
                            bool success = imgControl.LoadSelectionsFromFile(selectedFile);

                            if (success)
                            {
                                successCount = 1;
                                UpdateStatus($"✅ 已成功导入选区到: {imageName}");
                            }
                            else
                            {
                                failCount = 1;
                                failedImages.Add(imageName);
                                UpdateStatus($"❌ 导入选区失败: {imageName}");
                            }
                        }
                    }
                    else
                    {
                        foreach (TabItem tabItem in RawImgsTab.Items)
                        {
                            string imageName = ((TextBlock)tabItem.Header).Text;

                            try
                            {
                                var imgControl = (ImageWithRubberBandControl)tabItem.Content;

                                // 确保图像控件已加载
                                if (imgControl.DisplayImageSource == null)
                                {
                                    System.Diagnostics.Debug.WriteLine($"跳过未加载的图像: {imageName}");
                                    failCount++;
                                    failedImages.Add(imageName + " (未加载)");
                                    continue;
                                }

                                imgControl.ClearRubberBands();
                                bool success = imgControl.LoadSelectionsFromFile(selectedFile);

                                if (success)
                                {
                                    successCount++;
                                }
                                else
                                {
                                    failCount++;
                                    failedImages.Add(imageName);
                                }
                            }
                            catch (Exception ex)
                            {
                                failCount++;
                                failedImages.Add(imageName);
                                System.Diagnostics.Debug.WriteLine($"应用选区异常: {imageName} - {ex.Message}");
                            }
                        }
                    }

                    StringBuilder resultMessage = new StringBuilder();

                    if (applyOption == ApplyOption.CurrentOnly)
                    {
                        resultMessage.AppendLine("📂 选区导入完成！");
                        resultMessage.AppendLine();
                        resultMessage.AppendLine($"目标图像: 当前图像");
                    }
                    else
                    {
                        resultMessage.AppendLine("📦 批量应用完成！");
                        resultMessage.AppendLine();
                        resultMessage.AppendLine($"源文件: {Path.GetFileName(selectedFile)}");
                        resultMessage.AppendLine($"目标图像: 所有图像 ({RawImgsTab.Items.Count} 张)");
                    }

                    resultMessage.AppendLine();
                    resultMessage.AppendLine($"✅ 成功: {successCount} 张图像");

                    if (failCount > 0)
                    {
                        resultMessage.AppendLine($"❌ 失败: {failCount} 张图像");
                    }

                    resultMessage.AppendLine();
                    resultMessage.AppendLine($"当前总选区数: {GetTotalSelectionCount()} 个");

                    if (failedImages.Count > 0 && failedImages.Count <= 10)
                    {
                        resultMessage.AppendLine();
                        resultMessage.AppendLine("失败的图像:");
                        foreach (var image in failedImages.Take(10))
                        {
                            resultMessage.AppendLine($"  • {image}");
                        }
                    }

                    MessageBox.Show(resultMessage.ToString(), "操作结果",
                        MessageBoxButton.OK,
                        failCount > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);

                    RefreshCurrentTabSelectionInfo();
                    UpdateTotalStatsDisplay();

                    if (applyOption == ApplyOption.AllImages)
                    {
                        UpdateStatus($"📦 已将选区应用到 {successCount} 张图像");
                        UpdateProgressInfo($"批量应用完成 | 当前总选区数: {GetTotalSelectionCount()}");
                    }
                    else
                    {
                        UpdateStatus($"📂 选区导入完成");
                        UpdateProgressInfo($"导入完成 | 当前总选区数: {GetTotalSelectionCount()}");
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"操作过程中发生错误：\n\n{ex.Message}",
                        "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    UpdateStatus($"❌ 操作失败: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 显示应用选项对话框
        /// </summary>
        private ApplyOption ShowApplyOptionsDialog()
        {
            var dialog = new Window
            {
                Title = "选择应用方式",
                Width = 450,
                Height = 280,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                Background = new SolidColorBrush(Color.FromRgb(245, 245, 245))
            };

            var mainGrid = new Grid();
            mainGrid.Margin = new Thickness(20);

            var rowDefinitions = new RowDefinition[]
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
                new RowDefinition { Height = GridLength.Auto }
            };
            foreach (var rowDef in rowDefinitions)
            {
                mainGrid.RowDefinitions.Add(rowDef);
            }

            var titleLabel = new Label
            {
                Content = "📂 如何选择应用方式？",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10)
            };
            Grid.SetRow(titleLabel, 0);
            mainGrid.Children.Add(titleLabel);

            var infoPanel = new StackPanel
            {
                Margin = new Thickness(0,10,0,10)
            };

            var option1Text = new TextBlock
            {
                Text = "🎯 仅应用到当前图像",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.DarkBlue),
                Margin = new Thickness(0, 5, 0, 3)
            };
            var option1Desc = new TextBlock
            {
                Text = "   将选区数据导入到当前选中的图像，不影响其他图像。",
                FontSize = 11,
                Foreground = new SolidColorBrush(Colors.Gray),
                Margin = new Thickness(0, 0, 0, 10),
                TextWrapping = TextWrapping.Wrap
            };

            var option2Text = new TextBlock
            {
                Text = "📋 应用到所有图像",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.DarkGreen),
                Margin = new Thickness(0, 5, 0, 3)
            };
            var option2Desc = new TextBlock
            {
                Text = $"   将选区数据应用到所有 {RawImgsTab.Items.Count} 张图像（会覆盖现有选区）。",
                FontSize = 11,
                Foreground = new SolidColorBrush(Colors.Gray),
                Margin = new Thickness(0, 0, 0, 10),
                TextWrapping = TextWrapping.Wrap
            };

            infoPanel.Children.Add(option1Text);
            infoPanel.Children.Add(option1Desc);
            infoPanel.Children.Add(option2Text);
            infoPanel.Children.Add(option2Desc);

            Grid.SetRow(infoPanel, 1);
            mainGrid.Children.Add(infoPanel);

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 10, 0, 0)
            };

            var btnCurrent = new Button
            {
                Content = "🎯 仅当前图像",
                Width = 120,
                Height = 35,
                Margin = new Thickness(0, 0, 10, 0),
                FontWeight = FontWeights.SemiBold
            };
            btnCurrent.Click += (s, e) => { dialog.DialogResult = true; dialog.Tag = ApplyOption.CurrentOnly; dialog.Close(); };

            var btnAll = new Button
            {
                Content = "📋 所有图像",
                Width = 120,
                Height = 35,
                Margin = new Thickness(0, 0, 10, 0),
                FontWeight = FontWeights.SemiBold,
                Background = new SolidColorBrush(Color.FromRgb(220, 240, 220))
            };
            btnAll.Click += (s, e) => { dialog.DialogResult = true; dialog.Tag = ApplyOption.AllImages; dialog.Close(); };

            var btnCancel = new Button
            {
                Content = "取消",
                Width = 80,
                Height = 35
            };
            btnCancel.Click += (s, e) => { dialog.DialogResult = false; dialog.Tag = ApplyOption.Cancel; dialog.Close(); };

            buttonPanel.Children.Add(btnCurrent);
            buttonPanel.Children.Add(btnAll);
            buttonPanel.Children.Add(btnCancel);

            Grid.SetRow(buttonPanel, 2);
            mainGrid.Children.Add(buttonPanel);

            dialog.Content = mainGrid;

            bool? result = dialog.ShowDialog();

            if (result == true && dialog.Tag is ApplyOption option)
            {
                return option;
            }

            return ApplyOption.Cancel;
        }

        /// <summary>
        /// 应用选项枚举
        /// </summary>
        private enum ApplyOption
        {
            CurrentOnly,
            AllImages,
            Cancel
        }

        /// <summary>
        /// 将当前图像的选区应用到所有其他图像
        /// </summary>
        private void ApplySelectionsToAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (RawImgsTab.SelectedItem == null)
            {
                MessageBox.Show("请先选择一张源图像！", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var sourceTabItem = (TabItem)RawImgsTab.SelectedItem;
            var sourceImgControl = (ImageWithRubberBandControl)sourceTabItem.Content;
            string sourceImageName = ((TextBlock)sourceTabItem.Header).Text;

            ObservableCollection<RubberBandData> sourceData = null;
            if (sourceImgControl.DataContext is ObservableCollection<RubberBandData>)
            {
                sourceData = sourceImgControl.DataContext as ObservableCollection<RubberBandData>;
            }

            if (sourceData == null || sourceData.Count == 0)
            {
                MessageBox.Show("当前图像没有选区数据，无法应用！", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            int targetImageCount = RawImgsTab.Items.Count - 1;
            var result = MessageBox.Show(
                $"确定要将 '{sourceImageName}' 的选区应用到所有其他图像吗？\n\n" +
                $"源图像选区信息：\n" +
                $"  • 总选区数: {sourceData.Count}\n" +
                $"  • 白点: {sourceData.Count(d => d.Category == "白点")}\n" +
                $"  • 非白点: {sourceData.Count(d => d.Category == "非白点")}\n\n" +
                $"这将覆盖其他 {targetImageCount} 张图像的现有选区数据。\n" +
                $"此操作不可撤销！",
                "确认应用到所有图像",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
            {
                UpdateStatus("已取消应用操作");
                return;
            }

            int successCount = 0;
            int failCount = 0;
            var failedImages = new List<string>();

            try
            {
                int processedCount = 0;

                foreach (TabItem tabItem in RawImgsTab.Items)
                {
                    string imageName = ((TextBlock)tabItem.Header).Text;

                    if (imageName == sourceImageName)
                    {
                        continue;
                    }

                    processedCount++;

                    try
                    {
                        var targetImgControl = (ImageWithRubberBandControl)tabItem.Content;
                        targetImgControl.ClearRubberBands();

                        foreach (var sourceSelection in sourceData)
                        {
                            RubberBandData newSelection = new RubberBandData
                            {
                                x = sourceSelection.x,
                                y = sourceSelection.y,
                                width = sourceSelection.width,
                                height = sourceSelection.height,
                                Category = sourceSelection.Category,
                                Color = sourceSelection.Color
                            };

                            if (targetImgControl.DataContext is ObservableCollection<RubberBandData>)
                            {
                                var targetData = targetImgControl.DataContext as ObservableCollection<RubberBandData>;
                                if (targetData != null)
                                {
                                    targetData.Add(newSelection);
                                }
                            }
                        }

                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        failCount++;
                        failedImages.Add(imageName);
                        System.Diagnostics.Debug.WriteLine($"应用选区失败: {imageName} - {ex.Message}");
                    }
                }

                StringBuilder resultMessage = new StringBuilder();
                resultMessage.AppendLine("✅ 选区应用完成！");
                resultMessage.AppendLine();
                resultMessage.AppendLine($"源图像: {sourceImageName}");
                resultMessage.AppendLine($"选区数量: {sourceData.Count} 个");
                resultMessage.AppendLine();
                resultMessage.AppendLine($"✅ 成功应用: {successCount} 张图像");

                if (failCount > 0)
                {
                    resultMessage.AppendLine($"❌ 失败: {failCount} 张图像");
                }

                resultMessage.AppendLine();
                resultMessage.AppendLine($"总共影响: {successCount + failCount} 张图像");
                resultMessage.AppendLine($"当前总选区数: {GetTotalSelectionCount()} 个");

                if (failedImages.Count > 0 && failedImages.Count <= 10)
                {
                    resultMessage.AppendLine();
                    resultMessage.AppendLine("失败的图像:");
                    foreach (var image in failedImages.Take(10))
                    {
                        resultMessage.AppendLine($"  • {image}");
                    }
                }

                MessageBox.Show(resultMessage.ToString(), "应用结果",
                    MessageBoxButton.OK,
                    failCount > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);

                RefreshCurrentTabSelectionInfo();
                UpdateTotalStatsDisplay();

                UpdateStatus($"📋 已将 '{sourceImageName}' 的选区应用到 {successCount} 张图像");
                UpdateProgressInfo($"应用完成 | 当前总选区数: {GetTotalSelectionCount()}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"应用选区过程中发生错误：\n\n{ex.Message}",
                    "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateStatus($"❌ 应用选区失败: {ex.Message}");
            }
        }

        #endregion


        #region 辅助方法

        /// <summary>
        /// 智能显示计算结果（根据数据量自动选择最佳展示方式）
        /// </summary>
        private void DisplayCalculationResults(Dictionary<string, List<string>> categoryResults, int totalProcessedItems)
        {
            // 计算总行数
            int totalLines = 2; // 标题行 + 总数行
            foreach (var category in categoryResults.Keys)
            {
                totalLines += 3; // 类别标题 + 空行 + 至少1行
                totalLines += categoryResults[category].Count; // 每个结果一行
            }

            const int MAX_LINES_FOR_MESSAGEBOX = 30; // MessageBox最大舒适显示行数
            const int MAX_CHARS_FOR_MESSAGEBOX = 1500; // MessageBox最大字符数

            if (totalLines <= MAX_LINES_FOR_MESSAGEBOX)
            {
                // 数据量较小，使用MessageBox显示完整结果
                ShowFullResultsInMessageBox(categoryResults, totalProcessedItems);
            }
            else
            {
                // 数据量较大，使用摘要+详细文件的方式
                ShowSummaryAndSaveDetails(categoryResults, totalProcessedItems, totalLines);
            }
        }

        /// <summary>
        /// 在MessageBox中显示完整结果（适用于小数据量）
        /// </summary>
        private void ShowFullResultsInMessageBox(Dictionary<string, List<string>> categoryResults, int totalProcessedItems)
        {
            StringBuilder resultSummary = new StringBuilder();
            resultSummary.AppendLine("AWB增益计算完成！");
            resultSummary.AppendLine($"总处理项目数: {totalProcessedItems}");
            resultSummary.AppendLine();

            foreach (var category in categoryResults.Keys.OrderBy(k => k))
            {
                bool isWhitePoint = (category == "白点");
                string modeDesc = isWhitePoint ? "（每图合并计算）" : "（每选区独立计算）";

                resultSummary.AppendLine($"【{category}】{modeDesc} 共 {categoryResults[category].Count} 个结果:");
                foreach (var result in categoryResults[category])
                {
                    resultSummary.AppendLine($"  • {result}");
                }
                resultSummary.AppendLine();
            }

            MessageBox.Show(this,
                resultSummary.ToString(),
                "计算完成",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        /// <summary>
        /// 显示摘要并保存详细信息到文件（适用于大数据量）
        /// </summary>
        private void ShowSummaryAndSaveDetails(Dictionary<string, List<string>> categoryResults, int totalProcessedItems, int totalLines)
        {
            // 构建摘要信息
            StringBuilder summary = new StringBuilder();
            summary.AppendLine("═══════════════════════════════════");
            summary.AppendLine("    AWB增益计算完成！");
            summary.AppendLine("═══════════════════════════════════");
            summary.AppendLine();
            summary.AppendLine($"📊 总处理项目数: {totalProcessedItems}");
            summary.AppendLine($"📝 总结果行数: {totalLines}");
            summary.AppendLine();
            summary.AppendLine("───────────────────────────────────");
            summary.AppendLine("各类别统计:");
            summary.AppendLine("───────────────────────────────────");

            int totalResults = 0;
            foreach (var category in categoryResults.Keys.OrderBy(k => k))
            {
                bool isWhitePoint = (category == "白点");
                string modeDesc = isWhitePoint ? "每图合并" : "每选区独立";
                int count = categoryResults[category].Count;
                totalResults += count;

                summary.AppendLine($"  • {category}: {count} 个结果 [{modeDesc}]");

                // 显示前2个和后2个结果作为示例
                var results = categoryResults[category];
                if (results.Count > 0)
                {
                    summary.AppendLine($"    示例:");
                    int showCount = Math.Min(2, results.Count);
                    for (int i = 0; i < showCount; i++)
                    {
                        summary.AppendLine($"      {results[i]}");
                    }
                    if (results.Count > 4)
                    {
                        summary.AppendLine($"      ... 还有 {results.Count - 4} 个结果");
                        // 显示最后2个
                        for (int i = results.Count - 2; i < results.Count; i++)
                        {
                            summary.AppendLine($"      {results[i]}");
                        }
                    }
                }
                summary.AppendLine();
            }

            summary.AppendLine("───────────────────────────────────");
            summary.AppendLine($"✅ 共计 {totalResults} 个增益值已计算完成");
            summary.AppendLine();
            summary.AppendLine("💡 提示: 详细结果已保存到文件");

            // 保存详细结果到文件
            string detailFilePath = SaveDetailedResultsToFile(categoryResults, totalProcessedItems);

            // 显示摘要并提供查看详情的选项
            summary.AppendLine();
            summary.AppendLine("是否打开详细结果文件？");

            var result = MessageBox.Show(this,
                summary.ToString(),
                "计算完成 - 摘要",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            // 如果用户选择查看详细信息
            if (result == MessageBoxResult.Yes && !string.IsNullOrEmpty(detailFilePath))
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = detailFilePath,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this,
                        $"无法打开文件:\n{ex.Message}\n\n文件位置:\n{detailFilePath}",
                        "提示",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
        }

        /// <summary>
        /// 将详细结果保存到文本文件
        /// </summary>
        private string SaveDetailedResultsToFile(Dictionary<string, List<string>> categoryResults, int totalProcessedItems)
        {
            try
            {
                // 生成文件名（包含时间戳）
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string fileName = $"AWB_Calculation_Results_{timestamp}.txt";

                // 获取应用程序目录
                string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string filePath = System.IO.Path.Combine(appDirectory, "AWB_Results", fileName);

                // 确保目录存在
                string directory = System.IO.Path.GetDirectoryName(filePath);
                if (!System.IO.Directory.Exists(directory))
                {
                    System.IO.Directory.CreateDirectory(directory);
                }

                // 写入详细结果
                using (var writer = new System.IO.StreamWriter(filePath, false, Encoding.UTF8))
                {
                    writer.WriteLine("═══════════════════════════════════════════════════════");
                    writer.WriteLine("           AWB增益计算详细结果报告");
                    writer.WriteLine("═══════════════════════════════════════════════════════");
                    writer.WriteLine();
                    writer.WriteLine($"生成时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    writer.WriteLine($"总处理项目数: {totalProcessedItems}");
                    writer.WriteLine($"总类别数: {categoryResults.Count}");
                    writer.WriteLine();
                    writer.WriteLine(new string('═', 60));
                    writer.WriteLine();

                    foreach (var category in categoryResults.Keys.OrderBy(k => k))
                    {
                        bool isWhitePoint = (category == "白点");
                        string modeDesc = isWhitePoint ? "每图合并计算" : "每选区独立计算";

                        writer.WriteLine($"┌─────────────────────────────────────────────┐");
                        writer.WriteLine($"│ 类别: {category,-30} │");
                        writer.WriteLine($"│ 模式: {modeDesc,-30} │");
                        writer.WriteLine($"│ 结果数: {categoryResults[category].Count,-28} │");
                        writer.WriteLine($"└─────────────────────────────────────────────┘");
                        writer.WriteLine();

                        int index = 1;
                        foreach (var result in categoryResults[category])
                        {
                            writer.WriteLine($"  [{index:D3}] {result}");
                            index++;
                        }

                        writer.WriteLine();
                        writer.WriteLine(new string('─', 60));
                        writer.WriteLine();
                    }

                    writer.WriteLine();
                    writer.WriteLine("═══════════════════════════════════════════════════════");
                    writer.WriteLine("                    报告结束");
                    writer.WriteLine("═══════════════════════════════════════════════════════");
                }

                return filePath;
            }
            catch (Exception ex)
            {
                // 如果保存失败，返回空字符串
                System.Diagnostics.Debug.WriteLine($"保存结果文件失败: {ex.Message}");
                return null;
            }
        }

        #endregion

    }

    /// <summary>
    /// 图像处理数据结构
    /// </summary>
    public class ImageProcessData
    {
        public string ImageName { get; set; }
        public byte[] Buffer { get; set; }
        public List<RubberBandData> Selections { get; set; }

        public ImageProcessData(string imageName, byte[] buffer, List<RubberBandData> selections)
        {
            ImageName = imageName;
            Buffer = buffer;
            Selections = selections;
        }
    }

    /// <summary>
    /// 图像加载结果数据结构
    /// </summary>
    public class ImageLoadResult
    {
        public int Index { get; set; }
        public string FileName { get; set; }
        public byte[] Buffer { get; set; }
        public bool Success { get; set; }

        public ImageLoadResult(int index, string fileName, byte[] buffer, bool success)
        {
            Index = index;
            FileName = fileName;
            Buffer = buffer;
            Success = success;
        }
    }

}
