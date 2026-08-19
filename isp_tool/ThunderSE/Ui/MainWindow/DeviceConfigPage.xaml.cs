using System;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Animation;
using ThunderSE.Common;
using ThunderSE.Device;
using ThunderSE.DeviceConfig;
using ThunderSE.Ui.SettingWindow.Awb;
using ThunderSE.Ui.SettingWindow.Blc;
using ThunderSE.Ui.SettingWindow.Ccm;
using ThunderSE.Ui.SettingWindow.IspSteps;
using ThunderSE.Ui.SettingWindow.Lsc;
using ThunderSE.Ui.SettingWindow.YGamma;
using ThunderSE.Uvc;

namespace ThunderSE.Ui.MainWindow
{
    /// <summary>
    /// DeviceConfigPage.xaml 的交互逻辑
    /// </summary>
    /// 
    public partial class DeviceConfigPage : UserControl
    {
        private BlcWindow _blcWindow;
        private LscWindow _lscWindow;
        private AwbWindow _awbWindow;
        private CcmOnlineIQWindow _cmOnlineIQWindow;
        private YGammaWindow _yGammaWindow;
        private UvcWindow _uvcWindow;
        private DevicePropertyWindow _devicePropertyWindow;

        private DeviceConfigPageViewModel _viewModel = null;

        private IspStepsWindow _ispStepsWindow = null;

        //TODO: 把打开窗口的操作都从ViewModel迁移到View里面吧，轮子哥说ViewModel不能动View

        public ThunderSE.DeviceConfig.Isp.IspModule? CurrentNavigatingModule
        {
            get { return (ThunderSE.DeviceConfig.Isp.IspModule)GetValue(CurrentNavigatingModuleProperty); }
            set
            {
                SetValue(CurrentNavigatingModuleProperty, value);
            }
        }

        public static readonly DependencyProperty CurrentNavigatingModuleProperty = DependencyProperty.Register(
            "CurrentNavigatingModule",
            typeof(ThunderSE.DeviceConfig.Isp.IspModule?),
            typeof(DeviceConfigPage),
            new PropertyMetadata(null, OnCurrentNavigatingModulePropChanged));

        public bool ShowUvcView
        {
            get { return (bool)GetValue(ShowUvcViewProperty); }
            set { SetValue(ShowUvcViewProperty, value); }
        }

        public static readonly DependencyProperty ShowUvcViewProperty =
                DependencyProperty.Register(
                    "ShowUvcView",
                    typeof(bool),
                    typeof(DeviceConfigPage),
                    new PropertyMetadata(false)
                    );

        private static void OnCurrentNavigatingModulePropChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
        {
            var dependencyObject = (DeviceConfigPage)o;
            dependencyObject.BeforeNavigateToModule((ThunderSE.DeviceConfig.Isp.IspModule?)e.NewValue);
        }

        private void BeforeNavigateToModule(ThunderSE.DeviceConfig.Isp.IspModule? moduleToNavigate)
        {
            if (moduleToNavigate != null)
            {
                Window.GetWindow(this).Activate();
            }
        }

        public DeviceConfigPage()
        {
            InitializeComponent();
            Style moduleBorderStyle = (Style)Application.Current.Resources["FadeOutBorderStyle"];

            var beginStoryBoard = new BeginStoryboard();
            beginStoryBoard.Storyboard = (Storyboard)this.Resources["FadeOutAnimationStoryBoard"];

            moduleBorderStyle.Triggers[0].EnterActions.Add(beginStoryBoard);
            var binding = new Binding("CurrentNavigatingModule")
            {
                Mode = BindingMode.TwoWay
            };
            this.SetBinding(CurrentNavigatingModuleProperty, binding);
            UvcView.Initialize();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _viewModel = (DeviceConfigPageViewModel)DataContext;
            if (_viewModel.IsOnline && UvcReceiver.Instance.IsConnected)
            {
                UvcView.ClickCutRawImage += OnCutRaw;

                // 订阅HGRM属性变化以更新画线
                if (_viewModel.HgrmAdapt != null)
                {
                    _viewModel.HgrmAdapt.PropertyChanged += OnHgrmPropertyChanged;
                    UvcView.SetHgrmData(_viewModel.HgrmAdapt);
                }
            }
        }

        private void OnHgrmPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // 当HGRM属性变化时更新画线
            if (e.PropertyName.StartsWith("ae_win_"))
            {
                if (_uvcWindow != null)
                {
                    _uvcWindow.SetHgrmData(_viewModel.HgrmAdapt);
                }
                //UvcView.SetHgrmData(_viewModel.HgrmAdapt);
            }
        }

        private void OnCutRaw()
        {
            var filePathSb = new StringBuilder(512);
            DeviceApi.Ax327XCutRaw(_viewModel.DeviceConfig.DeviceLocation, filePathSb);

            MessageBox.Show("已成功截取，图像保存在：" + filePathSb.ToString(), "", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private int OnUvcPlayStateChange(bool isPlaying)
        {
            if (!isPlaying)
            {
                UvcReceiver.Instance.Connect(_viewModel.DeviceConfig.UvcInterface, 
                    _viewModel.ResolutionWidth, _viewModel.ResolutionHeight);
            }
            return 0;
        }

        private void OpenSetttingWindow(object sender, ExecutedRoutedEventArgs e)
        {
            string settingSection = (string)e.Parameter;
            switch (settingSection)
            {
                case "BlcGrid":
                    if (_blcWindow != null)
                    {
                        _blcWindow.Activate();
                    }
                    else
                    {
                        _blcWindow = new BlcWindow();
                        _blcWindow.DataContext = new BlcWindowViewModel(_viewModel.DeviceConfig.IspProcessor);
                        _blcWindow.Closed += (object window, EventArgs eventArgs) =>
                        {
                            _blcWindow = null;
                        };
                        _blcWindow.Show();
                    }
                    break;

                case "LscGrid":
                    if (_lscWindow != null)
                    {
                        _lscWindow.Activate();
                    }
                    else
                    {
                        _lscWindow = new LscWindow();
                        _lscWindow.DataContext = new LscWindowViewModel(_viewModel.DeviceConfig.IspProcessor);
                        _lscWindow.Closed += (object window, EventArgs eventArgs) =>
                        {
                            _lscWindow = null;
                        };
                        _lscWindow.Show();
                    }
                    break;

                case "AwbGrid":
                    if (_awbWindow != null)
                    {
                        _awbWindow.Activate();
                    }
                    else
                    {
                        _awbWindow = new AwbWindow();
                        _awbWindow.DataContext = new AwbWindowViewModel(_viewModel.DeviceConfig.IspProcessor);
                        _awbWindow.Closed += (object window, EventArgs eventArgs) =>
                        {
                            _awbWindow = null;
                            _viewModel.IsAwbSetWindow = false;
                        };
                        _viewModel.IsAwbSetWindow = true;
                        _awbWindow.Show();
                    }
                    break;
                
                case "CCMGrid":
                    if (_cmOnlineIQWindow != null)
                    {
                        _cmOnlineIQWindow.Activate();
                    }
                    else
                    {
                        _cmOnlineIQWindow = new CcmOnlineIQWindow(_viewModel.DeviceConfig.IspProcessor);
                        //_cmOnlineIQWindow.DataContext = new CcmOnlineIQWindowViewModel(_viewModel.DeviceConfig.IspProcessor);
                        _cmOnlineIQWindow.Closed += (object window, EventArgs eventArgs) =>
                        {
                            _cmOnlineIQWindow = null;
                        };
                        _cmOnlineIQWindow.Show();
                    }
                    break;

                case "YGammaGrid":
                    if (_yGammaWindow != null)
                    {
                        _yGammaWindow.Activate();
                    }
                    else
                    {
                        if (_viewModel != null && _viewModel.SelectedGainLevelValue == 8)
                            return;

                        _yGammaWindow = new YGammaWindow();
                        _yGammaWindow.DataContext = new YGammaWindowViewModel(_viewModel.DeviceConfig.IspProcessor);
                        _yGammaWindow.Closed += (object window, EventArgs eventArgs) =>
                        {
                            _yGammaWindow = null;
                        };
                        _yGammaWindow.Show();
                    }
                    break;

                default:
                    break;
            }
        }

        private void OutputCurrentDateConfig(object sender, ExecutedRoutedEventArgs e)
        {
            string settingSection = (string)e.Parameter;
            var tmpDataArray = "";
            FormatDataWindow arrDataWindow;
            switch (settingSection)
            {
                case "General":
                    tmpDataArray = _viewModel.FormatAllISPDataToC();
                    arrDataWindow = new FormatDataWindow(tmpDataArray);
                    arrDataWindow.ShowDialog();
                    break;

                case "LevelGrid":
                    tmpDataArray = _viewModel.FormatGainLevelDataToC();
                    arrDataWindow = new FormatDataWindow(tmpDataArray);
                    arrDataWindow.ShowDialog();
                    break;

                case "AEGrid":
                    tmpDataArray = _viewModel.FormatAEDataToC();
                    arrDataWindow = new FormatDataWindow(tmpDataArray);
                    arrDataWindow.ShowDialog();
                    break;

                case "BlcGrid":
                    tmpDataArray = _viewModel.FormatBLCDataToC();
                    arrDataWindow = new FormatDataWindow(tmpDataArray);
                    arrDataWindow.ShowDialog();
                    break;

                case "DDCGrid":
                    tmpDataArray = _viewModel.FormatDDCDataToC();
                    arrDataWindow = new FormatDataWindow(tmpDataArray);
                    arrDataWindow.ShowDialog();
                    break;

                case "AwbGrid":
                    tmpDataArray = _viewModel.FormatAWBDataToC();
                    arrDataWindow = new FormatDataWindow(tmpDataArray);
                    arrDataWindow.ShowDialog();
                    break;

                case "CCMGrid":
                    tmpDataArray = _viewModel.FormatCCMDataToC();
                    arrDataWindow = new FormatDataWindow(tmpDataArray);
                    arrDataWindow.ShowDialog();
                    break;

                case "YGammaGrid":
                    tmpDataArray = _viewModel.FormatYGAMMADataToC();
                    arrDataWindow = new FormatDataWindow(tmpDataArray);
                    arrDataWindow.ShowDialog();
                    break;

                case "ChGrid":
                    tmpDataArray = _viewModel.FormatCHDataToC();
                    arrDataWindow = new FormatDataWindow(tmpDataArray);
                    arrDataWindow.ShowDialog();
                    break;

                case "EEGrid":
                    tmpDataArray = _viewModel.FormatEEDataToC();
                    arrDataWindow = new FormatDataWindow(tmpDataArray);
                    arrDataWindow.ShowDialog();
                    break;

                case "SajGrid":
                    tmpDataArray = _viewModel.FormatSAJDataToC();
                    arrDataWindow = new FormatDataWindow(tmpDataArray);
                    arrDataWindow.ShowDialog();
                    break;

                default:
                    break;
            }
        }

        public void ModuleBorderLoaded(object eventSender, EventArgs eventArgs)
        {
            SetValue(CurrentNavigatingModuleProperty, null);
        }

        #region 打开各种参数编辑窗口的事件处理方法
        private void OnClickShowEnables(object sender, RoutedEventArgs e)
        {
            if (_ispStepsWindow != null)
            {
                _ispStepsWindow.Show();
                _ispStepsWindow.Activate();
            }
            else
            {
                _ispStepsWindow = new IspStepsWindow();
                _ispStepsWindow.DataContext = new IspStepsWindowViewModel(_viewModel.DeviceConfig.IspProcessor);
                _ispStepsWindow.Show();
            }
        }

        private void OnClickShowAEExpTag(object sender, RoutedEventArgs e)
        {
            var tmpDataArray = _viewModel.ExpAdapt.exp_tag.Select(x => Convert.ToInt32(x)).ToArray();
            var arrDataWindow = new ArrayDataWindow(tmpDataArray);
            arrDataWindow.ShowDialog();

            if (arrDataWindow.DialogResult.Value == true)
            {
                _viewModel.ExpAdapt.exp_tag = arrDataWindow.ArrayData.Select(x => Convert.ToByte(x)).ToArray();
            }
        }

        private void OnClickShowAEHgrmCentreWeight(object sender, RoutedEventArgs e)
        {
            var tmpDataArray = _viewModel.HgrmAdapt.hgrm_centre_weight.Select(x => Convert.ToInt32(x)).ToArray();
            var arrDataWindow = new ArrayDataWindow(tmpDataArray);
            arrDataWindow.ShowDialog();

            if (arrDataWindow.DialogResult.Value == true)
            {
                _viewModel.HgrmAdapt.hgrm_centre_weight =
                    arrDataWindow.ArrayData.Select(x => Convert.ToInt32(x)).ToArray();
            }
        }

        private void OnClickShowAEHgrmGrayWeight(object sender, RoutedEventArgs e)
        {
            var tmpDataArray = _viewModel.HgrmAdapt.hgrm_gray_weight.Select(x => Convert.ToInt32(x)).ToArray();
            var arrDataWindow = new ArrayDataWindow(tmpDataArray);
            arrDataWindow.ShowDialog();

            if (arrDataWindow.DialogResult.Value == true)
            {
                _viewModel.HgrmAdapt.hgrm_gray_weight =
                    arrDataWindow.ArrayData.Select(x => Convert.ToInt32(x)).ToArray();
            }
        }

        private void OnClickShowDDC_DThRate(object sender, RoutedEventArgs e)
        {
            var tmpDataArray = _viewModel.d_th_rate.Select(x => Convert.ToInt32(x)).ToArray();
            var arrDataWindow = new ArrayDataWindow(tmpDataArray);
            arrDataWindow.ShowDialog();

            if (arrDataWindow.DialogResult.Value == true)
            {
                _viewModel.d_th_rate =
                    arrDataWindow.ArrayData.Select(x => Convert.ToInt32(x)).ToArray();
            }
        }

        private void OnClickShowBLC_Rate(object sender, RoutedEventArgs e)
        {
            var tmpDataArray = _viewModel.Blc_rate.Select(x => Convert.ToInt32(x)).ToArray();
            var arrDataWindow = new ArrayDataWindow(tmpDataArray);
            arrDataWindow.ShowDialog();

            if (arrDataWindow.DialogResult.Value == true)
            {
                _viewModel.Blc_rate =arrDataWindow.ArrayData.Select(x => Convert.ToByte(x)).ToArray();
            }
        }

        private void OnClickShowDDC_HThRate(object sender, RoutedEventArgs e)
        {
            var tmpDataArray = _viewModel.h_th_rate.Select(x => Convert.ToInt32(x)).ToArray();
            var arrDataWindow = new ArrayDataWindow(tmpDataArray);
            arrDataWindow.ShowDialog();

            if (arrDataWindow.DialogResult.Value == true)
            {
                _viewModel.h_th_rate = arrDataWindow.ArrayData.Select(x => Convert.ToInt32(x)).ToArray();
            }
        }

        private void OnClickShowDDCIndxTable(object sender, RoutedEventArgs e)
        {
            var tmpDataArray = _viewModel.indx_table.Select(x => Convert.ToInt32(x)).ToArray();
            var arrDataWindow = new ArrayDataWindow(tmpDataArray, showAsHex: true);
            arrDataWindow.ShowDialog();

            if (arrDataWindow.DialogResult.Value == true)
            {
                _viewModel.indx_table =
                    arrDataWindow.ArrayData.Select(x => Convert.ToUInt32(x)).ToArray();
            }
        }

        private void OnClickShowDDCIndxAdapt(object sender, RoutedEventArgs e)
        {
            var tmpDataArray = _viewModel.indx_adapt.Select(x => Convert.ToInt32(x)).ToArray();
            var arrDataWindow = new ArrayDataWindow(tmpDataArray);
            arrDataWindow.ShowDialog();

            if (arrDataWindow.DialogResult.Value == true)
            {
                _viewModel.indx_adapt =
                    arrDataWindow.ArrayData.Select(x => Convert.ToInt32(x)).ToArray();
            }
        }

        private void OnClickShowDDCStdTh(object sender, RoutedEventArgs e)
        {
            var tmpDataArray = _viewModel.std_th.Select(x => Convert.ToInt32(x)).ToArray();
            var arrDataWindow = new ArrayDataWindow(tmpDataArray);
            arrDataWindow.ShowDialog();

            if (arrDataWindow.DialogResult.Value == true)
            {
                _viewModel.std_th =
                    arrDataWindow.ArrayData.Select(x => Convert.ToInt32(x)).ToArray();
            }
        }

        private void OnClickShowLenShadingCorrectionData(object sender, RoutedEventArgs e)
        {
            var tmpDataArray = _viewModel.LscCorrectionData.Select(x => Convert.ToInt32(x)).ToArray();

            // 根据LSC网格的实际大小计算合适的显示参数
            // 首先获取当前分辨率下的LSC网格大小
            int resolutionWidth = _viewModel.ResolutionWidth;
            int resolutionHeight = _viewModel.ResolutionHeight;

            // 计算LSC网格尺寸
            int blockH = (resolutionHeight / 2 + 32 - 1) / 32 + 1;  // blockSizeY = 32
            int blockW = (resolutionWidth / 2 + 16 - 1) / 16 + 1;   // blockSizeX = 16
            int totalSize = 4 * blockH * blockW;  // 4个Bayer通道

            // 根据网格尺寸确定显示参数
            // 这里使用一个合理的默认值，例如按通道分组显示
            int parts = 4; // 4个通道
            int numberPerLine = blockH; // 每行显示blockW个数据，即一行代表一个通道的一行网格

            var arrDataWindow = new ArrayDataWindow(tmpDataArray, parts, numberPerLine, false, true);
            arrDataWindow.ShowDialog();

            if (arrDataWindow.DialogResult.Value == true)
            {
                _viewModel.LscCorrectionData = arrDataWindow.ArrayData.Select(x => Convert.ToInt16(x)).ToArray();
            }
        }

        private void OnClickShowAwbStatTab(object sender, RoutedEventArgs e)
        {
            var tmpDataArray = _viewModel.Awb_Stat_Tab.Select(x => Convert.ToInt32(x)).ToArray();
            var arrDataWindow = new ArrayDataWindow(tmpDataArray,4,1,false,false);
            arrDataWindow.ShowDialog();

            if (arrDataWindow.DialogResult.Value == true)
            {
                _viewModel.Awb_Stat_Tab = arrDataWindow.ArrayData.Select(x => Convert.ToByte(x)).ToArray();
            }
        }

        private void OnClickShowAwbCbTh(object sender, RoutedEventArgs e)
        {
            var arrDataWindow = new ArrayDataWindow(_viewModel.Awb_Cb_Th);
            arrDataWindow.ShowDialog();

            if (arrDataWindow.DialogResult.Value == true)
            {
                _viewModel.Awb_Cb_Th = arrDataWindow.ArrayData;
            }
        }

        private void OnClickShowAwbCrTh(object sender, RoutedEventArgs e)
        {
            var arrDataWindow = new ArrayDataWindow(_viewModel.Awb_Cr_Th);
            arrDataWindow.ShowDialog();

            if (arrDataWindow.DialogResult.Value == true)
            {
                _viewModel.Awb_Cr_Th = arrDataWindow.ArrayData;
            }
        }

        private void OnClickShowAwbCbcrTh(object sender, RoutedEventArgs e)
        {
            var arrDataWindow = new ArrayDataWindow(_viewModel.Awb_Cbcr_Th);
            arrDataWindow.ShowDialog();

            if (arrDataWindow.DialogResult.Value == true)
            {
                _viewModel.Awb_Cbcr_Th = arrDataWindow.ArrayData;
            }
        }

        private void OnClickShowCCM(object sender, RoutedEventArgs e)
        {
            var tmpDataArray = _viewModel.ccm.Select(x => Convert.ToInt32(x)).ToArray();
            var arrDataWindow = new ArrayDataWindow(tmpDataArray, 3, 1, false, false);
            arrDataWindow.ShowDialog();

            if (arrDataWindow.DialogResult.Value == true)
            {
                _viewModel.ccm = arrDataWindow.ArrayData.Select(x => Convert.ToInt16(x)).ToArray();
            }
        }

        private void OnClickShowYGammaTable(object sender, RoutedEventArgs e)
        {
            var tmpDataArray = _viewModel.YGammaNum.Select(x => Convert.ToInt32(x)).ToArray();
            var arrDataWindow = new ArrayDataWindow(tmpDataArray);
            arrDataWindow.ShowDialog();

            if (arrDataWindow.DialogResult.Value == true)
            {
                _viewModel.YGammaNum = arrDataWindow.ArrayData.Select(x => Convert.ToByte(x)).ToArray();
            }
        }

        private void OnClickShowYGammaLowRate(object sender, RoutedEventArgs e)
        {
            var tmpDataArray = _viewModel.YGammaYLowRate.Select(x => Convert.ToInt32(x)).ToArray();
            var arrDataWindow = new ArrayDataWindow(tmpDataArray);
            arrDataWindow.ShowDialog();

            if (arrDataWindow.DialogResult.Value == true)
            {
                _viewModel.YGammaYLowRate = arrDataWindow.ArrayData.Select(x => Convert.ToByte(x)).ToArray();
            }
        }

        private void OnClickShowYGammaHighRate(object sender, RoutedEventArgs e)
        {
            var tmpDataArray = _viewModel.YGammaYHighRate.Select(x => Convert.ToInt32(x)).ToArray();
            var arrDataWindow = new ArrayDataWindow(tmpDataArray);
            arrDataWindow.ShowDialog();

            if (arrDataWindow.DialogResult.Value == true)
            {
                _viewModel.YGammaYHighRate = arrDataWindow.ArrayData.Select(x => Convert.ToByte(x)).ToArray();
            }
        }

        private void OnClickShowYGammaRate(object sender, RoutedEventArgs e)
        {
            var tmpDataArray = _viewModel.YGammaRate.Select(x => Convert.ToInt32(x)).ToArray();
            var arrDataWindow = new ArrayDataWindow(tmpDataArray);
            arrDataWindow.ShowDialog();

            if (arrDataWindow.DialogResult.Value == true)
            {
                _viewModel.YGammaRate = arrDataWindow.ArrayData.Select(x => Convert.ToByte(x)).ToArray();
            }
        }

        private void OnClickShowEEDnslope(object sender, RoutedEventArgs e)
        {
            var tmpDataArray = _viewModel.ee_dn_slope.Select(x => Convert.ToInt32(x)).ToArray();
            var arrDataWindow = new ArrayDataWindow(tmpDataArray);
            arrDataWindow.ShowDialog();

            if (arrDataWindow.DialogResult.Value == true)
            {
                _viewModel.ee_dn_slope = arrDataWindow.ArrayData.Select(x => Convert.ToByte(x)).ToArray();
            }
        }

        private void OnClickShowSAJSat(object sender, RoutedEventArgs e)
        {
            var tmpDataArray = _viewModel.Saj_sat.Select(x => Convert.ToInt32(x)).ToArray();
            var arrDataWindow = new ArrayDataWindow(tmpDataArray);
            arrDataWindow.ShowDialog();

            if (arrDataWindow.DialogResult.Value == true)
            {
                _viewModel.Saj_sat = arrDataWindow.ArrayData.Select(x => Convert.ToByte(x)).ToArray();
            }
        }

        private void OnClickShowSAJSatRate(object sender, RoutedEventArgs e)
        {
            var tmpDataArray = _viewModel.Saj_rate.Select(x => Convert.ToInt32(x)).ToArray();
            var arrDataWindow = new ArrayDataWindow(tmpDataArray);
            arrDataWindow.ShowDialog();

            if (arrDataWindow.DialogResult.Value == true)
            {
                _viewModel.Saj_rate = arrDataWindow.ArrayData.Select(x => Convert.ToByte(x)).ToArray();
            }
        }

        private void OnClickShowEESharpSlope(object sender, RoutedEventArgs e)
        {
            var tmpDataArray = _viewModel.ee_sharp_slope.Select(x => Convert.ToInt32(x)).ToArray();
            var arrDataWindow = new ArrayDataWindow(tmpDataArray);
            arrDataWindow.ShowDialog();

            if (arrDataWindow.DialogResult.Value == true)
            {
                _viewModel.ee_sharp_slope = arrDataWindow.ArrayData.Select(x => Convert.ToByte(x)).ToArray();
            }
        }

        private void OnClickShowEEThAdp(object sender, RoutedEventArgs e)
        {
            var tmpDataArray = _viewModel.ee_th_adp.Select(x => Convert.ToInt32(x)).ToArray();
            var arrDataWindow = new ArrayDataWindow(tmpDataArray);
            arrDataWindow.ShowDialog();

            if (arrDataWindow.DialogResult.Value == true)
            {
                _viewModel.ee_th_adp = arrDataWindow.ArrayData.Select(x => Convert.ToByte(x)).ToArray();
            }
        }

        private void OnClickShowEEDnTh(object sender, RoutedEventArgs e)
        {
            var tmpDataArray = _viewModel.ee_dn_th.Select(x => Convert.ToInt32(x)).ToArray();
            var arrDataWindow = new ArrayDataWindow(tmpDataArray);
            arrDataWindow.ShowDialog();

            if (arrDataWindow.DialogResult.Value == true)
            {
                _viewModel.ee_dn_th = arrDataWindow.ArrayData.Select(x => Convert.ToByte(x)).ToArray();
            }
        }

        private void OnClickShowEESharpClass(object sender, RoutedEventArgs e)
        {
            var tmpDataArray = _viewModel.sharp_class.Select(x => Convert.ToInt32(x)).ToArray();
            var arrDataWindow = new ArrayDataWindow(tmpDataArray);
            arrDataWindow.ShowDialog();

            if (arrDataWindow.DialogResult.Value == true)
            {
                _viewModel.sharp_class = arrDataWindow.ArrayData.Select(x => Convert.ToByte(x)).ToArray();
            }
        }

        private void OnClickShowDnClass(object sender, RoutedEventArgs e)
        {
            var tmpDataArray = _viewModel.dn_class.Select(x => Convert.ToInt32(x)).ToArray();
            var arrDataWindow = new ArrayDataWindow(tmpDataArray);
            arrDataWindow.ShowDialog();

            if (arrDataWindow.DialogResult.Value == true)
            {
                _viewModel.dn_class = arrDataWindow.ArrayData.Select(x => Convert.ToByte(x)).ToArray();
            }
        }

        private void OnClickShowChEnhence(object sender, RoutedEventArgs e)
        {
            var tmpDataArray = _viewModel.enhence.Select(x => Convert.ToInt32(x)).ToArray();
            var arrDataWindow = new ArrayDataWindow(tmpDataArray);
            arrDataWindow.ShowDialog();

            if (arrDataWindow.DialogResult.Value == true)
            {
                _viewModel.enhence = arrDataWindow.ArrayData.Select(x => Convert.ToInt32(x)).ToArray();
            }
        }

        private void OnClickShowChTh1(object sender, RoutedEventArgs e)
        {
            var tmpDataArray = _viewModel.th1.Select(x => Convert.ToInt32(x)).ToArray();
            var arrDataWindow = new ArrayDataWindow(tmpDataArray);
            arrDataWindow.ShowDialog();

            if (arrDataWindow.DialogResult.Value == true)
            {
                _viewModel.th1 = arrDataWindow.ArrayData.Select(x => Convert.ToInt32(x)).ToArray();
            }
        }

        private void OnClickShowChTh0(object sender, RoutedEventArgs e)
        {
            var tmpDataArray = _viewModel.th0.Select(x => Convert.ToInt32(x)).ToArray();
            var arrDataWindow = new ArrayDataWindow(tmpDataArray);
            arrDataWindow.ShowDialog();

            if (arrDataWindow.DialogResult.Value == true)
            {
                _viewModel.th0 = arrDataWindow.ArrayData.Select(x => Convert.ToInt32(x)).ToArray();
            }
        }

        private void OnClickShowChRRate(object sender, RoutedEventArgs e)
        {
            // 优化数据提取和合并过程
            var rDataArray = _viewModel.r_rate.Select(x => Convert.ToInt32(x)).ToArray();
            var gDataArray = _viewModel.g_rate.Select(x => Convert.ToInt32(x)).ToArray(); // 修正变量名：应该是g_rate而不是b_rate
            var bDataArray = _viewModel.b_rate.Select(x => Convert.ToInt32(x)).ToArray(); // 修正变量名：应该是b_rate而不是g_rate

            // 使用更高效的方式合并数组
            var tmpDataArray = new int[rDataArray.Length + gDataArray.Length + bDataArray.Length];

            // 使用Buffer.BlockCopy进行高效内存复制（如果数据类型相同）
            Array.Copy(rDataArray, 0, tmpDataArray, 0, rDataArray.Length);
            Array.Copy(gDataArray, 0, tmpDataArray, rDataArray.Length, gDataArray.Length);
            Array.Copy(bDataArray, 0, tmpDataArray, rDataArray.Length + gDataArray.Length, bDataArray.Length);

            var arrDataWindow = new ArrayDataWindow(tmpDataArray, 3, 1, false, false);
            arrDataWindow.ShowDialog();

            if (arrDataWindow.DialogResult.HasValue && arrDataWindow.DialogResult.Value)
            {
                // 优化数据回写，减少重复的Select操作
                var updatedData = arrDataWindow.ArrayData.Select(x => Convert.ToInt32(x)).ToArray();

                // 将合并的数据按原长度分割回各个数组
                int rLength = rDataArray.Length;
                int gLength = gDataArray.Length;

                _viewModel.r_rate = new ArraySegment<int>(updatedData, 0, rLength).ToArray();
                _viewModel.g_rate = new ArraySegment<int>(updatedData, rLength, gLength).ToArray();
                _viewModel.b_rate = new ArraySegment<int>(updatedData, rLength + gLength, updatedData.Length - rLength - gLength).ToArray();
            }
        }

        private void OnClickShowChBRate(object sender, RoutedEventArgs e)
        {
            var tmpDataArray = _viewModel.b_rate.Select(x => Convert.ToInt32(x)).ToArray();
            var arrDataWindow = new ArrayDataWindow(tmpDataArray);
            arrDataWindow.ShowDialog();

            if (arrDataWindow.DialogResult.Value == true)
            {
                //_viewModel.b_rate = arrDataWindow.ArrayData.Select(x => Convert.ToInt32(x)).ToArray();
            }
        }

        private void OnClickShowChGRate(object sender, RoutedEventArgs e)
        {
            var tmpDataArray = _viewModel.g_rate.Select(x => Convert.ToInt32(x)).ToArray();
            var arrDataWindow = new ArrayDataWindow(tmpDataArray);
            arrDataWindow.ShowDialog();

            if (arrDataWindow.DialogResult.Value == true)
            {
                //_viewModel.g_rate = arrDataWindow.ArrayData.Select(x => Convert.ToInt32(x)).ToArray();
            }
        }

        private void OnClickShowChSat(object sender, RoutedEventArgs e)
        {
            var tmpDataArray = _viewModel.Ch_sat.Select(x => Convert.ToInt32(x)).ToArray();
            var arrDataWindow = new ArrayDataWindow(tmpDataArray);
            arrDataWindow.ShowDialog();

            if (arrDataWindow.DialogResult.Value == true)
            {
                _viewModel.Ch_sat = arrDataWindow.ArrayData.Select(x => Convert.ToInt32(x)).ToArray();
            }
        }

        private void OnClickShowChRate(object sender, RoutedEventArgs e)
        {
            var tmpDataArray = _viewModel.rate.Select(x => Convert.ToInt32(x)).ToArray();
            var arrDataWindow = new ArrayDataWindow(tmpDataArray);
            arrDataWindow.ShowDialog();

            if (arrDataWindow.DialogResult.Value == true)
            {
                _viewModel.rate = arrDataWindow.ArrayData.Select(x => Convert.ToInt32(x)).ToArray();
            }
        }

        private void OnClickCloseTab(object sender, RoutedEventArgs e)
        {
            //TODO: 要提示保存啊
            //      另外还要关掉子窗口

            ConfigManager.GetInstance().RemoveConfig(_viewModel.DeviceConfig.Name);
        }

        private void OnClickShowUvcView(object sender, RoutedEventArgs e)
        {
            ShowUvcView = true;
        }

        private void OnClickHideUvcView(object sender, RoutedEventArgs e)
        {
            ShowUvcView = false;
        }

        private void OnClickShowUvcInNewWindow(object sender, RoutedEventArgs e)
        {
            if (_uvcWindow == null)
            {
                _uvcWindow = new UvcWindow();
                _uvcWindow.ClickCutRawImage += OnCutRaw;

                // 传递HGRM数据给新窗口，使其显示画线
                if (_viewModel?.HgrmAdapt != null)
                {
                    _uvcWindow.SetHgrmData(_viewModel.HgrmAdapt);
                }

                // 【关键修复】订阅Closed事件，确保窗口关闭后清理引用
                _uvcWindow.Closed += (s, args) =>
                {
                    _uvcWindow.ClickCutRawImage -= OnCutRaw;
                    _uvcWindow = null;
                };
            }
            _uvcWindow.Show();
        }

        /// <summary>
        /// 打开设备属性设置窗口（图像参数 ProcAmp + 相机控制 Camera Control）。
        /// 设备描述符优先取当前已连接设备，其次回退到配置中的 UVC 接口。
        /// </summary>
        private void OpenDevicePropertyWindow(object sender, ExecutedRoutedEventArgs e)
        {
            try
            {
                if (_devicePropertyWindow != null)
                {
                    _devicePropertyWindow.Activate();
                    return;
                }

                string descriptor = UvcReceiver.Instance.CurrentDeviceDescriptor;
                if (string.IsNullOrEmpty(descriptor))
                    descriptor = _viewModel?.DeviceConfig?.UvcInterface;

                _devicePropertyWindow = new DevicePropertyWindow(descriptor);
                _devicePropertyWindow.Owner = Window.GetWindow(this);
                _devicePropertyWindow.Closed += (s, args) => _devicePropertyWindow = null;
                _devicePropertyWindow.Show();
            }
            catch (Exception ex)
            {
                Logger.Error($"Open DevicePropertyWindow failed: {ex.Message}");
            }
        }

        private void OnClickShowExpGainSettingWindow(object sender, RoutedEventArgs e)
        {
            var expGainSettingWindow = new ExpGainWindow();
            expGainSettingWindow.DataContext = new ExpGainWindowViewModel(_viewModel.DeviceConfig);
            expGainSettingWindow.ShowDialog();
        }

        // IIC输入验证方法（支持0-65535完整16位）
        private void OnIICInputPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                string proposedText = textBox.Text.Remove(textBox.SelectionStart, textBox.SelectionLength)
                                       .Insert(textBox.CaretIndex, e.Text);

                if (!string.IsNullOrEmpty(proposedText))
                {
                    if (int.TryParse(proposedText, out int value))
                    {
                        if (value < 0 || value > 65535)
                        {
                            e.Handled = true;
                            MessageBox.Show("请输入0-65535范围内的数值 (0x0000-0xFFFF)", "输入范围错误",
                                          MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                    }
                    else if (!string.IsNullOrEmpty(proposedText.Trim()))
                    {
                        e.Handled = true;
                    }
                }
            }
        }


        // 打开IIC配置窗口
        private void OnOpenIICConfigClick(object sender, RoutedEventArgs e)
        {
            IICConfigWindow iicConfigWindow = new IICConfigWindow(_viewModel);
            iicConfigWindow.Owner = Application.Current.MainWindow;
            iicConfigWindow.Show();
        }

        #endregion

        #region 一些需要在View层处理的UI事件，比如键盘事件等
        // 通用的键盘事件处理方法，用于处理TextBox中的回车键
        private void OnTextBoxKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (sender is TextBox textBox)
                {
                    // 强制更新绑定源，确保输入的值立即反映到ViewModel
                    //    var bindingExpression = textBox.GetBindingExpression(TextBox.TextProperty);
                    //    if (bindingExpression != null)
                    //    {
                    //        bindingExpression.UpdateSource();
                    //    }

                    //    // 移动到下一个焦点元素，这将触发当前控件的失去焦点事件
                    //    textBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                    // 获取当前绑定表达式
                    var bindingExpression = textBox.GetBindingExpression(TextBox.TextProperty);
                    if (bindingExpression != null)
                    {
                        // 获取绑定表达式的当前值
                        object sourceValue = bindingExpression.DataItem;
                        string propertyPath = bindingExpression.ParentBinding.Path.Path;

                        // 获取当前属性的值
                        object currentValue = GetPropertyValue(sourceValue, propertyPath);
                        string currentValueAsString = currentValue?.ToString() ?? "";

                        // 获取文本框的当前文本
                        string textboxValue = textBox.Text;

                        // 比较当前文本框值与绑定源值（进行适当的类型转换比较）
                        if (AreValuesEqual(textboxValue, currentValueAsString))
                        {
                            // 值没有变化，直接移动焦点
                            textBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                        }
                        else
                        {
                            // 值已改变，更新绑定源
                            bindingExpression.UpdateSource();
                            textBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                        }
                    }
                    else
                    {
                        // 没有绑定的情况下，移动焦点
                        textBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                    }
                }
            }
        }

        // 辅助方法：比较两个值是否相等（考虑类型转换）
        private bool AreValuesEqual(string textboxValue, string boundValue)
        {
            // 处理空值情况
            if (string.IsNullOrEmpty(textboxValue) && string.IsNullOrEmpty(boundValue))
                return true;
            if (string.IsNullOrEmpty(textboxValue) || string.IsNullOrEmpty(boundValue))
                return false;

            // 尝试将两个值作为数字进行比较，以处理数字类型的属性
            if (double.TryParse(textboxValue, out double textNum) &&
                double.TryParse(boundValue, out double boundNum))
            {
                return Math.Abs(textNum - boundNum) < double.Epsilon;
            }

            // 字符串比较（忽略前后空白）
            return textboxValue.Trim() == boundValue.Trim();
        }

        // 辅助方法：通过属性路径获取属性值
        private object GetPropertyValue(object source, string propertyPath)
        {
            if (source == null) return null;

            var properties = propertyPath.Split('.');
            object currentValue = source;

            foreach (string property in properties)
            {
                var propInfo = currentValue.GetType().GetProperty(property);
                if (propInfo == null) return null;
                currentValue = propInfo.GetValue(currentValue);
            }

            return currentValue;
        }

        #endregion

    }
}
