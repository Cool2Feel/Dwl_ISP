using GalaSoft.MvvmLight;
using System;
using System.Collections.Generic;
using ThunderSE.DeviceConfig.Isp;
using ThunderSE.Ui.CommonCustomControl;

namespace ThunderSE.Ui.SettingWindow.Awb
{
    class ColorblockPickingWindowViewModel : ViewModelBase
    {
        //private RelayCommand _okCommand;
        //private RelayCommand _addImageCommand;

        //private List<byte[]> _rawImageBufferList = new List<byte[]>();
        //private List<RubberBandData[]> _rubberBandDataList = new List<RubberBandData[]>();

        //private AutoWhiteBalance _whiteBalanceData = new AutoWhiteBalance();

        //public Action CloseAction { get; set; }


        public ColorblockPickingWindowViewModel()
        {
            //_okCommand = new RelayCommand(Ok);
        }

        //public RelayCommand OkCommand
        //{
        //    get { return _okCommand; }
        //}

        //public RelayCommand AddImageCommand
        //{
        //    get { return _addImageCommand; }
        //}

        //private void Ok()
        //{
        //    var correctionData = new Dictionary<int, int>(); ;
        //    for (int i = 0; i < _rawImageBufferList.Count; i++)
        //    {
        //        int[] XArray = new int[6];
        //        int[] YArray = new int[6];
        //        int[] HeightArray = new int[6];
        //        int[] WidthArray = new int[6];

        //        for (int j = 0; j < 6; j++)
        //        {
        //            XArray[j] = _rubberBandDataList[i][j].x;
        //            YArray[j] = _rubberBandDataList[i][j].y;
        //            HeightArray[j] = _rubberBandDataList[i][j].height;
        //            WidthArray[j] = _rubberBandDataList[i][j].width;
        //        }

        //        int bgain = 0;
        //        int rgain = 0;

        //        byte[] tmpBuffer = _rawImageBufferList[i];
        //        //BlackLevel.ApplyBlackLevelCorrection(ref tmpBuffer);
        //        IspApi.AWBCal(tmpBuffer, 1280, 720, Bayer, XArray, YArray, WidthArray, HeightArray, ref bgain, ref rgain);
        //        correctionData[rgain] = bgain / 4;
        //    }
        //    _whiteBalanceData.CorrectionData = correctionData;

        //    CloseAction();
        //}

        //private void AddImages()
        //{
        //    Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog();
        //    openFileDialog.Multiselect = true;
        //    openFileDialog.Filter = "RawÎÄ¼þ(*.raw) | *.raw";
        //    if (!(bool)openFileDialog.ShowDialog())
        //    {
        //        return;
        //    }


        //    foreach (var rawImgPath in rawImgs)
        //    {
        //        if (!File.Exists(rawImgPath))
        //        {
        //            continue;
        //        }

        //        string fileName = System.IO.Path.GetFileName(rawImgPath);
        //        string tabName = System.IO.Path.GetFileNameWithoutExtension(rawImgPath);

        //        TabItem tabItem = new TabItem();
        //        TextBlock tabItemHeaderText = new TextBlock();
        //        tabItemHeaderText.Width = 80;
        //        tabItemHeaderText.TextTrimming = TextTrimming.CharacterEllipsis;
        //        tabItemHeaderText.ToolTip = fileName;
        //        tabItemHeaderText.Text = tabName;

        //        tabItem.Header = tabItemHeaderText;

        //        byte[] rawImageBuffer = File.ReadAllBytes(fileName);
        //        _rawImageBufferList.Add(rawImageBuffer);
        //        var imgControl = new RawImgDisplayControl(rawImageBuffer, Bayer);
        //        var rubberBandData = new RubberBandData[6];
        //        _rubberBandDataList.Add(rubberBandData);

        //        imgControl.DataContext = rubberBandData;

        //        tabItem.Content = imgControl;

        //        RawImgsTab.Items.Add(tabItem);
        //        RawImgsTab.SelectedIndex = 0;
        //    }
        //}
    }
}
