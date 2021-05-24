using System;
using Windows.UI.Xaml.Controls;
using DJI.WindowsSDK;
using Windows.UI.Core;
using Emgu.CV;
using Emgu.CV.Structure;
using Emgu.CV.CvEnum;
using System.Threading;
using System.Collections.Generic;
using GeographicLib;
using System.Drawing;
using System.IO;
using Windows.Storage;
using System.Threading.Tasks;
using Windows.System;
using System.Windows.Input;
using DJIUWPSample.Commands;
using Windows.UI.Popups;
using Windows.UI.Xaml.Controls.Maps;
using Windows.Devices.Geolocation;
using Windows.Storage.Streams;
using Windows.UI.Xaml.Media;


// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace Droniada
{
	/// <summary>
	/// An empty page that can be used on its own or navigated to within a Frame.
	/// </summary>
	public sealed partial class MainPage : Page
	{
		private VideoCapture _capture = null;
		int camera_number = GlobalValues.CAMERA;
		Thread mainThread;
		bool pauseThread = false;

		private Gimbal gimbal;
		private Telemetry telemetry = null;
		private Detector detector;
		PositionCalculator positionCalculator = null;

		StorageFolder storageFolder;
		StorageFolder currentFolder;
		StorageFolder detectionsFolder;
		StorageFolder imagesFolder;
		StorageFile telemetryFile;
		StorageFile detectionsFile;

		private int mode = GlobalValues.MODE;       // 0 - local detection, 1 - save images , 2 - mission


		public MainPage()
		{
			this.InitializeComponent();
			storageFolder = ApplicationData.Current.LocalFolder;
			String folderName = System.DateTime.Now.ToString("dd_MM_yyyy_HH_mm_ss");
			Task task = create_telemetry_file(folderName);

			detector = new Detector();


			if (mode != 0 && mode != 4)
			{
				DJISDKManager.Instance.SDKRegistrationStateChanged += Instance_SDKRegistrationEvent;
				DJISDKManager.Instance.RegisterApp("1443e129ca489621152be6bc");
			}
			else
			{
				setup();
			}

			

			System.Timers.Timer myTimer = new System.Timers.Timer();
			myTimer.Elapsed += new System.Timers.ElapsedEventHandler(DisplayValues);
			myTimer.Interval = 500;
			myTimer.Start();

			mainThread = new Thread(new ThreadStart(this.main_loop));
			mainThread.IsBackground = true;
			mainThread.Start();

			setup_map();
		}

		void tcpDataReceived(string msg)
		{
			_ = Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
			{
				tcpData.Text = msg;
			});
		}

		private void setup()
		{

			if (mode != 0 && mode != 4)
			{
				gimbal = new Gimbal();
				telemetry = new Telemetry();
			}
			else
			{
				telemetry = new Telemetry(true);
			}

			if (mode == 2 || mode == 1)
			{
				_capture = new VideoCapture(camera_number, VideoCapture.API.Msmf);
				_capture.SetCaptureProperty(CapProp.FrameWidth, 1280);
				_capture.SetCaptureProperty(CapProp.FrameHeight, 720);

				System.Diagnostics.Debug.WriteLine("Camera initialized!!!");
			}

			positionCalculator = new PositionCalculator(telemetry);
			positionCalculator.update_current_location();

			if(mode == 3 || mode == 4)
			{
				telemetry.client.OnDataRecived += tcpDataReceived;
			}
		}

		private void main_loop()
		{
			if (mode == 0)
			{
				mode_0();
			}
			else if (mode == 1)
			{
				mode_1();
			}
			else if (mode == 2)
			{
				mode_2();
			}
		}


		private void mode_2()
		{
			int ind = 0;
			long milliseconds = DateTimeOffset.Now.ToUnixTimeMilliseconds();

			List<Detection> confirmed_detections = new List<Detection>();
			List<Detection> all_detections = new List<Detection>();
			int confirmed_detection_id = 0;
			int time_index = 0;

			while (true)
			{
				if (pauseThread)
				{
					Thread.Sleep(1);
					continue;
				}
				if (_capture != null)
				{
					Mat mat = _capture.QueryFrame();

					Image<Bgr, Byte> img_bgr = mat.ToImage<Bgr, Byte>();

					if (telemetry.altitude >= GlobalValues.MIN_ALTITUDE)
					{

						List<Detection> detections = detector.detect(img_bgr);

						positionCalculator.update_current_location();
						positionCalculator.update_meters_per_pixel();
						positionCalculator.calculate_max_meters_area();
						positionCalculator.calculate_extreme_points();

						bool update_detections_file = false;

						foreach (Detection d in detections)
						{
							GeodesicLocation location = positionCalculator.calculate_point_lat_long(d.mid);
							d.update_lat_lon(location);
							d.area_m = positionCalculator.calculate_area_in_meters_2(d.area);
							d.draw_detection(img_bgr);

							foreach (Detection conf_d in confirmed_detections)
							{
								if (d.check_detection(conf_d))
								{
									conf_d.merge_detecions(d);
									conf_d.last_seen = time_index;
									d.to_delete = true;
									update_detections_file = true;
									break;
								}
							}
							if (d.to_delete)
							{
								continue;
							}

							foreach (Detection all_d in all_detections)
							{
								if (d.check_detection(all_d))
								{
									all_d.merge_detecions(d);
									all_d.last_seen = time_index;
									d.to_delete = true;
									break;
								}
							}
							if (!d.to_delete)
							{
								d.last_seen = time_index;
							}

						}

						detections.RemoveAll(d => d.to_delete);

						all_detections.AddRange(detections);

						foreach (Detection all_d in all_detections)
						{
							if (all_d.seen_times > 8)
							{
								all_d.detection_id = confirmed_detection_id;

								confirmed_detection_id++;

								String filename = Path.Combine(detectionsFolder.Path + @"\" + all_d.detection_id.ToString() + ".jpg");

								all_d.filename = filename;

								Task t = write_to_file(detectionsFile, all_d.getString());

								save_frame_crop(img_bgr, all_d.rectangle, filename);
								confirmed_detections.Add(all_d);
								all_d.to_delete = true;
							}
							else if (all_d.seen_times > 4)
							{
								if (time_index - all_d.last_seen > 800)
									all_d.to_delete = true;
							}
							else
							{
								if (time_index - all_d.last_seen > 20)
									all_d.to_delete = true;
							}
						}

						all_detections.RemoveAll(d => d.to_delete);

						String detections_file_text = "";

						foreach (Detection c in confirmed_detections)
						{
							if (update_detections_file)
							{
								detections_file_text += c.getString();
							}

							Point my_mid = positionCalculator.get_detection_on_image_cords(c.gps_location);

							if (!my_mid.IsEmpty)
							{
								c.draw_confirmed_detection(img_bgr, my_mid);
							}
						}
						if (update_detections_file)
						{
							Task t_dets = rewrite_file(detectionsFile, detections_file_text);
						}

						time_index++;

					}

					CvInvoke.Imshow("img", img_bgr);
					CvInvoke.WaitKey(1);

					ind++;
					if (GlobalValues.PRINT_FPS && DateTimeOffset.Now.ToUnixTimeMilliseconds() - milliseconds > 1000)
					{
						milliseconds = DateTimeOffset.Now.ToUnixTimeMilliseconds();
						System.Diagnostics.Debug.WriteLine(ind);
						ind = 0;
					}
				}
			}
		}

		private void mode_1()
		{
			int image_id = 0;
			int ind = 0;
			long milliseconds = DateTimeOffset.Now.ToUnixTimeMilliseconds();


			while (true)
			{
				if(pauseThread)
				{
					Thread.Sleep(1);
					continue;
				}
				if (_capture != null)
				{
					Mat mat = new Mat();
					mat = _capture.QueryFrame();

					Image<Bgr, Byte> img_bgr = mat.ToImage<Bgr, Byte>();


					if (telemetry.altitude >= GlobalValues.MIN_ALTITUDE)
					{

						String filename = Path.Combine(imagesFolder.Path + @"\" + image_id.ToString() + ".png");
						image_id++;

						Task t = write_to_file(telemetryFile, telemetry.getString());

						img_bgr.Save(filename);

						save_frame_crop(img_bgr, Rectangle.Empty, filename);
					}

					CvInvoke.Imshow("img", img_bgr);
					CvInvoke.WaitKey(1);

					ind++;
					if (GlobalValues.PRINT_FPS && DateTimeOffset.Now.ToUnixTimeMilliseconds() - milliseconds > 1000)
					{
						milliseconds = DateTimeOffset.Now.ToUnixTimeMilliseconds();
						System.Diagnostics.Debug.WriteLine(ind);
						ind = 0;
					}
				}

			}
		}


		private void mode_0()
		{
			int ind = 0;
			long milliseconds = DateTimeOffset.Now.ToUnixTimeMilliseconds();

			List<Detection> confirmed_detections = new List<Detection>();
			List<Detection> all_detections = new List<Detection>();
			int confirmed_detection_id = 0;
			int time_index = 0;

			while (true)
			{
				String path = "1" + ".jpg";

				Mat myimg = CvInvoke.Imread(path, ImreadModes.Color);
				Image<Bgr, Byte> img_bgr = myimg.ToImage<Bgr, Byte>();

				List<Detection> detections = detector.detect(img_bgr);

				positionCalculator.update_current_location();
				positionCalculator.update_meters_per_pixel();
				positionCalculator.calculate_max_meters_area();
				positionCalculator.calculate_extreme_points();

				bool update_detections_file = false;

				foreach (Detection d in detections)
				{
					GeodesicLocation location = positionCalculator.calculate_point_lat_long(d.mid);
					d.update_lat_lon(location);
					d.area_m = positionCalculator.calculate_area_in_meters_2(d.area);
					d.draw_detection(img_bgr);

					foreach (Detection conf_d in confirmed_detections)
					{
						if (d.check_detection(conf_d))
						{
							conf_d.merge_detecions(d);
							conf_d.last_seen = time_index;
							d.to_delete = true;
							update_detections_file = true;
							break;
						}
					}
					if (d.to_delete)
					{
						continue;
					}

					foreach (Detection all_d in all_detections)
					{
						if (d.check_detection(all_d))
						{
							all_d.merge_detecions(d);
							all_d.last_seen = time_index;
							d.to_delete = true;
							break;
						}
					}

					if (!d.to_delete)
					{
						d.last_seen = time_index;
					}
				}

				detections.RemoveAll(d => d.to_delete);

				all_detections.AddRange(detections);

				foreach (Detection all_d in all_detections)
				{
					if (all_d.seen_times > 8)
					{
						all_d.detection_id = confirmed_detection_id;

						confirmed_detection_id++;

						String filename = Path.Combine(detectionsFolder.Path + @"\" + all_d.detection_id.ToString() + ".jpg");

						all_d.filename = filename;

						Task t = write_to_file(detectionsFile, all_d.getString());

						save_frame_crop(img_bgr, all_d.rectangle, filename);
						confirmed_detections.Add(all_d);
						all_d.to_delete = true;
					}
					else if (all_d.seen_times > 4)
					{
						if (time_index - all_d.last_seen > 800)
							all_d.to_delete = true;
					}
					else
					{
						if (time_index - all_d.last_seen > 20)
							all_d.to_delete = true;
					}
				}

				all_detections.RemoveAll(d => d.to_delete);

				String detections_file_text = "";

				foreach (Detection c in confirmed_detections)
				{
					if (update_detections_file)
					{
						detections_file_text += c.getString();
					}

					Point my_mid = positionCalculator.get_detection_on_image_cords(c.gps_location);

					if (!my_mid.IsEmpty)
					{
						c.draw_confirmed_detection(img_bgr, my_mid);
					}
				}
				if (update_detections_file)
				{
					Task t_dets = rewrite_file(detectionsFile, detections_file_text);
				}

				time_index++;

				CvInvoke.Imshow("img", img_bgr);
				CvInvoke.WaitKey(1);

				ind++;
				if (GlobalValues.PRINT_FPS && DateTimeOffset.Now.ToUnixTimeMilliseconds() - milliseconds > 1000)
				{
					milliseconds = DateTimeOffset.Now.ToUnixTimeMilliseconds();
					System.Diagnostics.Debug.WriteLine(ind);
					ind = 0;
				}
			}

		}


		async Task create_telemetry_file(String folderName)
		{
			currentFolder = await storageFolder.CreateFolderAsync(folderName);
			detectionsFolder = await currentFolder.CreateFolderAsync("detections");
			imagesFolder = await currentFolder.CreateFolderAsync("images");
			telemetryFile = await currentFolder.CreateFileAsync("telemetry.txt", CreationCollisionOption.ReplaceExisting);
			detectionsFile = await currentFolder.CreateFileAsync("detections.txt", CreationCollisionOption.ReplaceExisting);
		}

		async Task write_to_file(StorageFile file, String text)
		{
			await FileIO.AppendTextAsync(file, text);
		}

		async Task rewrite_file(StorageFile file, String text)
		{
			await FileIO.WriteTextAsync(file, text);
		}


		private void save_frame_crop(Image<Bgr, Byte> frame, Rectangle rectangle, String filename)
		{
			if (!rectangle.IsEmpty)
			{
				int x = rectangle.X - rectangle.Width;
				int y = rectangle.Y - rectangle.Height;
				int x1 = rectangle.X + 2 * rectangle.Width;
				int y1 = rectangle.Y + 2 * rectangle.Height;

				x = Math.Max(0, x);
				y = Math.Max(0, y);
				x1 = Math.Min(GlobalValues.CAMERA_WIDTH, x1);
				y1 = Math.Min(GlobalValues.CAMERA_HEIGHT, y1);

				frame.ROI = new Rectangle(x, y, x1 - x, y1 - y);
				//////////resize
				frame.Save(filename);

				frame.ROI = Rectangle.Empty;


			}
		}

		public void DisplayValues(object sender, System.Timers.ElapsedEventArgs e)
		{
			_ = Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
			{
				if (telemetry != null)
				{
					Altitude.Text = telemetry.altitude.ToString();
					Longitude.Text = telemetry.gps_location.longitude.ToString();
					Latitude.Text = telemetry.gps_location.latitude.ToString();
					Yaw.Text = telemetry.attitude.yaw.ToString();
					AircraftLocationChange();
				}
			}
			);
		}

		//Callback of SDKRegistrationEvent
		private async void Instance_SDKRegistrationEvent(SDKRegistrationState state, SDKError resultCode)
		{
			if (resultCode == SDKError.NO_ERROR)
			{
				System.Diagnostics.Debug.WriteLine("Register app successfully.");

				setup();

			}
			else
			{
				System.Diagnostics.Debug.WriteLine("SDK register failed, the error is: ");
				System.Diagnostics.Debug.WriteLine(resultCode.ToString());
			}
		}

		private async void OpenFolder_Click(object sender, Windows.UI.Xaml.RoutedEventArgs e)
		{
			await Launcher.LaunchFolderAsync(currentFolder);
		}


		private void change_camera(object sender, Windows.UI.Xaml.RoutedEventArgs e)
		{
			if (mode == 1 || mode == 2)
			{
				if (camera_number == 0)
				{
					camera_number = 1;
				}
				else
				{
					camera_number = 0;
				}

				pauseThread = true;

				_capture = new VideoCapture(camera_number);
				_capture.SetCaptureProperty(CapProp.FrameWidth, 1280);
				_capture.SetCaptureProperty(CapProp.FrameHeight, 720);

				pauseThread = false;

			}
		}

		/// <summary>
		/// Map objects !!!!!!!
		/// </summary>
		/// 

		public ICommand _setGroundStationModeEnabled;
		public ICommand SetGroundStationModeEnabled
		{
			get
			{

				_setGroundStationModeEnabled = new RelayCommand(async delegate ()
				{
					SDKError err = await DJISDKManager.Instance.ComponentManager.GetFlightControllerHandler(0, 0).SetGroundStationModeEnabledAsync(new BoolMsg() { value = true });
					var messageDialog = new MessageDialog(String.Format("Set GroundStationMode Enabled: {0}", err.ToString()));
					await messageDialog.ShowAsync();
				}, delegate () { return true; });

				return _setGroundStationModeEnabled;
			}
		}

		public ICommand _loadMission;
		public ICommand LoadMission
		{
			get
			{

				_loadMission = new RelayCommand(async delegate ()
				{
					SDKError err = DJISDKManager.Instance.WaypointMissionManager.GetWaypointMissionHandler(0).LoadMission(mission);
					var messageDialog = new MessageDialog(String.Format("SDK load mission: {0}", err.ToString()));
					await messageDialog.ShowAsync();
				}, delegate () { return true; });

				return _loadMission;
			}
		}

		public ICommand _uploadMission;
		public ICommand UploadMission
		{
			get
			{

				_uploadMission = new RelayCommand(async delegate ()
				{
					SDKError err = await DJISDKManager.Instance.WaypointMissionManager.GetWaypointMissionHandler(0).UploadMission();
					var messageDialog = new MessageDialog(String.Format("Upload mission to aircraft: {0}", err.ToString()));
					await messageDialog.ShowAsync();
				}, delegate () { return true; });

				return _uploadMission;
			}
		}

		public ICommand _startMission;
		public ICommand StartMission
		{
			get
			{
				_startMission = new RelayCommand(async delegate ()
				{
					var err = await DJISDKManager.Instance.WaypointMissionManager.GetWaypointMissionHandler(0).StartMission();
					var messageDialog = new MessageDialog(String.Format("Start mission: {0}", err.ToString()));
					await messageDialog.ShowAsync();
				}, delegate () { return true; });

				return _startMission;
			}
		}


		MapIcon waypointIcon = null;
		MapIcon currentWaypointIcon = null;
		MapElementsLayer routeLayer = new MapElementsLayer();
		MapElementsLayer waypointLayer = new MapElementsLayer();
		MapElementsLayer locationLayer = new MapElementsLayer();

		MapElementsLayer currentWaypointLayer = new MapElementsLayer();
		WaypointMission mission;
		bool missionCreated = false;

		BasicGeoposition currentWaypoint;

		private void setup_map()
		{

			WaypointMap.Layers.Add(waypointLayer);
			WaypointMap.Layers.Add(currentWaypointLayer);
			WaypointMap.Layers.Add(locationLayer);
			WaypointMap.Layers.Add(routeLayer);

			if (telemetry != null)
				WaypointMap.Center = new Geopoint(new BasicGeoposition() {Latitude = telemetry.gps_location.latitude, Longitude = telemetry.gps_location.longitude});
			else
				WaypointMap.Center = new Geopoint(new BasicGeoposition() { Latitude = 52, Longitude = 17 });

			
			mapItems.ItemsSource = InitInterestPoints();


			if (currentWaypointIcon == null)
			{
				currentWaypointIcon = new MapIcon()
				{
					Image = RandomAccessStreamReference.CreateFromUri(new Uri("ms-appx:///Assets/xd")),
					NormalizedAnchorPoint = new Windows.Foundation.Point(0.5, 0.5),
					ZIndex = 0,
				};
			}
		}

		private void Recenter_Map(object sender, Windows.UI.Xaml.RoutedEventArgs e)
		{
			WaypointMap.Center = new Geopoint(new BasicGeoposition() { Latitude = telemetry.gps_location.latitude, Longitude = telemetry.gps_location.longitude});
		}

		private List<InterestPoint> InitInterestPoints()
		{
			if (telemetry != null)
			{
				List<InterestPoint> points = new List<InterestPoint>();
				points.Add(new InterestPoint
				{
					ImageSourceUri = new Uri("ms-appx:///Assets/arrow.png"),
					Location = new Geopoint(new BasicGeoposition() { Latitude = telemetry.gps_location.latitude, Longitude = telemetry.gps_location.longitude}),
					Rotate = new RotateTransform
					{
						Angle = telemetry.attitude.yaw,
						CenterX = 15,
						CenterY = 15
					},
					Translate = new TranslateTransform
					{
						X = 0,
						Y = 0
					}
				});

				return points;
			}
			return null;
		}

		private void AircraftLocationChange()
		{
			/*var points = mapItems.ItemsSource as List<InterestPoint>;
			points[0].Rotate.Angle = telemetry.attitude.yaw;

			var location = new BasicGeoposition() { Latitude = 0, Longitude = 0 };

			points[0].Location = new Geopoint(location);*/

			mapItems.ItemsSource = InitInterestPoints();
		}

		private void init_mission(double autoFlightSpeed, WaypointMissionFinishedAction action, WaypointMissionHeadingMode heading_mode)
		{
			missionCreated = true;
			mission = new WaypointMission()
			{
				waypointCount = 0,
				maxFlightSpeed = 15,
				autoFlightSpeed = autoFlightSpeed,
				finishedAction = action,
				headingMode = heading_mode,
				flightPathMode = WaypointMissionFlightPathMode.NORMAL,
				gotoFirstWaypointMode = WaypointMissionGotoFirstWaypointMode.SAFELY,
				exitMissionOnRCSignalLostEnabled = false,
				pointOfInterest = new LocationCoordinate2D()
				{
					latitude = 0,
					longitude = 0
				},
				gimbalPitchRotationEnabled = false,
				repeatTimes = 0,
				missionID = 0,
				waypoints = new List<Waypoint>()
		};

		}



		private Waypoint InitDumpWaypoint(double latitude, double longitude, double altitude, WaypointTurnMode turnMode, int heading, double speed)
		{
			Waypoint waypoint = new Waypoint()
			{
				location = new LocationCoordinate2D() { latitude = latitude, longitude = longitude },
				altitude = altitude,
				gimbalPitch = -90,
				turnMode = turnMode,
				heading = heading,
				actionRepeatTimes = 1,
				actionTimeoutInSeconds = 900,
				cornerRadiusInMeters = 0.2,
				speed = speed,
				shootPhotoTimeInterval = -1,
				shootPhotoDistanceInterval = -1,
				waypointActions = new List<WaypointAction>()
			};
			return waypoint;
		}


		private void RedrawCurrentWaypoint()
		{


			if (currentWaypointLayer.MapElements.Count == 0)
			{
				currentWaypointIcon.Location = new Geopoint(new BasicGeoposition() { Latitude = currentWaypoint.Latitude, Longitude = currentWaypoint.Longitude });
				currentWaypointLayer.MapElements.Add(currentWaypointIcon);
			}
			else
			{
				currentWaypointIcon.Location = new Geopoint(new BasicGeoposition() { Latitude = currentWaypoint.Latitude, Longitude = currentWaypoint.Longitude });
			}
		}


	private void RedrawWaypoint()
		{

			List<BasicGeoposition> waypointPositions = new List<BasicGeoposition>();

			for (int i = 0; i < mission.waypoints.Count; ++i)
			{
				if (waypointLayer.MapElements.Count == i)
				{
					MapIcon waypointIcon = new MapIcon()
					{
						Image = RandomAccessStreamReference.CreateFromUri(new Uri("ms-appx:///Assets/waypoint.png")),
						NormalizedAnchorPoint = new Windows.Foundation.Point(0.5, 0.5),
						ZIndex = 0,
					};
					waypointLayer.MapElements.Add(waypointIcon);
				}

				var geolocation = new BasicGeoposition() { Latitude = mission.waypoints[i].location.latitude, Longitude = mission.waypoints[i].location.longitude };
				(waypointLayer.MapElements[i] as MapIcon).Location = new Geopoint(geolocation);
				waypointPositions.Add(geolocation);
			}

			if (waypointPositions.Count >= 2)
			{
				if (routeLayer.MapElements.Count == 0)
				{
					var polyline = new MapPolyline
					{
						StrokeColor = Windows.UI.Color.FromArgb(255, 0, 255, 0),
						Path = new Geopath(waypointPositions),
						StrokeThickness = 2
					};
					routeLayer.MapElements.Add(polyline);
				}
				else
				{
					var waypointPolyline = routeLayer.MapElements[0] as MapPolyline;
					waypointPolyline.Path = new Geopath(waypointPositions);
				}
			}




			/*for (int i = 0; i < mission.waypoints.Count; ++i)
			{
				waypointLayer.MapElements.Add(waypointIcon);
				var geolocation = new BasicGeoposition() { Latitude = mission.waypoints[i].location.latitude, Longitude = mission.waypoints[i].location.longitude };
				(waypointLayer.MapElements[i] as MapIcon).Location = new Geopoint(geolocation);
				waypointPositions.Add(geolocation);
			}


			if (waypointPositions.Count >= 2)
			{
				var polyline = new MapPolyline
				{
					StrokeColor = Windows.UI.Color.FromArgb(255, 255, 0, 0),
					Path = new Geopath(waypointPositions),
					StrokeThickness = 2
				};
				routeLayer.MapElements.Add(polyline);
			}*/

		

		}

		private void UploadMission_Click(object sender, Windows.UI.Xaml.RoutedEventArgs e)
		{
			UploadMission.Execute(null);
		}

		private void LoadMission_Click(object sender, Windows.UI.Xaml.RoutedEventArgs e)
		{
			LoadMission.Execute(null);
		}

		private void SetGround_Click(object sender, Windows.UI.Xaml.RoutedEventArgs e)
		{
			SetGroundStationModeEnabled.Execute(null);
		}

		private void WaypointMap_MapTapped(MapControl sender, MapInputEventArgs args)
		{
			currentWaypoint.Latitude = args.Location.Position.Latitude;
			currentWaypoint.Longitude = args.Location.Position.Longitude;

			MapLat.Text = " " + currentWaypoint.Latitude.ToString();
			MapLon.Text = " " + currentWaypoint.Longitude.ToString();

			RedrawCurrentWaypoint();
		}

		private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
		{
			if (System.Text.RegularExpressions.Regex.IsMatch(altitudeTextBox.Text, "[^0-9]"))
			{
				altitudeTextBox.Text = altitudeTextBox.Text.Remove(altitudeTextBox.Text.Length - 1);
			}
		}

		private void SpeedTextBox_TextChanged(object sender, TextChangedEventArgs e)
		{
			if (System.Text.RegularExpressions.Regex.IsMatch(speedTextBox.Text, "[^0-9]"))
			{
				speedTextBox.Text = speedTextBox.Text.Remove(speedTextBox.Text.Length - 1);
			}
			else
			{
				int number;

				bool success = Int32.TryParse(speedTextBox.Text, out number);

				if (success && number > 15)
				{
					speedTextBox.Text = "15";
				}
			}
		}


		private void Init_Mission_Click(object sender, Windows.UI.Xaml.RoutedEventArgs e)
		{
			WaypointMissionFinishedAction finishedAction = (WaypointMissionFinishedAction)Enum.Parse(typeof(WaypointMissionFinishedAction), finishedCombo.SelectedIndex.ToString());
			WaypointMissionHeadingMode headingMode = (WaypointMissionHeadingMode)Enum.Parse(typeof(WaypointMissionHeadingMode), ((ComboBoxItem)headingCombo.SelectedItem).Tag.ToString());

			init_mission(Int32.Parse(speedTextBox.Text), finishedAction, headingMode);

			int heading;

			bool success = Int32.TryParse(headingTextBox.Text, out heading);

			if (success && heading > 180)
			{
				heading = heading - 360;
			}

			WaypointTurnMode turnMode = (WaypointTurnMode)Enum.Parse(typeof(WaypointTurnMode), turnCombo.SelectedIndex.ToString());

			Waypoint waypoint = InitDumpWaypoint(telemetry.gps_location.latitude + 0.00003, telemetry.gps_location.longitude, Double.Parse(altitudeTextBox.Text), turnMode, heading, Int32.Parse(speedTextBox.Text));

			mission.waypoints.Add(waypoint);

			RedrawWaypoint();

			int a = 0;
		}

		private void Add_Waypoint_Click(object sender, Windows.UI.Xaml.RoutedEventArgs e)
		{
			if (missionCreated)
			{
				var lastWaypoint = mission.waypoints[mission.waypoints.Count - 1];

				var altitude = Double.Parse(altitudeTextBox.Text);

				if (currentWaypoint.Latitude != lastWaypoint.location.latitude || currentWaypoint.Longitude != lastWaypoint.location.longitude
					|| altitude != lastWaypoint.altitude)
				{
					int heading;

					bool success = Int32.TryParse(headingTextBox.Text, out heading);

					if (success && heading > 180)
					{
						heading = heading - 360;
					}

					WaypointTurnMode turnMode = (WaypointTurnMode)Enum.Parse(typeof(WaypointTurnMode), turnCombo.SelectedIndex.ToString());

					Waypoint waypoint = InitDumpWaypoint(currentWaypoint.Latitude, currentWaypoint.Longitude, altitude, turnMode, heading, Int32.Parse(speedTextBox.Text));

					mission.waypoints.Add(waypoint);

					MapLat.Text = "";
					MapLon.Text = "";


					RedrawWaypoint();
				}
			}
		}

		private void HeadingTextBox_TextChanged(object sender, TextChangedEventArgs e)
		{
			if (System.Text.RegularExpressions.Regex.IsMatch(headingTextBox.Text, "[^0-9]"))
			{
				headingTextBox.Text = headingTextBox.Text.Remove(headingTextBox.Text.Length - 1);
			}
			else
			{
				int number;

				bool success = Int32.TryParse(headingTextBox.Text, out number);

				if (success && number > 360)
				{
					speedTextBox.Text = "360";
				}
				else if (success && number < 0)
				{
					speedTextBox.Text = "0";
				}
			}
		}

		private void ResendData_Click(object sender, Windows.UI.Xaml.RoutedEventArgs e)
		{
			telemetry.resend_data();
		}

		private void Button_Click(object sender, Windows.UI.Xaml.RoutedEventArgs e)
		{
			SetGroundStationModeEnabled.Execute(null);
		}

		private void Button_Click_1(object sender, Windows.UI.Xaml.RoutedEventArgs e)
		{
			Waypoint waypoint = InitDumpWaypoint(telemetry.gps_location.latitude + 0.00003, telemetry.gps_location.longitude, mission.waypoints[0].altitude, mission.waypoints[0].turnMode, mission.waypoints[0].heading, mission.waypoints[0].speed);

			mission.waypoints[0] = waypoint;
			LoadMission.Execute(null);
		}

		private void Button_Click_2(object sender, Windows.UI.Xaml.RoutedEventArgs e)
		{
			UploadMission.Execute(null);
		}

		private void Button_Click_3(object sender, Windows.UI.Xaml.RoutedEventArgs e)
		{
			StartMission.Execute(null);
		}

		private void StartMission_Click(object sender, Windows.UI.Xaml.RoutedEventArgs e)
		{
			StartMission.Execute(null);
		}


	}

	public class InterestPoint
	{
		public Uri ImageSourceUri { get; set; }
		public Geopoint Location { get; set; }
		public RotateTransform Rotate { get; set; }
		public TranslateTransform Translate { get; set; }
		public Point CenterPoint { get; set; }
	}


}
