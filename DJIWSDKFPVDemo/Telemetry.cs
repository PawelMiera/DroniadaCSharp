using DJI.WindowsSDK;
using System;
using System.Collections;
using System.Threading;
using Windows.ApplicationModel.Core;
using Windows.UI.Core;

namespace Droniada
{
	class Telemetry
	{
		bool print_GPS = false;
		bool print_attitude = false;
		bool print_altitude = false;

		public Client client;

		Queue tcpMessagesQueue = new Queue();

		public Attitude attitude;
		public double altitude;
		public LocationCoordinate2D gps_location;
		DJI.WindowsSDK.Components.FlightControllerHandler flightControllerHandler;

		public Telemetry(bool test)
		{
			altitude = 3;
			gps_location.latitude = 52.085234;
			gps_location.longitude = 18.869299;

			attitude = new Attitude()
			{
				pitch = 0,
				yaw = 0,
				roll = 0
			};

			if (GlobalValues.MODE == 3 || GlobalValues.MODE == 4)
			{
				client = new Client(GlobalValues.HOST, GlobalValues.PORT);
				client.OnDataRecived += got_data;
				client.connect();
				tcpMessagesQueue.Enqueue("y" + attitude.yaw.ToString() + ",");
				tcpMessagesQueue.Enqueue("l" + gps_location.latitude.ToString() + ",");
				tcpMessagesQueue.Enqueue("g" + gps_location.longitude.ToString() + ",");
				tcpMessagesQueue.Enqueue("a" + altitude.ToString()+ ",");

				Thread tcpThread = new Thread(new ThreadStart(this.tcp_loop));
				tcpThread.IsBackground = true;
				tcpThread.Start();
			}

		}

		public void resend_data()
		{
			attitude.yaw += 10;
			altitude += 2;
			gps_location.latitude += 1;
			tcpMessagesQueue.Enqueue("y" + attitude.yaw.ToString() + ",");
			tcpMessagesQueue.Enqueue("l" + gps_location.latitude.ToString() + ",");
			tcpMessagesQueue.Enqueue("a" + altitude.ToString() + ",");
		}

		private void got_data(string s)
		{

		}
		private void tcp_loop()
		{
			while(true)
			{
				if(tcpMessagesQueue.Count > 0)
				{
					if(client.clientConnected)
					{
						System.Diagnostics.Debug.WriteLine("sending!!!");
						client.sendMessage((string)tcpMessagesQueue.Dequeue());
					}
				}
				else
				{
					Thread.Sleep(1);
				}
			}
		}

		public Telemetry()
		{
			gps_location.latitude = 52;
			gps_location.longitude = 17;

			flightControllerHandler = DJISDKManager.Instance.ComponentManager.GetFlightControllerHandler(0, 0);
			flightControllerHandler.AttitudeChanged += ComponentHandingPage_AttitudeChanged;
			flightControllerHandler.AircraftLocationChanged += ComponentHandingPage_LocationChanged;
			flightControllerHandler.AltitudeChanged += ComponentHandingPage_AltitudeChanged;

			if (GlobalValues.MODE == 3 || GlobalValues.MODE == 4)
			{
				client = new Client(GlobalValues.HOST, GlobalValues.PORT);
				client.OnDataRecived += got_data;
				client.connect();
				tcpMessagesQueue.Enqueue("y" + attitude.yaw.ToString() + ",");
				tcpMessagesQueue.Enqueue("l" + gps_location.latitude.ToString() + ",");
				tcpMessagesQueue.Enqueue("g" + gps_location.longitude.ToString() + ",");
				tcpMessagesQueue.Enqueue("a" + altitude.ToString() + ",");

				Thread tcpThread = new Thread(new ThreadStart(this.tcp_loop));
				tcpThread.IsBackground = true;
				tcpThread.Start();
			}
		}



		private async void ComponentHandingPage_AltitudeChanged(object sender, DoubleMsg? value)
		{
			await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
			{
				if (value.HasValue)
				{
					altitude = value.Value.value;
					tcpMessagesQueue.Enqueue("a" + altitude.ToString() + ",");
					if (print_altitude)
					{
						System.Diagnostics.Debug.Write("Altitude: ");
						System.Diagnostics.Debug.WriteLine(altitude);
					}
				}

			});
		}

		private async void ComponentHandingPage_AttitudeChanged(object sender, Attitude? value)
		{
			await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
			{
				if (value.HasValue)
				{
					attitude = value.Value;
					tcpMessagesQueue.Enqueue("y" + attitude.yaw.ToString() + ",");
					if (print_attitude)
					{
						System.Diagnostics.Debug.Write("yaw: ");
						System.Diagnostics.Debug.Write(attitude.yaw);
						System.Diagnostics.Debug.Write(" pitch: ");
						System.Diagnostics.Debug.Write(attitude.pitch);
						System.Diagnostics.Debug.Write(" roll: ");
						System.Diagnostics.Debug.WriteLine(attitude.roll);

					}
				}
			});
		}

		private async void ComponentHandingPage_LocationChanged(object sender, LocationCoordinate2D? value)
		{
			await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
			{
				if (value.HasValue)
				{
					gps_location = value.Value;

					tcpMessagesQueue.Enqueue("l" + gps_location.latitude.ToString() + ",");
					tcpMessagesQueue.Enqueue("g" + gps_location.longitude.ToString() + ",");

					if (print_GPS)
					{
						System.Diagnostics.Debug.Write("lat: ");
						System.Diagnostics.Debug.Write(gps_location.latitude);
						System.Diagnostics.Debug.Write(" lon: ");
						System.Diagnostics.Debug.WriteLine(gps_location.longitude);
					}
				}
			});
		}

		public String getString()
		{
			String label = gps_location.latitude.ToString() + "," + gps_location.longitude.ToString() + "," + altitude.ToString() + "," + attitude.yaw.ToString() + "\n";
			return label;
		}
	}
}

/*
 * 		public ICommand _getAltitude;
		public ICommand GetAltitude
		{
			get
			{
				_getAltitude = new RelayCommand(async delegate ()
				{
					do
					{

						var res = await flightControllerHandler.GetAltitudeAsync();
						if (res.error != SDKError.NO_ERROR)
						{
							System.Diagnostics.Debug.WriteLine("Altitude Error!!");
						}
						if (res.value != null)
						{
							altitude = res.value.Value.value;
							if (false)
							{
								System.Diagnostics.Debug.Write("Altitude: ");
								System.Diagnostics.Debug.WriteLine(altitude);
							}
						}

					} while (true);

				}, delegate () { return true; });

				return _getAltitude;
			}
		}


		public ICommand _getLocation;
		public ICommand GetLocation
		{
			get
			{
				_getLocation = new RelayCommand(async delegate ()
				{
					do
					{

						var res = await flightControllerHandler.GetAircraftLocationAsync();
						if (res.error != SDKError.NO_ERROR)
						{
							System.Diagnostics.Debug.WriteLine("Location Error!!");
						}
						if (res.value != null)
						{
							gps_location = res.value.Value;
							//fpvPage.AircraftLocationChange(locationCoordinate2D);
							if (false)
							{
								System.Diagnostics.Debug.Write("lat: ");
								System.Diagnostics.Debug.Write(gps_location.latitude);
								System.Diagnostics.Debug.Write(" lon: ");
								System.Diagnostics.Debug.WriteLine(gps_location.longitude);
							}
						}

					} while (true);

				}, delegate () { return true; });

				return _getLocation;
			}
		}*/
