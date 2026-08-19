using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace ThunderSE.Ui.MainWindow
{
    /// <summary>
    /// CreateConfigDialog.xaml µÄ½»»¥Âß¼­
    /// </summary>
    public partial class CreateConfigWindow : Window
    {
        public string ConfigName
        {
            get;
            set;
        }

        public CreateConfigWindow()
        {
            InitializeComponent();
        }

        private void OnClickConfirm(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void OnClickCancel(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
