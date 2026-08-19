using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Xml.Serialization;
using GalaSoft.MvvmLight;
using ThunderSE.Common;

namespace ThunderSE.Uvc
{
    /// <summary>
    /// 摄像头视频属性（Proc Amp / IAMVideoProcAmp）控制器。
    /// 包装 uvc.dll 的原生接口，向 UI 暴露可调参数集合，
    /// 并按设备持久化上次设置的参数值（亮度/对比度/增益等）。
    /// </summary>
    public sealed class ProcAmpController
    {
        private static readonly Lazy<ProcAmpController> _instance =
            new Lazy<ProcAmpController>(() => new ProcAmpController());

        public static ProcAmpController Instance => _instance.Value;

        /// <summary>当前设备支持的可调参数集合（绑定到 UI）</summary>
        public ObservableCollection<ProcAmpParamViewModel> Parameters { get; }

        /// <summary>是否已成功初始化并发现可调属性</summary>
        public bool IsAvailable { get; private set; }

        /// <summary>最后一次初始化的错误信息（不可用时的提示）</summary>
        public string LastError { get; private set; }

        /// <summary>当前正在控制的设备描述符</summary>
        private string _currentDeviceKey = "";

        private ProcAmpController()
        {
            Parameters = new ObservableCollection<ProcAmpParamViewModel>();
            IsAvailable = false;
            LastError = "";
        }

        /// <summary>
        /// 根据设备描述符（如 "video=USB Camera"）初始化 Proc Amp 控制。
        /// 应在设备连接成功后调用。会自动应用该设备上次的持久化设置。
        /// </summary>
        public void Initialize(string deviceDescriptor)
        {
            // 先保存上一个设备的设置，再切换
            Release();

            if (string.IsNullOrEmpty(deviceDescriptor))
            {
                IsAvailable = false;
                LastError = "设备描述符为空";
                return;
            }

            _currentDeviceKey = deviceDescriptor;

            try
            {
                int count = UvcApi.InitProcAmp(deviceDescriptor);
                if (count < 0)
                {
                    // 透出原生 HRESULT，便于区分「类未注册/访问被拒/COM 未初始化」等具体原因。
                    // 部署了旧版 uvc.dll（无 GetLastProcAmpError 导出）时降级为仅显示错误码。
                    int hr = 0;
                    try { hr = UvcApi.GetLastProcAmpError(); }
                    catch (Exception ex) { Logger.Warn($"GetLastProcAmpError unavailable: {ex.Message}"); }

                    IsAvailable = false;
                    LastError = hr != 0
                        ? $"无法访问摄像头图像属性 (错误码 {count}, HRESULT=0x{hr:X8})"
                        : $"无法访问摄像头图像属性 (错误码 {count})";
                    Logger.Warn($"InitProcAmp failed for '{deviceDescriptor}', code={count}, HRESULT=0x{hr:X8}");
                    return;
                }
                if (count == 0)
                {
                    IsAvailable = false;
                    LastError = "该摄像头不支持任何可调图像属性";
                    Logger.Info($"ProcAmp: device '{deviceDescriptor}' supports 0 adjustable properties");
                    return;
                }

                for (int i = 0; i < count; i++)
                {
                    var info = new UvcApi.ProcAmpParamInfo();
                    if (UvcApi.GetProcAmpInfo(i, ref info) == 0)
                    {
                        Parameters.Add(new ProcAmpParamViewModel(info));
                    }
                }

                // 面板数据直接从设备获取显示，不加载持久化设置覆盖。
                // ProcAmpSettings.xml 仅用于保存记录（Save()），不作为初始化数据源。
                IsAvailable = true;
                LastError = "";
                Logger.Info($"ProcAmp initialized: {Parameters.Count} adjustable properties for '{deviceDescriptor}'");
            }
            catch (Exception ex)
            {
                IsAvailable = false;
                LastError = $"初始化异常: {ex.Message}";
                Logger.Error($"ProcAmp initialize exception: {ex.Message}");
            }
        }

        /// <summary>
        /// 将所有参数恢复为设备默认值（并持久化）。
        /// </summary>
        public void ResetAllToDefault()
        {
            foreach (var vm in Parameters)
            {
                if (!vm.SupportsManual) continue;
                if (vm.IsAuto) vm.IsAuto = false;
                vm.Value = vm.Default;
            }
            // 不再自动持久化，需用户点击"保存"按钮才写入文件。
            Logger.Info("ProcAmp: all parameters reset to default");
        }

        /// <summary>
        /// 保存当前设备的参数设置到文件（设备断开/窗口关闭时调用）。
        /// </summary>
        public void Save()
        {
            if (string.IsNullOrEmpty(_currentDeviceKey) || Parameters.Count == 0) return;

            try
            {
                var items = new Dictionary<int, CameraSettingsStore.SavedValue>();
                foreach (var vm in Parameters)
                {
                    if (vm.SupportsManual || vm.SupportsAuto)
                    {
                        // 只持久化设备已确认接受的数值（AppliedValue），
                        // 避免把写入失败（如只读/仿真 UVC 设备）的无效值保存并
                        // 在下次连接时错误显示（"获取数据显示异常"）。
                        items[vm.PropertyId] = new CameraSettingsStore.SavedValue { Value = vm.AppliedValue, Auto = vm.IsAuto };
                    }
                }
                CameraSettingsStore.SetProcAmp(_currentDeviceKey, items);
                CameraSettingsStore.Save();
            }
            catch (Exception ex)
            {
                Logger.Error($"ProcAmp Save failed: {ex.Message}");
            }
        }

        /// <summary>
        /// 从持久化文件加载当前设备的已保存设置并应用到设备。
        /// 仅由"恢复"按钮触发，不自动调用。
        /// </summary>
        public void LoadFromFile()
        {
            if (string.IsNullOrEmpty(_currentDeviceKey)) return;
            var saved = CameraSettingsStore.GetProcAmp(_currentDeviceKey);
            if (saved == null) return;

            foreach (var vm in Parameters)
            {
                if (!saved.TryGetValue(vm.PropertyId, out var savedVal)) continue;

                try
                {
                    if (savedVal.Auto && vm.SupportsAuto)
                    {
                        vm.IsAuto = true;
                    }
                    else if (vm.SupportsManual)
                    {
                        vm.IsAuto = false;
                        vm.Value = savedVal.Value;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"ProcAmp apply saved '{vm.DisplayName}' failed: {ex.Message}");
                }
            }
            Logger.Info("ProcAmp: settings loaded from file");
        }

        /// <summary>
        /// 释放 Proc Amp 资源（设备断开/窗口关闭时调用）。不再自动持久化，
        /// 需用户点击"保存"按钮才写入文件。
        /// </summary>
        public void Release()
        {
            if (Parameters.Count > 0) Parameters.Clear();
            _currentDeviceKey = "";
            IsAvailable = false;
            LastError = "";

            try
            {
                UvcApi.ReleaseProcAmp();
            }
            catch (Exception ex)
            {
                Logger.Error($"ReleaseProcAmp exception: {ex.Message}");
            }
        }

        }

    /// <summary>
    /// 单个 Proc Amp 参数视图模型（亮度/对比度等），直接绑定到滑块与复选框。
    /// </summary>
    public class ProcAmpParamViewModel : ViewModelBase
    {
        private int _value;
        private int _appliedValue;   // 设备已确认接受的数值（初始化=设备当前值，写入成功才更新）
        private bool _isAuto;
        private int _min;
        private int _max;
        private int _step;
        private int _defaultValue;
        private bool _supportsManual;
        private bool _supportsAuto;
        private bool _isDragging;    // 滑块正在拖动中，拖放结束前不写入设备
        private List<ProcAmpOption> _options;   // 离散取值参数（电源频率等）的固定选项；null=连续滑块参数

        public int PropertyId { get; }
        public string DisplayName { get; }
        public int Min { get => _min; set { if (_min != value) { _min = value; RaisePropertyChanged(); } } }
        public int Max { get => _max; set { if (_max != value) { _max = value; RaisePropertyChanged(); } } }
        public int Step { get => _step; set { if (_step != value) { _step = value; RaisePropertyChanged(); } } }
        public int Default { get => _defaultValue; set { if (_defaultValue != value) { _defaultValue = value; RaisePropertyChanged(); } } }

        /// <summary>设备已确认接受的数值（供持久化，只写入成功才更新）</summary>
        public int AppliedValue => _appliedValue;
        public bool SupportsManual { get => _supportsManual; set { if (_supportsManual != value) { _supportsManual = value; RaisePropertyChanged(); } } }
        public bool SupportsAuto { get => _supportsAuto; set { if (_supportsAuto != value) { _supportsAuto = value; RaisePropertyChanged(); } } }

        public int Value
        {
            get => _value;
            set
            {
                if (_value == value) return;
                _value = value;
                RaisePropertyChanged();
                // 徽章文本随滑块拖动实时刷新（连续参数尤其需要）
                RaisePropertyChanged("ValueDisplay");
                if (_options != null)
                {
                    // 离散参数：同步下拉框选中项
                    RaisePropertyChanged("SelectedOption");
                }
                // 非拖动状态（如代码主动设置/键盘导航）时立即写入设备；
                // 拖动状态由 SetDragging(false) 在鼠标释放时一次性写入。
                if (!_isDragging && !_isAuto) ApplyManual(value);
            }
        }

        public bool IsAuto
        {
            get => _isAuto;
            set
            {
                if (_isAuto == value) return;
                _isAuto = value;
                RaisePropertyChanged();
                RaisePropertyChanged("IsSliderEnabled");
                ApplyAuto(value);
            }
        }

        /// <summary>滑块是否可用：支持手动且当前非自动模式</summary>
        public bool IsSliderEnabled => SupportsManual && !_isAuto;

        /// <summary>离散参数的下拉选项（电源频率: 禁用/50 Hz/60 Hz）；连续参数为 null</summary>
        public IReadOnlyList<ProcAmpOption> Options => _options;

        /// <summary>是否为离散取值参数（电源频率等，下拉框代替滑块）</summary>
        public bool IsDiscrete => _options != null;

        /// <summary>是否为连续滑块参数（与 IsDiscrete 相反，用于控制滑块可见性）</summary>
        public bool IsContinuous => !IsDiscrete;

        /// <summary>当前下拉选项（双向绑定）；设备值不在已知选项内时回退到默认值选项</summary>
        public ProcAmpOption SelectedOption
        {
            get
            {
                if (_options == null) return null;
                return _options.Find(o => o.Value == _value)
                       ?? _options.Find(o => o.Value == _defaultValue);
            }
            set
            {
                if (value == null || _options == null) return;
                Value = value.Value;
                RaisePropertyChanged();
            }
        }

        /// <summary>数值徽章显示文本：离散参数显示选项标签（如 50 Hz），其余显示数值</summary>
        public string ValueDisplay
        {
            get
            {
                var opt = SelectedOption;
                return opt != null ? opt.Label : _value.ToString();
            }
        }

        /// <summary>
        /// 设置滑块拖动状态。拖动期间不写入设备，鼠标释放后一次性写入最终值。
        /// </summary>
        public void SetDragging(bool dragging)
        {
            _isDragging = dragging;
            if (!dragging && !_isAuto)
            {
                // 拖动结束，写入最终值
                ApplyManual(_value);
            }
        }

        public ProcAmpParamViewModel(UvcApi.ProcAmpParamInfo info)
        {
            PropertyId = info.PropertyId;
            DisplayName = GetDisplayName((UvcApi.ProcAmpProperty)info.PropertyId);
            Min = info.MinVal;
            Max = info.MaxVal;
            Step = info.StepVal <= 0 ? 1 : info.StepVal;
            Default = info.DefaultVal;
            _value = info.CurrentVal;
            _appliedValue = info.CurrentVal; // 设备当前值即已确认接受的初始值

            // VideoProcAmp_Flags: 0x1=Auto, 0x2=Manual
            SupportsAuto = (info.Flags & 0x1) != 0;
            SupportsManual = (info.Flags & 0x2) != 0;

            // 若当前为自动模式，或仅支持自动，则视为自动
            _isAuto = SupportsAuto && ((info.Flags & 0x1) != 0 || !SupportsManual);
            if (SupportsAuto && !SupportsManual) _isAuto = true;

            // 电源频率（VideoProcAmp_PowerlineFrequency: 1=50Hz, 2=60Hz，0=禁用）
            // 为离散取值参数，改用下拉框避免滑块连续拖动不适配 50/60Hz；不提供“禁用”选项。
            if (PropertyId == (int)UvcApi.ProcAmpProperty.PowerlineFrequency)
            {
                _options = new List<ProcAmpOption>
                {
                    new ProcAmpOption("50 Hz", 1),
                    new ProcAmpOption("60 Hz", 2),
                };
            }
        }

        private void ApplyManual(int v)
        {
            try
            {
                int ret = UvcApi.SetProcAmpValue(PropertyId, v, 0);
                if (ret < 0)
                {
                    Logger.Warn($"SetProcAmp {DisplayName} = {v} failed (code {ret})");
                }
                else
                {
                    _appliedValue = v; // 设备已接受，记录为可持久化数值
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"SetProcAmp {DisplayName} exception: {ex.Message}");
            }
        }

        /// <summary>
        /// 恢复显示为设备已确认接受的数值（仅改界面显示，不触发写入）。
        /// 用于写入失败（只读/仿真设备）时避免显示未生效的无效值。
        /// </summary>
        public void RestoreToApplied()
        {
            if (_value == _appliedValue) return;
            _value = _appliedValue;
            // 显式通知（无参 RaisePropertyChanged 会被 CallerMemberName 解析成方法名，不刷新界面）
            RaisePropertyChanged("Value");
            RaisePropertyChanged("ValueDisplay");
            if (_options != null)
            {
                RaisePropertyChanged("SelectedOption");
            }
        }

        private void ApplyAuto(bool auto)
        {
            try
            {
                int ret = UvcApi.SetProcAmpValue(PropertyId, _value, auto ? 1 : 0);
                if (ret < 0) Logger.Warn($"SetProcAmp {DisplayName} auto={auto} failed (code {ret})");
            }
            catch (Exception ex)
            {
                Logger.Error($"SetProcAmp {DisplayName} auto exception: {ex.Message}");
            }
        }

        public static string GetDisplayName(UvcApi.ProcAmpProperty p)
        {
            switch (p)
            {
                case UvcApi.ProcAmpProperty.Brightness: return "亮度";
                case UvcApi.ProcAmpProperty.Contrast: return "对比度";
                case UvcApi.ProcAmpProperty.Hue: return "色调";
                case UvcApi.ProcAmpProperty.Saturation: return "饱和度";
                case UvcApi.ProcAmpProperty.Sharpness: return "锐度";
                case UvcApi.ProcAmpProperty.Gamma: return "伽马";
                case UvcApi.ProcAmpProperty.ColorEnable: return "色彩启用";
                case UvcApi.ProcAmpProperty.WhiteBalance: return "白平衡";
                case UvcApi.ProcAmpProperty.BacklightCompensation: return "背光补偿";
                case UvcApi.ProcAmpProperty.Gain: return "增益";
                case UvcApi.ProcAmpProperty.DigitalMultiplier: return "数字放大";
                case UvcApi.ProcAmpProperty.DigitalMultiplierLimit: return "数字放大上限";
                case UvcApi.ProcAmpProperty.WhiteBalanceComponent: return "白平衡(分量)";
                case UvcApi.ProcAmpProperty.PowerlineFrequency: return "电源频率";
                default: return "属性" + (int)p;
            }
        }
    }

    /// <summary>
    /// 下拉框选项：标签 + UVC 属性值（用于电源频率等离散取值参数）。
    /// </summary>
    public sealed class ProcAmpOption
    {
        public string Label { get; }
        public int Value { get; }

        public ProcAmpOption(string label, int value) { Label = label; Value = value; }
    }

    #region XML 序列化结构 —— 图像属性与相机控制共用的统一设置文件

    [XmlRoot("ProcAmpSettings")]
    public class ProcAmpSettingsFile
    {
        [XmlArray("Devices")]
        [XmlArrayItem("Device")]
        public List<DeviceFileEntry> Devices { get; set; } = new List<DeviceFileEntry>();
    }

    public class DeviceFileEntry
    {
        [XmlAttribute("key")]
        public string Key { get; set; }

        /// <summary>图像属性 (VideoProcAmp)：亮度/对比度等</summary>
        [XmlArray("Params")]
        [XmlArrayItem("Param")]
        public List<ParamFileEntry> Params { get; set; } = new List<ParamFileEntry>();

        /// <summary>相机控制 (CameraControl)：变焦/对焦/曝光等，与图像属性存于同一文件</summary>
        [XmlArray("CameraControls")]
        [XmlArrayItem("Param")]
        public List<ParamFileEntry> CameraControls { get; set; } = new List<ParamFileEntry>();
    }

    public class ParamFileEntry
    {
        [XmlAttribute("id")]
        public int Id { get; set; }

        [XmlAttribute("value")]
        public int Value { get; set; }

        [XmlAttribute("auto")]
        public bool Auto { get; set; }
    }

    #endregion

/// <summary>
/// 设备参数设置的统一持久化存储。
/// 图像属性(ProcAmp)与相机控制(CameraControl)共用同一份 ProcAmpSettings.xml，
/// 按设备描述符分节（Params/CameraControls）保存。
/// 本类是唯一负责读写该文件的入口，避免两个控制器各自写文件时互相覆盖。
/// </summary>
public static class CameraSettingsStore
{
    private static readonly object _lock = new object();
    private static readonly string SettingsFile =
        Path.Combine(Path.Combine(Directory.GetCurrentDirectory(), "Configs"), "ProcAmpSettings.xml");

    private static Dictionary<string, DeviceData> _store;

    /// <summary>单个参数的保存内容（值 + 是否自动）</summary>
    public sealed class SavedValue
    {
        public int Value;
        public bool Auto;
    }

    private sealed class DeviceData
    {
        public Dictionary<int, SavedValue> ProcAmp { get; } = new Dictionary<int, SavedValue>();
        public Dictionary<int, SavedValue> CameraControl { get; } = new Dictionary<int, SavedValue>();
    }

    private static Dictionary<string, DeviceData> Store
    {
        get
        {
            lock (_lock)
            {
                if (_store == null) _store = LoadFromFile();
                return _store;
            }
        }
    }

    /// <summary>读取设备已保存的图像属性；无记录返回 null</summary>
    public static Dictionary<int, SavedValue> GetProcAmp(string deviceKey) => GetSection(deviceKey, false);

    /// <summary>读取设备已保存的相机控制；无记录返回 null</summary>
    public static Dictionary<int, SavedValue> GetCameraControl(string deviceKey) => GetSection(deviceKey, true);

    /// <summary>覆写设备已保存的图像属性（需再调用 Save 写盘）</summary>
    public static void SetProcAmp(string deviceKey, Dictionary<int, SavedValue> items) => SetSection(deviceKey, items, false);

    /// <summary>覆写设备已保存的相机控制（需再调用 Save 写盘）</summary>
    public static void SetCameraControl(string deviceKey, Dictionary<int, SavedValue> items) => SetSection(deviceKey, items, true);

    /// <summary>将全部设备设置写入统一文件</summary>
    public static void Save()
    {
        lock (_lock)
        {
            var store = Store; // 确保已加载
            try
            {
                var file = new ProcAmpSettingsFile();
                foreach (var kvp in store)
                {
                    var entry = new DeviceFileEntry { Key = kvp.Key };
                    foreach (var p in kvp.Value.ProcAmp)
                        entry.Params.Add(new ParamFileEntry { Id = p.Key, Value = p.Value.Value, Auto = p.Value.Auto });
                    foreach (var c in kvp.Value.CameraControl)
                        entry.CameraControls.Add(new ParamFileEntry { Id = c.Key, Value = c.Value.Value, Auto = c.Value.Auto });
                    file.Devices.Add(entry);
                }

                var serializer = new XmlSerializer(typeof(ProcAmpSettingsFile));
                using (var fs = File.Create(SettingsFile))
                {
                    serializer.Serialize(fs, file);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"CameraSettingsStore.Save failed: {ex.Message}");
            }
        }
    }

    private static Dictionary<int, SavedValue> GetSection(string deviceKey, bool cameraControl)
    {
        if (string.IsNullOrEmpty(deviceKey)) return null;
        lock (_lock)
        {
            if (!Store.TryGetValue(deviceKey, out var dev)) return null;
            return cameraControl ? dev.CameraControl : dev.ProcAmp;
        }
    }

    private static void SetSection(string deviceKey, Dictionary<int, SavedValue> items, bool cameraControl)
    {
        if (string.IsNullOrEmpty(deviceKey)) return;
        lock (_lock)
        {
            var store = Store;
            if (!store.TryGetValue(deviceKey, out var dev))
            {
                dev = new DeviceData();
                store[deviceKey] = dev;
            }
            var dict = cameraControl ? dev.CameraControl : dev.ProcAmp;
            dict.Clear();
            foreach (var kvp in items) dict[kvp.Key] = kvp.Value;
        }
    }

    private static Dictionary<string, DeviceData> LoadFromFile()
    {
        var store = new Dictionary<string, DeviceData>();
        try
        {
            if (!File.Exists(SettingsFile)) return store;
            var serializer = new XmlSerializer(typeof(ProcAmpSettingsFile));
            using (var fs = File.OpenRead(SettingsFile))
            {
                var file = (ProcAmpSettingsFile)serializer.Deserialize(fs);
                if (file?.Devices == null) return store;
                foreach (var d in file.Devices)
                {
                    if (string.IsNullOrEmpty(d.Key)) continue;
                    var dev = new DeviceData();
                    if (d.Params != null)
                    {
                        foreach (var p in d.Params)
                            dev.ProcAmp[p.Id] = new SavedValue { Value = p.Value, Auto = p.Auto };
                    }
                    if (d.CameraControls != null)
                    {
                        foreach (var c in d.CameraControls)
                            dev.CameraControl[c.Id] = new SavedValue { Value = c.Value, Auto = c.Auto };
                    }
                    store[d.Key] = dev;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"CameraSettingsStore.Load failed: {ex.Message}");
        }
        return store;
    }
}
}
