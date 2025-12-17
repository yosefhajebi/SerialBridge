using System;
using System.Net.Sockets;
using System.Text;

namespace SerialBridgeService.Utils
{
	public static class TcpUtil
	{
		public static void Send(string data, string ip, int port)
		{
		
			using (TcpClient client = new TcpClient())
			{
				// اتصال با انتظار 
				client.ConnectAsync(ip, port)
					  .GetAwaiter()
					  .GetResult();

				NetworkStream stream = client.GetStream();

				// ارسال فرمان
				string command = "SEND";
				byte[] dataq = Encoding.ASCII.GetBytes(data);

				stream.WriteAsync(dataq, 0, dataq.Length)
					  .GetAwaiter()
					  .GetResult();

				
			}
		}
	}
}