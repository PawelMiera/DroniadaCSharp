using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Droniada
{
	class Client
	{
		private TcpClient client;
		private NetworkStream stream;
		public bool clientConnected = false;

		public delegate void DataRecived(string data);
		public event DataRecived OnDataRecived;


		public string Ip;
		public int Port;



		public Client(string Ip, int Port)
		{
			this.Ip = Ip;
			this.Port = Port;
			client = new TcpClient();
		}

		public void connect()
		{
			Task t = Connect();
		}


		public async Task Connect()
		{
			try
			{
				if (client == null)
				{
					client = new TcpClient();
				}
				await client.ConnectAsync(Ip, Port);
				clientConnected = true;
				stream = client.GetStream();
				OnDataRecived("Connected");

			}
			catch (Exception ex)
			{
				OnDataRecived("Error Connecting" + ex.ToString());
			}
		}

		public void sendMessage(string msg)
		{
			if (client.Connected)
			{
				try
				{
					Byte[] data = Encoding.ASCII.GetBytes(msg);
					if (stream != null)
						stream.Write(data, 0, data.Length);
					else
					{
						stream = client.GetStream();
						stream.Write(data, 0, data.Length);
					}
				}
				catch (Exception ex)
				{
					OnDataRecived("Error Connecting" + ex.ToString());
					client.Close();
					stream.Close();
					client = new TcpClient();
					Task t = Connect();
				}
			}
			else
			{
				OnDataRecived("Error Connecting");
			}
		}
	}
}
