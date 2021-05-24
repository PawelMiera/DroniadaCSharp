using DJI.WindowsSDK;
using System.Windows.Input;
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
