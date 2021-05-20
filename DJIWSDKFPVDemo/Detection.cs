using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Emgu.CV;
using Emgu.CV.Structure;
using Emgu.CV.CvEnum;
using Emgu.CV.Util;
using System.Drawing;
using DJI.WindowsSDK;
using GeographicLib;

namespace Droniada
{
	class Detection
	{
		public int detection_id = -1;
		public int shape = -1;
		public Rectangle rectangle;
		public double area;
		public double area_m = 1;
		int[] color;
		public VectorOfPoint points;
		public Point mid;
		public bool to_delete = false;
		public int last_seen = 0;
		public int seen_times = 1;
		public int color_id = -1;
		public String filename;
		public GeodesicLocation gps_location = new GeodesicLocation(1.32133212, 1.32131232);


		public Detection(int shape, Rectangle rectangle, double area, int[] color, VectorOfPoint points, Point mid)
		{
			this.shape = shape;
			this.rectangle = rectangle;
			this.area = area;
			this.color = color;
			this.points = points;
			this.mid = mid;


			int max = this.color.Max();
			color_id = Array.IndexOf(this.color, max);

		}

		public String getString()
		{
			String label = detection_id.ToString() + ",";


			if (color_id != -1)
			{
				if (color_id == GlobalValues.ORANGE)
					label += "Orange,";

				else if (color_id == GlobalValues.BROWN)
					label += "Brown,";

				else if (color_id == GlobalValues.WHITE)
					label += "White,";
			}

			if (shape != -1)
			{
				if (shape == GlobalValues.TRIANGLE)
					label += "Triangle,";

				else if (shape == GlobalValues.SQUARE)
					label += "Square,";

				else if (shape == GlobalValues.CIRCLE)
					label += "Circle,";
			}

			label += gps_location.Latitude.ToString() + ",";
			label += gps_location.Longitude.ToString() + ",";
			label += area_m.ToString() + ",";
			label += seen_times.ToString() + ",";
			label += filename + "\n";

			return label;

		}

		public void update_lat_lon(GeodesicLocation location)
		{
			gps_location = location;
		}

		public void update_color_id()
		{
			int max = color.Max();
			color_id = Array.IndexOf(color, max);
		}

		public void draw_detection(Image<Bgr, Byte> frame)
		{
			if (points != null)
			{
				for (int i = 0; i < points.Size; i++)
				{
					CvInvoke.Circle(frame, points[i], 5, new MCvScalar(0, 0, 255), -1);
				}
			}
			if (!mid.IsEmpty)
			{
				CvInvoke.Circle(frame, mid, 10, new MCvScalar(0, 255, 0), -1);
			}

			String label = "";

			if (color_id != -1)
			{
				if (color_id == GlobalValues.ORANGE)
					label = "Orange ";

				else if (color_id == GlobalValues.BROWN)
					label = "Brown ";

				else if (color_id == GlobalValues.WHITE)
					label = "White ";
			}

			if (shape != -1)
			{
				if (shape == GlobalValues.TRIANGLE)
					label += "Triangle";

				else if (shape == GlobalValues.SQUARE)
					label += "Square";

				else if (shape == GlobalValues.CIRCLE)
					label += "Circle";
			}

			List<String> labels = new List<string>();
			labels.Add(label);

			if (gps_location.Latitude > 1)
			{
				labels.Add("Lat: " + gps_location.Latitude.ToString("#.#####"));
				labels.Add("Lon: " + gps_location.Longitude.ToString("#.#####"));
			}

			if (area_m != -1)
				labels.Add("Area: " + area_m.ToString("##.##"));


			if (!rectangle.IsEmpty)
			{
				int x = Math.Max(0, rectangle.X - GlobalValues.BOX_SIZE_INCREASE);
				int y = Math.Max(0, rectangle.Y - GlobalValues.BOX_SIZE_INCREASE);
				int x1 = Math.Min(GlobalValues.CAMERA_WIDTH, rectangle.X + rectangle.Width + GlobalValues.BOX_SIZE_INCREASE);
				int y1 = Math.Min(GlobalValues.CAMERA_HEIGHT, rectangle.Y + rectangle.Height + GlobalValues.BOX_SIZE_INCREASE);

				Rectangle r = new Rectangle(x, y, x1 - x, y1 - y);

				CvInvoke.Rectangle(frame, r, new MCvScalar(255, 0, 0), 2);

				List<int> labelSizes_x = new List<int>();
				List<int> labelSizes_y = new List<int>();
				int baseLine = 1;
				foreach (String l in labels)
				{
					Size labelSize = CvInvoke.GetTextSize(l, FontFace.HersheySimplex, 0.5, 2, ref baseLine);

					labelSizes_x.Add(labelSize.Width);
					labelSizes_y.Add(labelSize.Height);
				}

				int height = (labelSizes_y.Max() + 10) * labels.Count;

				int rect_y_min = rectangle.Y - height - GlobalValues.BOX_SIZE_INCREASE;

				if (rect_y_min < height)
				{
					rect_y_min = rectangle.Y + rectangle.Height + GlobalValues.BOX_SIZE_INCREASE;
					rect_y_min = Math.Min(GlobalValues.CAMERA_HEIGHT, rect_y_min);
				}


				int label_ymin = Math.Max(rectangle.Y, labelSizes_y.Max() + 10);

				label_ymin = Math.Max(label_ymin, 0);

				int max_x = labelSizes_x.Max();

				CvInvoke.Rectangle(frame, new Rectangle(rectangle.X - GlobalValues.BOX_SIZE_INCREASE, rect_y_min, max_x, height),
					new MCvScalar(255, 255, 255), -1);

				foreach (String l in labels)
				{
					CvInvoke.PutText(frame, l, new Point(rectangle.X - GlobalValues.BOX_SIZE_INCREASE, rect_y_min + 15),
						FontFace.HersheySimplex, 0.5, new MCvScalar(0, 0, 0), 2);
					rect_y_min += 20;
				}


			}

		}

		public void merge_detecions(Detection new_det)
		{
			color[0] += new_det.color[0];
			color[1] += new_det.color[1];
			color[2] += new_det.color[2];
			update_color_id();
			seen_times++;
			area_m = (area_m * (seen_times - 1) + new_det.area_m) / seen_times;

			double latitude = (gps_location.Latitude * (seen_times - 1) + new_det.gps_location.Latitude) / seen_times;
			double longitude = (gps_location.Longitude * (seen_times - 1) + new_det.gps_location.Longitude) / seen_times;

			gps_location = new GeodesicLocation(latitude, longitude);
		}

		public void draw_confirmed_detection(Image<Bgr, Byte> frame, Point my_mid)
		{
			CvInvoke.Circle(frame, my_mid, 22, new MCvScalar(0, 0, 255), 3);
		}

		public bool check_detection(Detection new_det)
		{

			return (shape == new_det.shape) && (Math.Abs(gps_location.Latitude - new_det.gps_location.Latitude) < GlobalValues.MAX_LONG_LAT_DIFF)
				   && (Math.Abs(gps_location.Longitude - new_det.gps_location.Longitude) < GlobalValues.MAX_LONG_LAT_DIFF) &&
				   Math.Abs(area_m - new_det.area_m) < GlobalValues.MAX_AREA_DIFF;
		}
	}
}
