using System;
using Windows.UI.Xaml.Controls;
using DJI.WindowsSDK;
using DJIVideoParser;
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

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace Droniada
{
	/// <summary>
	/// An empty page that can be used on its own or navigated to within a Frame.
	/// </summary>
	public sealed partial class MainPage : Page
	{

		private VideoCapture _capture;
		int camera_number = GlobalValues.CAMERA;
		Thread mainThread;
		bool pauseThread = false;

		private Parser videoParser;
		private Gimbal gimbal;
		private Telemetry telemetry;
		private Detector detector;
		PositionCalculator positionCalculator = null;

		private byte[] image_data;
		private int image_width = 1440;
		private int image_height = 1080;
		private bool new_image = false;

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

			if (mode == 3 || mode == 4 || mode == 5)
			{
				_capture = new VideoCapture(camera_number, VideoCapture.API.Msmf);
				_capture.SetCaptureProperty(CapProp.FrameWidth, 1280);
				_capture.SetCaptureProperty(CapProp.FrameHeight, 720);

				System.Diagnostics.Debug.WriteLine("Camera initialized!!!");
			}

			positionCalculator = new PositionCalculator(telemetry);
			positionCalculator.update_current_location();


			System.Timers.Timer myTimer = new System.Timers.Timer();
			myTimer.Elapsed += new System.Timers.ElapsedEventHandler(DisplayValues);
			myTimer.Interval = 500;
			myTimer.Start();

			mainThread = new Thread(new ThreadStart(this.main_loop));
			mainThread.IsBackground = true;
			mainThread.Start();
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
			else if (mode == 3)
			{
				mode_3();
			}
			else if (mode == 4)
			{
				mode_3();
			}
			else if (mode == 5)
			{
				mode_5();
			}


		}


		private void mode_5()
		{
			int ind = 0;
			long milliseconds = DateTimeOffset.Now.ToUnixTimeMilliseconds();

			List<Detection> confirmed_detections = new List<Detection>();
			List<Detection> all_detections = new List<Detection>();
			int confirmed_detection_id = 0;
			int time_index = 0;

			while (true)
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

		private void mode_3()
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


		private void mode_1()
		{
			int image_id = 0;
			int ind = 0;
			long milliseconds = DateTimeOffset.Now.ToUnixTimeMilliseconds();

			while (true)
			{
				if (new_image)
				{
					new_image = false;

					Image<Rgba, byte> img = new Image<Rgba, byte>(image_width, image_height);

					img.Bytes = image_data;

					Image<Bgr, byte> img_bgr = new Image<Bgr, byte>(image_width, image_height);

					CvInvoke.CvtColor(img, img_bgr, ColorConversion.Rgba2Bgr);

					if (telemetry.altitude >= GlobalValues.MIN_ALTITUDE)
					{

						String filename = Path.Combine(imagesFolder.Path + @"\" + image_id.ToString() + ".jpg");
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
				else
					Thread.Sleep(1);
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
				if (new_image)
				{
					new_image = false;

					Image<Rgba, byte> img = new Image<Rgba, byte>(image_width, image_height);

					img.Bytes = image_data;

					Image<Bgr, byte> img_bgr = new Image<Bgr, byte>(image_width, image_height);

					CvInvoke.CvtColor(img, img_bgr, ColorConversion.Rgba2Bgr);

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
				else
					Thread.Sleep(1);
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

				frame.Save(filename);

				frame.ROI = Rectangle.Empty;


			}
		}

		public void DisplayValues(object sender, System.Timers.ElapsedEventArgs e)
		{
			_ = Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
			{
				Altitude.Text = telemetry.altitude.ToString();
				Longitude.Text = telemetry.gps_location.longitude.ToString();
				Latitude.Text = telemetry.gps_location.latitude.ToString();
				Yaw.Text = telemetry.attitude.yaw.ToString();
			}
			);
		}




		//Callback of SDKRegistrationEvent
		private async void Instance_SDKRegistrationEvent(SDKRegistrationState state, SDKError resultCode)
		{
			if (resultCode == SDKError.NO_ERROR)
			{
				System.Diagnostics.Debug.WriteLine("Register app successfully.");

				//Must in UI thread
				await Dispatcher.RunAsync(CoreDispatcherPriority.High, async () =>
				{
					//Raw data and decoded data listener
					if (videoParser == null && mode != 3 && mode != 4)
					{
						videoParser = new Parser();
						videoParser.Initialize(delegate (byte[] data)
						{
							//Note: This function must be called because we need DJI Windows SDK to help us to parse frame data.
							return DJISDKManager.Instance.VideoFeeder.ParseAssitantDecodingInfo(0, data);
						});
						//Set the swapChainPanel to display and set the decoded data callback.
						videoParser.SetSurfaceAndVideoCallback(0, 0, swapChainPanel, ReceiveDecodedData);


						DJISDKManager.Instance.VideoFeeder.GetPrimaryVideoFeed(0).VideoDataUpdated += OnVideoPush;
					}
					//get the camera type and observe the CameraTypeChanged event.
					if (mode != 3 && mode != 4)
					{
						DJISDKManager.Instance.ComponentManager.GetCameraHandler(0, 0).CameraTypeChanged += OnCameraTypeChanged;
						var type = await DJISDKManager.Instance.ComponentManager.GetCameraHandler(0, 0).GetCameraTypeAsync();

						OnCameraTypeChanged(this, type.value);
					}
					setup();
				});

			}
			else
			{
				System.Diagnostics.Debug.WriteLine("SDK register failed, the error is: ");
				System.Diagnostics.Debug.WriteLine(resultCode.ToString());
			}
		}

		//raw data
		void OnVideoPush(VideoFeed sender, byte[] bytes)
		{
			videoParser.PushVideoData(0, 0, bytes, bytes.Length);
		}

		//Decode data. Do nothing here. This function would return a bytes array with image data in RGBA format.
		async void ReceiveDecodedData(byte[] data, int width, int height)
		{
			image_data = data;
			image_height = height;
			image_width = width;
			new_image = true;
		}

		//We need to set the camera type of the aircraft to the DJIVideoParser. After setting camera type, DJIVideoParser would correct the distortion of the video automatically.
		private void OnCameraTypeChanged(object sender, CameraTypeMsg? value)
		{
			if (value != null)
			{
				switch (value.Value.value)
				{
					case CameraType.MAVIC_2_ZOOM:
						this.videoParser.SetCameraSensor(AircraftCameraType.Mavic2Zoom);
						break;
					case CameraType.MAVIC_2_PRO:
						this.videoParser.SetCameraSensor(AircraftCameraType.Mavic2Pro);
						break;
					default:
						this.videoParser.SetCameraSensor(AircraftCameraType.Others);
						break;
				}

			}
		}

		private async void OpenFolder_Click(object sender, Windows.UI.Xaml.RoutedEventArgs e)
		{
			await Launcher.LaunchFolderAsync(currentFolder);
		}

		private void StartMission_Click(object sender, Windows.UI.Xaml.RoutedEventArgs e)
		{
			StartMission.Execute(null);
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

		private void change_camera(object sender, Windows.UI.Xaml.RoutedEventArgs e)
		{
			if (mode == 3 || mode == 4 || mode == 5)
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
				//Thread trd = new Thread(new ThreadStart(this.main_loop));
				//trd.IsBackground = true;
				//trd.Start();

			}
		}
	}
}
