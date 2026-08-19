using System;
using System.Collections.ObjectModel;
using GalaSoft.MvvmLight;
using ThunderSE.Common;

namespace ThunderSE.Uvc
{
    /// <summary>
    /// 摄像头相机控制（Camera Control / IAMCameraControl）控制器。
    /// 包装 uvc.dll 的原生接口，向 UI 暴露可调参数集合，
    /// 并按设备持久化上次设置的参数值（平移/俯仰/变焦/曝光/对焦等）。
    /// 与图像属性(VideoProcAmp)共用同一原生初始化：InitProcAmp 会一并初始化两套控制，
    /// 因此本控制器不重复初始化，仅在必要时（原生层尚未初始化）补齐一次 InitProcAmp。
    /// </summary>
    public sealed class CameraControlController
    {
        private static readonly Lazy<CameraControlController> _instance =
            new Lazy<CameraControlController>(() => new CameraControlController());

        public static CameraControlController Instance => _instance.Value;

        /// <summary>当前设备支持的相机控制参数集合（绑定到 UI）</summary>
        public ObservableCollection<CameraControlParamViewModel> Parameters { get; }

        /// <summary>是否已成功初始化并发现可调参数</summary>
        public bool IsAvailable { get; private set; }

        /// <summary>最后一次初始化的错误信息（不可用时的提示）</summary>
        public string LastError { get; private set; }

        /// <summary>当前正在控制的设备描述符</summary>
        private string _currentDeviceKey = "";

        private CameraControlController()
        {
            Parameters = new ObservableCollection<CameraControlParamViewModel>();
            IsAvailable = false;
            LastError = "";
        }

        /// <summary>
        /// 根据设备描述符（如 "video=USB Camera"）初始化相机控制。
        /// 应在设备连接成功后调用。原生层 InitProcAmp 会一并初始化相机控制，
        /// 此处仅在原生层尚未初始化时补齐一次 InitProcAmp，随后读取相机控制数据。
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
                // 原生层 InitProcAmp 同时初始化图像属性 + 相机控制；尚未初始化时补齐一次
                if (UvcApi.GetProcAmpCount() == 0 && UvcApi.GetCameraControlCount() == 0)
                {
                    int initCount = UvcApi.InitProcAmp(deviceDescriptor);
                    if (initCount < 0)
                    {
                        int hr = 0;
                        try { hr = UvcApi.GetLastProcAmpError(); }
                        catch (Exception ex) { Logger.Warn($"GetLastProcAmpError unavailable: {ex.Message}"); }

                        IsAvailable = false;
                        LastError = hr != 0
                            ? $"无法访问摄像头相机控制 (错误码 {initCount}, HRESULT=0x{hr:X8})"
                            : $"无法访问摄像头相机控制 (错误码 {initCount})";
                        Logger.Warn($"CameraControl init via InitProcAmp failed for '{deviceDescriptor}', code={initCount}");
                        return;
                    }
                }

                int count = UvcApi.GetCameraControlCount();
                if (count == 0)
                {
                    IsAvailable = false;
                    LastError = "该摄像头不支持任何相机控制";
                    Logger.Info($"CameraControl: device '{deviceDescriptor}' supports 0 controls");
                    return;
                }

                for (int i = 0; i < count; i++)
                {
                    var info = new UvcApi.ProcAmpParamInfo();
                    if (UvcApi.GetCameraControlInfo(i, ref info) == 0)
                    {
                        Parameters.Add(new CameraControlParamViewModel(info));
                    }
                }

                // 面板数据直接从设备获取显示，不加载持久化设置覆盖。
                // 与图像属性共用 ProcAmpSettings.xml；仅用于保存记录（Save()），不作为初始化数据源。
                IsAvailable = true;
                LastError = "";
                Logger.Info($"CameraControl initialized: {Parameters.Count} controls for '{deviceDescriptor}'");
            }
            catch (Exception ex)
            {
                IsAvailable = false;
                LastError = $"初始化异常: {ex.Message}";
                Logger.Error($"CameraControl initialize exception: {ex.Message}");
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
            Logger.Info("CameraControl: all parameters reset to default");
        }

        /// <summary>
        /// 保存当前设备的参数设置到文件（设备断开/窗口关闭时调用）。
        /// </summary>
        public void Save()
        {
            if (string.IsNullOrEmpty(_currentDeviceKey) || Parameters.Count == 0) return;

            try
            {
                var items = new System.Collections.Generic.Dictionary<int, CameraSettingsStore.SavedValue>();
                foreach (var vm in Parameters)
                {
                    if (vm.SupportsManual || vm.SupportsAuto)
                    {
                        // 只持久化设备已确认接受的数值（AppliedValue），
                        // 避免把写入失败（如只读/仿真 UVC 设备）的无效值保存并在下次连接时错误显示。
                        items[vm.PropertyId] = new CameraSettingsStore.SavedValue { Value = vm.AppliedValue, Auto = vm.IsAuto };
                    }
                }
                CameraSettingsStore.SetCameraControl(_currentDeviceKey, items);
                CameraSettingsStore.Save();
            }
            catch (Exception ex)
            {
                Logger.Error($"CameraControl Save failed: {ex.Message}");
            }
        }

        /// <summary>
        /// 从持久化文件加载当前设备的已保存设置并应用到设备。
        /// 仅由"恢复"按钮触发，不自动调用。
        /// </summary>
        public void LoadFromFile()
        {
            if (string.IsNullOrEmpty(_currentDeviceKey)) return;
            var saved = CameraSettingsStore.GetCameraControl(_currentDeviceKey);
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
                    Logger.Error($"CameraControl apply saved '{vm.DisplayName}' failed: {ex.Message}");
                }
            }
            Logger.Info("CameraControl: settings loaded from file");
        }

        /// <summary>
        /// 释放相机控制资源（设备断开/窗口关闭时调用）。不再自动持久化，
        /// 需用户点击"保存"按钮才写入文件。
        /// 注意：原生层由 ReleaseProcAmp 统一释放，此处仅清理托管状态。
        /// </summary>
        public void Release()
        {
            if (Parameters.Count > 0) Parameters.Clear();
            _currentDeviceKey = "";
            IsAvailable = false;
            LastError = "";
        }

        }

    /// <summary>
    /// 单个相机控制参数视图模型（平移/俯仰/变焦/曝光等），直接绑定到滑块与复选框。
    /// </summary>
    public class CameraControlParamViewModel : ViewModelBase
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
                // 先写设备，成功后仅当设备确认接受才提交界面状态；
                // 失败（只读/仿真设备）时保持原状态，避免勾选一个从未生效的“自动”模式。
                if (!ApplyAuto(value)) return;
                _isAuto = value;
                RaisePropertyChanged();
                RaisePropertyChanged("IsSliderEnabled");
            }
        }

        /// <summary>滑块是否可用：支持手动且当前非自动模式</summary>
        public bool IsSliderEnabled => SupportsManual && !_isAuto;

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

        public CameraControlParamViewModel(UvcApi.ProcAmpParamInfo info)
        {
            PropertyId = info.PropertyId;
            DisplayName = GetDisplayName((UvcApi.CameraControlProperty)info.PropertyId);
            Min = info.MinVal;
            Max = info.MaxVal;
            Step = info.StepVal <= 0 ? 1 : info.StepVal;
            Default = info.DefaultVal;
            _value = info.CurrentVal;
            _appliedValue = info.CurrentVal; // 设备当前值即已确认接受的初始值

            // VideoProcAmp_Flags / CameraControl_Flags: 0x1=Auto, 0x2=Manual
            SupportsAuto = (info.Flags & 0x1) != 0;
            SupportsManual = (info.Flags & 0x2) != 0;

            // 若当前为自动模式，或仅支持自动，则视为自动
            _isAuto = SupportsAuto && ((info.Flags & 0x1) != 0 || !SupportsManual);
            if (SupportsAuto && !SupportsManual) _isAuto = true;
        }

        private void ApplyManual(int v)
        {
            try
            {
                int ret = UvcApi.SetCameraControlValue(PropertyId, v, 0);
                if (ret < 0)
                {
                    Logger.Warn($"SetCameraControl {DisplayName} = {v} failed (code {ret})");
                    // 写入失败（只读/仿真 UVC 设备）：界面回滚为设备已确认接受的数值，
                    // 避免滑块/徽章停留在从未生效的假值上。
                    RestoreToApplied();
                }
                else
                {
                    _appliedValue = v; // 设备已确认接受，记录为可持久化数值
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"SetCameraControl {DisplayName} exception: {ex.Message}");
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
        }

        /// <summary>
        /// 切换自动模式。返回设备是否确认接受；失败时调用方不应提交界面状态。
        /// </summary>
        private bool ApplyAuto(bool auto)
        {
            try
            {
                int ret = UvcApi.SetCameraControlValue(PropertyId, _value, auto ? 1 : 0);
                if (ret < 0)
                {
                    Logger.Warn($"SetCameraControl {DisplayName} auto={auto} failed (code {ret})");
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"SetCameraControl {DisplayName} auto exception: {ex.Message}");
                return false;
            }
        }

        public static string GetDisplayName(UvcApi.CameraControlProperty p)
        {
            switch (p)
            {
                case UvcApi.CameraControlProperty.Pan: return "水平平移 (Pan)";
                case UvcApi.CameraControlProperty.Tilt: return "垂直俯仰 (Tilt)";
                case UvcApi.CameraControlProperty.Roll: return "翻滚 (Roll)";
                case UvcApi.CameraControlProperty.Zoom: return "变焦 (Zoom)";
                case UvcApi.CameraControlProperty.Exposure: return "曝光 (Exposure)";
                case UvcApi.CameraControlProperty.Iris: return "光圈 (Iris)";
                case UvcApi.CameraControlProperty.Focus: return "对焦 (Focus)";
                case UvcApi.CameraControlProperty.ScanMode: return "扫描模式";
                case UvcApi.CameraControlProperty.Privacy: return "隐私遮挡";
                case UvcApi.CameraControlProperty.PanTilt: return "云台 (PanTilt)";
                case UvcApi.CameraControlProperty.PanRelative: return "平移(相对)";
                case UvcApi.CameraControlProperty.TiltRelative: return "俯仰(相对)";
                case UvcApi.CameraControlProperty.RollRelative: return "翻滚(相对)";
                case UvcApi.CameraControlProperty.ZoomRelative: return "变焦(相对)";
                case UvcApi.CameraControlProperty.ExposureRelative: return "曝光(相对)";
                case UvcApi.CameraControlProperty.IrisRelative: return "光圈(相对)";
                case UvcApi.CameraControlProperty.FocusRelative: return "对焦(相对)";
                case UvcApi.CameraControlProperty.PanTiltRelative: return "云台(相对)";
                case UvcApi.CameraControlProperty.FocalLength: return "焦距 (FocalLength)";
                case UvcApi.CameraControlProperty.AutoExposurePriority: return "自动曝光优先";
                default: return "属性" + (int)p;
            }
        }
    }
}
