using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Xml;

namespace ThunderSE.Ui.CommonCustomControl
{
    public struct RubberBandData
    {
        public int x;
        public int y;
        public int width;
        public int height;
        public string Category; // 新增类别字段
        public Brush Color; // 新增颜色字段
    }

    /// <summary>
    /// RawImgDisplayControl.xaml 的交互逻辑
    /// </summary>
    /// 
    public partial class ImageWithRubberBandControl : UserControl
    {
        //private Processor _ispProcessor = null;
        //private int _imgHeight = 0;
        //private int _imgWidth = 0;

        private Point _startPoint;
        private Point _endPoint;
        private Shape _rubberBand;

        //private byte[] _rawImgBuffer;
        private ImageSource _imgSource;
        private TextBlock _colorDisplayBlock;
        private ObservableCollection<RubberBandData> _dataList;

        private Dictionary<string, Brush> _categoryColors = new Dictionary<string, Brush>
        {
            { "白点", Brushes.Red },
            { "非白点", Brushes.GreenYellow },
            { "默认", Brushes.Red }
        };

        // 不同类别的最大选区数配置
        private Dictionary<string, int> _categoryMaxBands = new Dictionary<string, int>
        {
            { "白点", 6 },      // 白点类别最多6个选区
            { "非白点", 24 },   // 非白点类别最多24个选区
            { "默认", 6 }       // 默认使用6个选区
        };

        // 当前选中的类别
        public string CurrentCategory { get; set; } = "白点";

        //public Processor IspProcessor
        //{
        //    get { return _ispProcessor; }
        //    set
        //    {
        //        _ispProcessor = value;
        //        _imgHeight = _ispProcessor.IspCommonConfig.ResolutionHeight;
        //        _imgWidth = _ispProcessor.IspCommonConfig.ResolutionWidth;
        //    }
        //}

        public int MaxBands
        {
            get;
            set;
        }

        // 每个类别的最大选区数（保留此属性用于向后兼容，但实际使用字典配置）
        public int MaxBandsPerCategory
        {
            get
            {
                // 返回当前类别的最大选区数
                return GetMaxBandsForCategory(CurrentCategory);
            }
            set
            {
                // 设置所有类别的默认值（不推荐直接使用）
                foreach (var key in _categoryMaxBands.Keys.ToList())
                {
                    _categoryMaxBands[key] = value;
                }
            }
        }

        /// <summary>
        /// 获取指定类别的最大选区数
        /// </summary>
        /// <param name="category">类别名称</param>
        /// <returns>该类别允许的最大选区数</returns>
        private int GetMaxBandsForCategory(string category)
        {
            if (string.IsNullOrEmpty(category))
                return _categoryMaxBands["默认"];

            return _categoryMaxBands.ContainsKey(category)
                ? _categoryMaxBands[category]
                : _categoryMaxBands["默认"];
        }

        /// <summary>
        /// 设置指定类别的最大选区数
        /// </summary>
        /// <param name="category">类别名称</param>
        /// <param name="maxBands">最大选区数</param>
        public void SetMaxBandsForCategory(string category, int maxBands)
        {
            if (!string.IsNullOrEmpty(category) && maxBands > 0)
            {
                _categoryMaxBands[category] = maxBands;
            }
        }

        public ImageSource DisplayImageSource
        {
            get
            {
                return _imgSource;
            }
            set
            {
                _imgSource = value;
                ImageControl.Source = _imgSource;
            }
        }

        public ImageWithRubberBandControl()
        {
            InitializeComponent();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _dataList = (ObservableCollection<RubberBandData>)DataContext;
            if (_imgSource != null)
            {
                ImageControl.Source = _imgSource;
            }
        }

        // 获取某个类别当前的选区数量
        private int GetCategoryCount(string category)
        {
            if (_dataList == null || string.IsNullOrEmpty(category))
                return 0;

            return _dataList.Count(d => d.Category == category);
        }

        /// <summary>
        /// 检查是否可以为当前类别添加新的选区
        /// </summary>
        /// <param name="category">类别名称</param>
        /// <param name="currentCount">当前选区数量</param>
        /// <param name="maxAllowed">最大允许的选区数量</param>
        /// <returns>是否可以添加</returns>
        private bool CanAddSelectionForCategory(string category, out int currentCount, out int maxAllowed)
        {
            currentCount = GetCategoryCount(category);
            maxAllowed = GetMaxBandsForCategory(category);

            return currentCount < maxAllowed;
        }

        private void mainCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var currentPoint = e.GetPosition(mainCanvas);

            // 检查当前类别的选区数量是否已达到上限
            if (!CanAddSelectionForCategory(CurrentCategory, out int currentCount, out int maxAllowed))
            {
                // 当前类别已达到最大选区数，显示提示信息
                MessageBox.Show(
                    $"类别 '{CurrentCategory}' 已达到最大选区数 ({maxAllowed}个)\n\n" +
                    $"当前已有 {currentCount} 个选区，无法继续添加。\n" +
                    $"如需添加更多选区，请删除部分现有选区或切换到其他类别。",
                    "提示",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            //if (!IsMouseCaptured && _dataList.Count < MaxBands &&
            //    Math.Max(currentPoint.X, 0) == currentPoint.X && Math.Min(currentPoint.X, ImageControl.ActualWidth) == currentPoint.X &&
            //    Math.Max(currentPoint.Y, 0) == currentPoint.Y && Math.Min(currentPoint.Y, ImageControl.Height) == currentPoint.Y)
            //{
            //    _startPoint = currentPoint;
            //    Mouse.Capture(mainCanvas);
            //}
            // 检查鼠标位置是否在有效范围内
            bool isValidPosition =
                Math.Max(currentPoint.X, 0) == currentPoint.X &&
                Math.Min(currentPoint.X, ImageControl.ActualWidth) == currentPoint.X &&
                Math.Max(currentPoint.Y, 0) == currentPoint.Y &&
                Math.Min(currentPoint.Y, ImageControl.Height) == currentPoint.Y;

            if (!IsMouseCaptured && isValidPosition)
            {
                _startPoint = currentPoint;
                Mouse.Capture(mainCanvas);
            }
        }

        private void mainCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (mainCanvas.IsMouseCaptured)
            {
                if (_rubberBand != null)
                {
                    mainCanvas.Children.Remove(_colorDisplayBlock);
                    _colorDisplayBlock = null;
                    // 太小的框可以看作是误操作，扔掉就行了
                    if (_rubberBand.Width * _rubberBand.Height < 4)
                    {
                        mainCanvas.Children.Remove(_rubberBand);
                    }
                    else
                    {
                        double maxX = ImageControl.TranslatePoint(new Point(0, 0), mainCanvas).X + ImageControl.ActualWidth;
                        double minX = ImageControl.TranslatePoint(new Point(0, 0), mainCanvas).X;
                        double maxY = ImageControl.TranslatePoint(new Point(0, 0), mainCanvas).Y + ImageControl.ActualHeight;
                        double minY = ImageControl.TranslatePoint(new Point(0, 0), mainCanvas).Y;

                        RubberBandData data = new RubberBandData();

                        double left = Math.Min(_startPoint.X, _endPoint.X);
                        double top = Math.Min(_startPoint.Y, _endPoint.Y);

                        left = Math.Max(left, 0);
                        left = Math.Min(left, ImageControl.ActualWidth);

                        top = Math.Max(top, 0);
                        top = Math.Min(top, ImageControl.ActualHeight);

                        double leftRelativePercent = (left - minX) / (maxX - minX);
                        double topRelativePercent = (top - minY) / (maxY - minY);
                        double widthRelativePercent = _rubberBand.Width / (maxX - minX);
                        double heightRelativePercent = _rubberBand.Height / (maxY - minY);

                        data.y = (int)(topRelativePercent * _imgSource.Height);
                        data.x = (int)(leftRelativePercent * _imgSource.Width);
                        data.width = (int)(widthRelativePercent * _imgSource.Width);
                        data.height = (int)(heightRelativePercent * _imgSource.Height);

                        data.Category = CurrentCategory; // 设置类别
                        data.Color = _categoryColors.ContainsKey(CurrentCategory) ? _categoryColors[CurrentCategory] : _categoryColors["默认"]; // 设置颜色

                        _dataList.Add(data);

                        // 更新矩形框的颜色
                        _rubberBand.Stroke = data.Color;
                    }
                    _rubberBand = null;
                }
                mainCanvas.ReleaseMouseCapture();
            }
        }

        private void mainCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (mainCanvas.IsMouseCaptured)
            {
                _endPoint = e.GetPosition(mainCanvas);

                double maxX = ImageControl.TranslatePoint(new Point(0, 0), mainCanvas).X + ImageControl.ActualWidth;
                double minX = ImageControl.TranslatePoint(new Point(0, 0), mainCanvas).X;
                double maxY = ImageControl.TranslatePoint(new Point(0, 0), mainCanvas).Y + ImageControl.ActualHeight;
                double minY = ImageControl.TranslatePoint(new Point(0, 0), mainCanvas).Y;

                _endPoint.X = _endPoint.X > maxX ? maxX : _endPoint.X;
                _endPoint.X = _endPoint.X < minX ? minX : _endPoint.X;

                _endPoint.Y = _endPoint.Y > maxY ? maxY : _endPoint.Y;
                _endPoint.Y = _endPoint.Y < minY ? minY : _endPoint.Y;

                if (_rubberBand == null)
                {
                    _rubberBand = new Rectangle();
                    //_rubberBand.Stroke = Brushes.Red;
                    // 根据当前类别设置边框颜色
                    _rubberBand.Stroke = _categoryColors.ContainsKey(CurrentCategory) ? _categoryColors[CurrentCategory] : _categoryColors["默认"];
                    _rubberBand.StrokeDashArray = new DoubleCollection(new double[] { 4, 2 });
                    mainCanvas.Children.Add(_rubberBand);
                }

                _rubberBand.Width = Math.Abs(_startPoint.X - _endPoint.X);
                _rubberBand.Height = Math.Abs(_startPoint.Y - _endPoint.Y);

                double left = Math.Min(_startPoint.X, _endPoint.X);
                double top = Math.Min(_startPoint.Y, _endPoint.Y);

                left = Math.Max(left, 0);
                left = Math.Min(left, ImageControl.ActualWidth);

                top = Math.Max(top, 0);
                top = Math.Min(top, ImageControl.ActualHeight);

                Canvas.SetLeft(_rubberBand, left);
                Canvas.SetTop(_rubberBand, top);

                // 取色
                if (_colorDisplayBlock == null)
                {
                    _colorDisplayBlock = new TextBlock();
                    mainCanvas.Children.Add(_colorDisplayBlock);
                }

                double AbsoluteXValue = (_endPoint.X - minX) / (maxX - minX) * _imgSource.Width;
                AbsoluteXValue = AbsoluteXValue == _imgSource.Width ? AbsoluteXValue - 1 : AbsoluteXValue;

                double AbsoluteYValue = (_endPoint.Y - minY) / (maxY - minY) * _imgSource.Height;
                AbsoluteYValue = AbsoluteYValue == _imgSource.Height ? AbsoluteYValue - 1 : AbsoluteYValue;

                var croppedBitmap = new CroppedBitmap((BitmapSource)_imgSource, new Int32Rect((int)AbsoluteXValue, (int)AbsoluteYValue, 1, 1));

                var pixels = new byte[4];
                croppedBitmap.CopyPixels(pixels, 4, 0);

                _colorDisplayBlock.Width = 120;
                _colorDisplayBlock.Height = 20;
                _colorDisplayBlock.Background = Brushes.Black;
                _colorDisplayBlock.Foreground = Brushes.White;
                _colorDisplayBlock.Text = String.Format("R:{0},G:{1},B:{2}", pixels[2], pixels[1], pixels[0]);

                if (_endPoint.X + _colorDisplayBlock.Width > maxX)
                {
                    Canvas.SetLeft(_colorDisplayBlock, _endPoint.X - _colorDisplayBlock.Width);
                }
                else
                {
                    Canvas.SetLeft(_colorDisplayBlock, _endPoint.X);
                }

                Canvas.SetTop(_colorDisplayBlock, _endPoint.Y + 10);
            }
        }

        public void UndoDrawRubberBand()
        {
            // Children[0]为图片
            if (mainCanvas.Children.Count > 1)
            {
                _dataList.RemoveAt(_dataList.Count - 1);
                mainCanvas.Children.RemoveAt(mainCanvas.Children.Count - 1);
            }
        }

        // 修改撤销功能，只撤销特定类别的最后一条记录
        public void UndoDrawRubberBandByCategory(string category = null)
        {
            // Children[0]为图片
            if (mainCanvas.Children.Count > 1 && _dataList.Count > 0)
            {
                // 如果指定了类别，则只撤销该类别的最后一个元素
                if (!string.IsNullOrEmpty(category))
                {
                    for (int i = _dataList.Count - 1; i >= 0; i--)
                    {
                        if (_dataList[i].Category == category)
                        {
                            // 移除对应的矩形框
                            int rectIndex = FindRectangleIndexInCanvas(i);
                            if (rectIndex >= 0)
                            {
                                mainCanvas.Children.RemoveAt(rectIndex);
                            }

                            _dataList.RemoveAt(i);
                            break;
                        }
                    }
                }
                else
                {
                    // 如果没有指定类别，则撤销最后一个元素
                    int lastIndex = _dataList.Count - 1;

                    // 移除对应的矩形框
                    int rectIndex = FindRectangleIndexInCanvas(lastIndex);
                    if (rectIndex >= 0)
                    {
                        mainCanvas.Children.RemoveAt(rectIndex);
                    }

                    _dataList.RemoveAt(lastIndex);
                }
            }
        }

        // 查找特定索引的矩形框在Canvas中的位置
        private int FindRectangleIndexInCanvas(int dataListIndex)
        {
            // 从Canvas的children中找到对应的数据项的矩形
            int rectCount = 0;
            for (int i = 1; i < mainCanvas.Children.Count; i++) // 从1开始，跳过图片
            {
                if (mainCanvas.Children[i] is Rectangle)
                {
                    if (rectCount == dataListIndex)
                    {
                        return i;
                    }
                    rectCount++;
                }
            }
            return -1;
        }

        // 撤销特定类别的所有选择
        public void UndoDrawRubberBandByCategoryAll(string category)
        {
            if (string.IsNullOrEmpty(category)) return;

            // 从后往前遍历，删除所有匹配类别的项
            for (int i = _dataList.Count - 1; i >= 0; i--)
            {
                if (_dataList[i].Category == category)
                {
                    // 移除对应的矩形框
                    int rectIndex = FindRectangleIndexInCanvas(i);
                    if (rectIndex >= 0)
                    {
                        mainCanvas.Children.RemoveAt(rectIndex);
                    }

                    _dataList.RemoveAt(i);
                }
            }
        }

        public void ClearRubberBands()
        {
            // Children[0]为图片
            if (mainCanvas.Children.Count > 1)
            {
                _dataList.Clear();
                mainCanvas.Children.RemoveRange(1, mainCanvas.Children.Count - 1);
            }
        }

        #region 右键菜单事件处理

        /// <summary>
        /// 保存选区数据 - 右键菜单
        /// </summary>
        private void SaveSelections_MenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_dataList == null || _dataList.Count == 0)
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

            if (saveDialog.ShowDialog() == true)
            {
                bool success = SaveSelectionsToFile(saveDialog.FileName);
                if (success)
                {
                    MessageBox.Show($"选区数据保存成功！\n\n共保存 {_dataList.Count} 个选区。\n文件位置：{saveDialog.FileName}",
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

            if (openDialog.ShowDialog() == true)
            {
                bool success = LoadSelectionsFromFile(openDialog.FileName);
                if (success)
                {
                    MessageBox.Show($"选区数据导入成功！\n\n共导入 {_dataList.Count} 个选区。\n文件位置：{openDialog.FileName}",
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
            if (_dataList == null || _dataList.Count == 0)
            {
                MessageBox.Show("当前没有选区数据！", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            MessageBoxResult result = MessageBox.Show(
                $"确定要清空所有选区吗？\n\n当前共有 {_dataList.Count} 个选区将被删除。\n此操作不可撤销！",
                "确认清空",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                ClearRubberBands();
            }
        }

        #endregion


        /// <summary>
        /// 保存当前图像的框选数据到JSON文件
        /// </summary>
        /// <param name="filePath">保存的文件路径</param>
        /// <returns>是否保存成功</returns>
        public bool SaveSelectionsToFile(string filePath)
        {
            try
            {
                if (_dataList == null || _dataList.Count == 0)
                {
                    return false;
                }

                // 创建可序列化的数据结构
                var serializableData = _dataList.Select(data => new
                {
                    data.x,
                    data.y,
                    data.width,
                    data.height,
                    data.Category,
                    ColorName = GetColorNameFromBrush(data.Color)
                }).ToList();

                string jsonString = JsonConvert.SerializeObject(serializableData, Newtonsoft.Json.Formatting.Indented);
                File.WriteAllText(filePath, jsonString, Encoding.UTF8);

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"保存选区数据失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 从JSON文件导入框选数据
        /// </summary>
        /// <param name="filePath">JSON文件路径</param>
        /// <returns>是否导入成功</returns>
        public bool LoadSelectionsFromFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    return false;
                }

                string jsonString = File.ReadAllText(filePath, Encoding.UTF8);

                var loadedData = JsonConvert.DeserializeObject<List<SerializableRubberBandData>>(jsonString);

                if (loadedData == null || loadedData.Count == 0)
                {
                    return false;
                }

                // 清除现有的选区
                ClearRubberBands();

                // 添加导入的选区
                foreach (var item in loadedData)
                {
                    RubberBandData data = new RubberBandData
                    {
                        x = item.x,
                        y = item.y,
                        width = item.width,
                        height = item.height,
                        Category = !string.IsNullOrEmpty(item.Category) ? item.Category : "默认",
                        Color = GetBrushFromColorName(item.ColorName)
                    };

                    _dataList.Add(data);

                    // 在界面上重新绘制矩形框
                    DrawRectangleForData(data);
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载选区数据失败: {ex.Message}");
                return false;
            }
        }
        /// <summary>
        /// 根据数据绘制矩形框
        /// </summary>
        /// <param name="data">选区数据</param>
        private void DrawRectangleForData(RubberBandData data)
        {
            if (_imgSource == null) return;

            double maxX = ImageControl.TranslatePoint(new Point(0, 0), mainCanvas).X + ImageControl.ActualWidth;
            double minX = ImageControl.TranslatePoint(new Point(0, 0), mainCanvas).X;
            double maxY = ImageControl.TranslatePoint(new Point(0, 0), mainCanvas).Y + ImageControl.ActualHeight;
            double minY = ImageControl.TranslatePoint(new Point(0, 0), mainCanvas).Y;

            double imageWidth = maxX - minX;
            double imageHeight = maxY - minY;

            // 计算相对位置
            double leftRelativePercent = (double)data.x / _imgSource.Width;
            double topRelativePercent = (double)data.y / _imgSource.Height;
            double widthRelativePercent = (double)data.width / _imgSource.Width;
            double heightRelativePercent = (double)data.height / _imgSource.Height;

            // 转换为屏幕坐标
            double left = minX + leftRelativePercent * imageWidth;
            double top = minY + topRelativePercent * imageHeight;
            double width = widthRelativePercent * imageWidth;
            double height = heightRelativePercent * imageHeight;

            // 创建矩形框
            var rectangle = new Rectangle
            {
                Width = width,
                Height = height,
                Stroke = data.Color,
                StrokeDashArray = new DoubleCollection(new double[] { 4, 2 })
            };

            Canvas.SetLeft(rectangle, left);
            Canvas.SetTop(rectangle, top);

            mainCanvas.Children.Add(rectangle);
        }

        /// <summary>
        /// 将Brush转换为颜色名称
        /// </summary>
        /// <param name="brush">画刷对象</param>
        /// <returns>颜色名称</returns>
        private string GetColorNameFromBrush(Brush brush)
        {
            if (brush == null) return "Red";

            // 简单映射，可以根据需要扩展
            if (brush == Brushes.Red) return "Red";
            if (brush == Brushes.GreenYellow) return "GreenYellow";
            if (brush == Brushes.Blue) return "Blue";
            if (brush == Brushes.Yellow) return "Yellow";
            if (brush == Brushes.Orange) return "Orange";
            if (brush == Brushes.Purple) return "Purple";

            return "Red"; // 默认红色
        }

        /// <summary>
        /// 根据颜色名称获取Brush
        /// </summary>
        /// <param name="colorName">颜色名称</param>
        /// <returns>画刷对象</returns>
        private Brush GetBrushFromColorName(string colorName)
        {
            switch (colorName?.ToLower())
            {
                case "red": return Brushes.Red;
                case "greenyellow": return Brushes.GreenYellow;
                case "blue": return Brushes.Blue;
                case "yellow": return Brushes.Yellow;
                case "orange": return Brushes.Orange;
                case "purple": return Brushes.Purple;
                default: return Brushes.Red;
            }
        }

        /// <summary>
        /// 可序列化的选区数据结构
        /// </summary>
        private class SerializableRubberBandData
        {
            public int x { get; set; }
            public int y { get; set; }
            public int width { get; set; }
            public int height { get; set; }
            public string Category { get; set; }
            public string ColorName { get; set; }
        }
    }
}
