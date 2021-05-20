using DJI.WindowsSDK;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using DJIVideoParser;
using System.Timers;
using System.Windows.Input;
using Windows.ApplicationModel.Core;
using Windows.UI.Core;
using DJIUWPSample.Commands;
using DJI.WindowsSDK.Components;
using System.Threading.Tasks;

namespace Droniada
{
	class Gimbal
	{
		GimbalHandler gimbalHandler = DJISDKManager.Instance.ComponentManager.GetGimbalHandler(0, 0);

		public Gimbal()
		{
			SetAngle.Execute(null);
		}

		public ICommand _setAngle;
		public ICommand SetAngle
		{
			get
			{
				_setAngle = new RelayCommand(async delegate ()
				{
					await Task.Delay(500);
					GimbalResetCommandMsg gimbalResetCommandMsg = new GimbalResetCommandMsg();
					await gimbalHandler.ResetGimbalAsync(gimbalResetCommandMsg);

					await Task.Delay(2000);

					var gimbalRotation = new GimbalSpeedRotation();
					gimbalRotation.pitch = -220;
					await gimbalHandler.RotateBySpeedAsync(gimbalRotation);

				}, delegate () { return true; });

				return _setAngle;
			}
		}

	}
}
