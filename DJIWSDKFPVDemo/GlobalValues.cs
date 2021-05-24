using System;
using System.Drawing;

namespace Droniada
{
	class GlobalValues
	{
		public static int MODE = 3;     // 0 - local detection,  1 - save smart, 2 - smart mission, 3 - python, 4 - pyhon test

		public static int CAMERA = 0;

		public static int MIN_ALTITUDE = 4;

		public static int MIN_AREA = 800;
		public static double MAX_LONG_LAT_DIFF = 0.00004;
		public static double MAX_AREA_DIFF = 0.5;

		public static double HORIZONTAL_ANGLE = 60;
		public static double VERTICAL_ANGLE = 36;


		public static int TRIANGLE = 0;
		public static int SQUARE = 1;
		public static int CIRCLE = 2;

		public static int WHITE = 0;
		public static int ORANGE = 1;
		public static int BROWN = 2;

		public static int PORT = 6969;
		public static string HOST = "127.0.0.1";



		public static int CAMERA_WIDTH = 1280;
		public static int CAMERA_HEIGHT = 720;
		public static int CAMERA_WIDTH_HALF = CAMERA_WIDTH / 2;
		public static int CAMERA_HEIGHT_HALF = CAMERA_HEIGHT / 2;

		public static int MAX_PIXEL_AREA = CAMERA_WIDTH * CAMERA_HEIGHT;

		public static int BOX_SIZE_INCREASE = 10;

		public static bool PRINT_FPS = true;

		public static double my_distance(Point v, Point u)
		{
			double s = ((v.X - u.X) * (v.X - u.X)) + ((v.Y - u.Y) * (v.Y - u.Y));

			return Math.Sqrt(s);
		}

	}
}
