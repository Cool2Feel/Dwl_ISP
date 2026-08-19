using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using ThunderSE.Device;
using ThunderSE.DeviceConfig;
using ThunderSE.DeviceConfig.Isp;
using ThunderSE.Common;

namespace ThunderSE.Ui.MainWindow
{
    public partial class DeviceConfigPageViewModel
    {
        private void TriggerModuleRealTimeUpdate(IspModule module)
        {
            _pendingModulesToUpdate.Add(module);

            _realTimeUpdateTimer.Stop();
            _realTimeUpdateTimer.Start();
        }

        private async void OnRealTimeUpdateTimerTick(object sender, EventArgs e)
        {
            _realTimeUpdateTimer.Stop();

            await Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Func<Task>(async () =>
            {
                var modulesToUpdate = Interlocked.Exchange(ref _pendingModulesToUpdate, new HashSet<IspModule>());

                var tasks = new List<Task>();
                foreach (var module in modulesToUpdate)
                {
                    Application.Current.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

                    tasks.Add(Task.Run(() => WriteModuleToSpecificDevice(module)));
                }

                await Task.WhenAll(tasks);
            }));
        }

        private void WriteModuleToSpecificDevice(IspModule module)
        {
            if (IsReloadingConfig)
                return;
            try
            {
                var moduleParams = _ispProcessor.AllProcessSteps[module].ParamsDataCollection;

                foreach (var item in moduleParams)
                {
                    Logger.Info($"Writing {module} parameters to device - Key: {item.Key}, Data Length: {item.Value.Length}");

                    int sentPos = 0;
                    while (sentPos < item.Value.Length)
                    {
                        var dataToSend = item.Value.Skip(sentPos).Take(512).ToArray();

                        Logger.Debug($"{module} Data Chunk - Key: {item.Key}, Position: {sentPos}, Length: {dataToSend.Length}");

                        int parameter = 0;
                        parameter = sentPos << 8 | (item.Key * Config.IspBitWidth);

                        Logger.Info($"{module} Data - Key: {item.Key}, Position: {sentPos}, Send Length: {dataToSend.Length}, Parameter: 0x{parameter:X}");

                        lock (DeviceConfig.DeviceWriteLock)
                        {
                            DeviceApi.WriteAx327XIspProperty(DeviceConfig.DeviceLocation, parameter, dataToSend, sizeof(byte) * dataToSend.Length);
                        }
                        sentPos += dataToSend.Length;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error writing {module} parameters to device: {ex.Message}", ex);
            }
        }
    }
}
