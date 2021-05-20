using GeographicLib;
using System;
using System.Drawing;

namespace Droniada
{
	class PositionCalculator
	{
		double vertical_constant;
		double horizontal_constant;
		double meters_per_pixel_vertical;
		double meters_per_pixel_horizontal;

		double max_meters_vertical;
		double max_meters_horizontal;

		double max_meters_area;

		Geodesic geod;

		GeodesicLocation point_lu;
		GeodesicLocation point_ld;
		GeodesicLocation point_ru;
		GeodesicLocation point_rd;
		GeodesicLocation currentLocation;

		double[] line_u;
		double[] line_d;
		double[] line_l;
		double[] line_r;

		double distance_vertical_geo;
		double distance_horizontal_geo;

		double beta;

		Telemetry telemetry;

		public PositionCalculator(Telemetry telemetry)
		{
			vertical_constant = calculate_vertical_constant();
			horizontal_constant = calculate_horizontal_constant();

			geod = Geodesic.WGS84;

			this.telemetry = telemetry;
			beta = rad2deg(Math.Atan(GlobalValues.CAMERA_WIDTH / GlobalValues.CAMERA_HEIGHT));
		}

		public void update_current_location()
		{
			currentLocation = new GeodesicLocation(telemetry.gps_location.latitude, telemetry.gps_location.longitude);
		}

		double calculate_vertical_constant()
		{
			double constant = 2 * Math.Tan(deg2rad(GlobalValues.VERTICAL_ANGLE / 2)) / GlobalValues.CAMERA_HEIGHT;

			return constant;
		}

		double calculate_horizontal_constant()
		{
			double constant = 2 * Math.Tan(deg2rad(GlobalValues.HORIZONTAL_ANGLE / 2)) / GlobalValues.CAMERA_WIDTH;
			return constant;
		}

		public void update_meters_per_pixel()
		{
			meters_per_pixel_vertical = calculate_meters_per_pixel_vertical(telemetry.altitude);
			meters_per_pixel_horizontal = calculate_meters_per_pixel_horizontal(telemetry.altitude);
		}

		double calculate_meters_per_pixel_vertical(double altitude)
		{
			return vertical_constant * altitude;
		}

		double calculate_meters_per_pixel_horizontal(double altitude)
		{
			return horizontal_constant * altitude;
		}

		void calculate_max_meters_horizontal()
		{
			/*
			Need to run update_meters_per_pixel() first!!!
			*/
			max_meters_horizontal = meters_per_pixel_horizontal * GlobalValues.CAMERA_WIDTH;
		}

		void calculate_max_meters_vertical()
		{
			/*
			Need to run update_meters_per_pixel() first!!!
			*/
			max_meters_vertical = meters_per_pixel_vertical * GlobalValues.CAMERA_HEIGHT;
		}

		public void calculate_max_meters_area()
		{
			/*
			Need to run update_meters_per_pixel() first!!!
			*/
			calculate_max_meters_vertical();
			calculate_max_meters_horizontal();

			max_meters_area = max_meters_vertical * max_meters_horizontal;
		}

		public void calculate_extreme_points()
		{
			/*
			Need to run calculate_max_meters_horizontal() and calculate_max_meters_vertical() first and calculate_current_location!!!
			*/

			double distance = Math.Sqrt(((max_meters_horizontal / 2) * (max_meters_horizontal / 2)) + ((max_meters_vertical / 2) * (max_meters_vertical / 2)));

			double a1 = telemetry.attitude.yaw - beta;
			double a2 = telemetry.attitude.yaw - 180 + beta;

			double a3 = telemetry.attitude.yaw + beta;
			double a4 = telemetry.attitude.yaw + 180 - beta;

			// print("{:.8f},{:.8f}".format(latitude, longitude))

			point_lu = geod.Location(currentLocation, a1, distance);

			//print("{:.8f},{:.8f}".format(g['lat2'], g['lon2']))

			point_ld = geod.Location(currentLocation, a2, distance);

			//print("{:.8f},{:.8f}".format(g['lat2'], g['lon2']))

			point_ru = geod.Location(currentLocation, a3, distance);

			//print("{:.8f},{:.8f}".format(g['lat2'], g['lon2']))

			point_rd = geod.Location(currentLocation, a4, distance);

			//print("{:.8f},{:.8f}".format(g['lat2'], g['lon2']))

			line_u = get_line_factors(point_lu, point_ru);
			line_d = get_line_factors(point_ld, point_rd);
			line_l = get_line_factors(point_lu, point_ld);
			line_r = get_line_factors(point_ru, point_rd);


			distance_vertical_geo = calculate_point_to_line_distance(point_lu, line_d);

			distance_horizontal_geo = calculate_point_to_line_distance(point_lu, line_r);
		}

		double[] get_line_factors(GeodesicLocation p1, GeodesicLocation p2)
		{
			double a = p1.Longitude - p2.Longitude;
			double b = p2.Latitude - p1.Latitude;

			double c = p1.Latitude * p2.Longitude - p2.Latitude * p1.Longitude;
			return new double[] {a, b, c};
		}

		double calculate_point_to_line_distance(GeodesicLocation point, double[] line)
		{
			double distance = Math.Abs(line[0] * point.Latitude + line[1] * point.Longitude + line[2]);

			distance /= Math.Sqrt((line[0] * line[0]) + (line[1] * line[1]));

			return distance;
		}

		public GeodesicLocation calculate_point_lat_long(Point point)
		{
			double x = point.X - GlobalValues.CAMERA_WIDTH_HALF;
			double y = GlobalValues.CAMERA_HEIGHT_HALF - point.Y;

			double angle = rad2deg(Math.Atan2(x, y));

			double end_angle = angle + telemetry.attitude.yaw;

			x *= meters_per_pixel_horizontal;
			y *= meters_per_pixel_vertical;

			double distance = Math.Sqrt((x * x) + (y * y));

			return geod.Location(currentLocation, end_angle, distance);

		}

		public double calculate_area_in_meters_2(double area)
		{

			double area_m = area * max_meters_area / GlobalValues.MAX_PIXEL_AREA;

			return area_m;
		}

		public Point get_detection_on_image_cords(GeodesicLocation p)
		{
			double du = calculate_point_to_line_distance(p, line_u);

			double dd = calculate_point_to_line_distance(p, line_d);

			double dl = calculate_point_to_line_distance(p, line_l);

			double dr = calculate_point_to_line_distance(p, line_r);

			if(du <= distance_vertical_geo && dd <= distance_vertical_geo && dl <= distance_horizontal_geo && dr <= distance_horizontal_geo)
			{
				double x = dl / distance_horizontal_geo * GlobalValues.CAMERA_WIDTH;
				double y = du / distance_vertical_geo * GlobalValues.CAMERA_HEIGHT;

				return new Point((int)x, (int)y);
			}
			else
			{
				return Point.Empty;
			}
		}

		private double deg2rad(double deg)
		{
			return deg* Math.PI / 180.0;
		}

		private double rad2deg(double rad)
		{
			return (180 / Math.PI) * rad;
		}
	}
}
