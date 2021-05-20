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
using System.Numerics;

namespace Droniada
{
	class Detector
	{
		Hsv brown_low;
		Hsv brown_high;
		Hsv brown_low_2;
		Hsv brown_high_2;
		Hsv white_low;
		Hsv white_high;
		Hsv orange_low;
		Hsv orange_high;

		public Detector()
		{
			brown_low = new Hsv(161, 26, 128);
			brown_high = new Hsv(203, 73, 225);
			brown_low_2 = new Hsv(0, 36, 128);
			brown_high_2 = new Hsv(9, 75, 138);
			white_low = new Hsv(65, 0, 230);
			white_high = new Hsv(178, 81, 255);
			orange_low = new Hsv(10, 35, 155);
			orange_high = new Hsv(57, 114, 255);
		}


		private VectorOfVectorOfPoint[] extract_contours(Image<Bgr, byte> frame)
		{
			Image<Hsv, byte> hsv = new Image<Hsv, byte>(frame.Width, frame.Height);

			CvInvoke.CvtColor(frame, hsv, ColorConversion.Bgr2Hsv);

			Image<Gray, byte> brownMask = hsv.InRange(brown_low, brown_high);
			Image<Gray, byte> brownMask_2 = hsv.InRange(brown_low_2, brown_high_2);
			Image<Gray, byte> whiteMask = hsv.InRange(white_low, white_high);
			Image<Gray, byte> orangeMask = hsv.InRange(orange_low, orange_high);

			//CvInvoke.Imshow("dsads", whiteMask + brownMask +brownMask_2 + orangeMask);

			VectorOfVectorOfPoint whiteContours = new VectorOfVectorOfPoint();
			VectorOfVectorOfPoint brownContours = new VectorOfVectorOfPoint();
			VectorOfVectorOfPoint orangeContours = new VectorOfVectorOfPoint();

			CvInvoke.FindContours(whiteMask, whiteContours, null, RetrType.List, ChainApproxMethod.ChainApproxSimple);
			CvInvoke.FindContours(brownMask + brownMask_2, brownContours, null, RetrType.List, ChainApproxMethod.ChainApproxSimple);
			CvInvoke.FindContours(orangeMask, orangeContours, null, RetrType.List, ChainApproxMethod.ChainApproxSimple);

			VectorOfVectorOfPoint[] contours_all_masks = { whiteContours, orangeContours, brownContours };

			return contours_all_masks;
		}



		public List<Detection> detect(Image<Bgr, byte> frame)
		{
			VectorOfVectorOfPoint[] contours = extract_contours(frame);

			List<Detection> detections = new List<Detection>();

			for (int c = 0; c < 3; c++)
			{
				for (int i = 0; i < contours[c].Size; i++)
				{
					double area = CvInvoke.ContourArea(contours[c][i]);

					if (area > GlobalValues.MIN_AREA)
					{
						Rectangle bb = CvInvoke.BoundingRectangle(contours[c][i]);

						int shape;
						VectorOfPoint points;

						get_contour_shape(contours[c][i], frame, bb, out shape, out points);

						if (shape != -1)
						{
							Point mid;
							if (shape != GlobalValues.TRIANGLE)
							{
								mid = new Point((int)(bb.X + bb.Width / 2), (int)(bb.Y + bb.Height / 2));
							}
							else
							{
								mid = new Point((int)((points[0].X + points[1].X + points[2].X) / 3), (int)((points[0].Y + points[1].Y + points[2].Y) / 3));
							}
							int[] detection_color = { 0, 0, 0 };
							detection_color[c] += 1;

							Detection detection = new Detection(shape, bb, area, detection_color, points, mid);
							detections.Add(detection);

						}
					}
				}
			}
			return detections;
		}

		private void get_contour_shape(VectorOfPoint countur, Image<Bgr, byte> frame, Rectangle bb, out int shape, out VectorOfPoint points)
		{
			shape = -1;
			points = null;

			double my_arclength = CvInvoke.ArcLength(countur, true);
			VectorOfPoint approx = new VectorOfPoint();
			CvInvoke.ApproxPolyDP(countur, approx, 0.03 * my_arclength, true);

			int length = approx.Size;

			if (CvInvoke.IsContourConvex(approx))
			{
				if (length == 3)
				{
					double distance0 = GlobalValues.my_distance(approx[0], approx[1]);
					double distance1 = GlobalValues.my_distance(approx[1], approx[2]);
					double distance2 = GlobalValues.my_distance(approx[0], approx[2]);

					double my_mean = (distance0 + distance1 + distance2) / 3;

					bool wrong_size = (Math.Abs(distance0 - my_mean) > 0.1 * my_mean) || (Math.Abs(distance1 - my_mean) > 0.1 * my_mean)
						|| (Math.Abs(distance2 - my_mean) > 0.1 * my_mean);

					if (!wrong_size)
					{
						shape = GlobalValues.TRIANGLE;
						points = approx;
					}

				}
				else if (length == 4)
				{
					double distance0 = GlobalValues.my_distance(approx[0], approx[1]);
					double distance1 = GlobalValues.my_distance(approx[0], approx[2]);
					double distance2 = GlobalValues.my_distance(approx[0], approx[3]);
					double distance3 = GlobalValues.my_distance(approx[1], approx[2]);
					double distance4 = GlobalValues.my_distance(approx[1], approx[3]);
					double distance5 = GlobalValues.my_distance(approx[2], approx[3]);

					double[] distances = new double[] { distance0, distance1, distance2, distance3, distance4, distance5 };

					Array.Sort(distances);

					double my_mean = (distances[0] + distances[1] + distances[2] + distances[3]) / 4;

					bool wrong_size = (Math.Abs(distances[0] - my_mean) > 0.1 * my_mean) || (Math.Abs(distances[1] - my_mean) > 0.1 * my_mean)
						|| (Math.Abs(distances[2] - my_mean) > 0.1 * my_mean) || (Math.Abs(distances[3] - my_mean) > 0.1 * my_mean);

					if (!wrong_size)
					{
						shape = GlobalValues.SQUARE;
						points = approx;
					}
				}
				else if (length > 5)
				{
					int diff = (int)Math.Round((double)(1.0 * bb.Width / 10));

					int x = Math.Max(0, bb.X - diff);
					int y = Math.Max(0, bb.Y - diff);
					int x1 = Math.Min(GlobalValues.CAMERA_WIDTH, bb.X + bb.Width + diff);
					int y1 = Math.Min(GlobalValues.CAMERA_HEIGHT, bb.Y + bb.Height + diff);

					Rectangle roi = new Rectangle(x, y, x1 - x, y1 - y);

					frame.ROI = roi;

					Image<Bgr, byte> crop = frame.Copy();           ////////posprawdzaj kolory

					frame.ROI = Rectangle.Empty;

					Image<Bgr, byte> crop_resized = crop.Resize(100, 100, Inter.Linear);


					Image<Gray, byte> crop_gray = new Image<Gray, byte>(100, 100);
					CvInvoke.CvtColor(crop_resized, crop_gray, ColorConversion.Bgr2Gray);

					CircleF[] circles = CvInvoke.HoughCircles(crop_gray, HoughModes.Gradient, 1, 50, param1: 20, param2: 27, minRadius: 35, maxRadius: 55);

					if (circles.Length > 0)
					{
						shape = GlobalValues.CIRCLE;
					}

				}
			}



		}
	}
}
