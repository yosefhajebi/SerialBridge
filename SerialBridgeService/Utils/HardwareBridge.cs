using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace SerialBridgeService.Utils
{
	/// <summary>
	/// Bridge برای انتشار وزن دستگاه به کلاینت‌ها
	/// </summary>
	public static class HardwareBridge
	{
		// Event داخلی برای هر کسی که می‌خواهد وزن را دریافت کند
		public static event EventHandler<WeightEventArgs> WeightReceived;

		// لیست کلاینت‌های TCP
		private static readonly List<TcpClient> tcpClients = new List<TcpClient>();
		private static readonly object clientLock = new object();

		/// <summary>
		/// ثبت یک کلاینت TCP جدید
		/// </summary>
		public static void RegisterClient(TcpClient client)
		{
			lock (clientLock)
			{
				tcpClients.Add(client);
			}
		}

		/// <summary>
		/// حذف یک کلاینت TCP
		/// </summary>
		public static void UnregisterClient(TcpClient client)
		{
			lock (clientLock)
			{
				if (tcpClients.Contains(client))
					tcpClients.Remove(client);
			}
		}

		/// <summary>
		/// انتشار وزن به همه کلاینت‌ها و Event داخلی
		/// </summary>
		public static void PublishWeight(WeightEventArgs e)
		{
			// انتشار Event داخلی
			var handler = WeightReceived;
			if (handler != null)
			{
				handler(null, e);
			}

			// انتشار به TCP کلاینت‌ها در Task جداگانه
			Task.Run(() =>
			{
				lock (clientLock)
				{
					var disconnected = new List<TcpClient>();

					foreach (var client in tcpClients)
					{
						try
						{
							if (!client.Connected)
							{
								disconnected.Add(client);
								continue;
							}

							var stream = client.GetStream();
							if (stream.CanWrite)
							{
								string msg = string.Format("{0:0.###}\r\n", e.Weight);
								byte[] data = Encoding.ASCII.GetBytes(msg);
								stream.Write(data, 0, data.Length);
							}
						}
						catch
						{
							disconnected.Add(client);
						}
					}

					// حذف کلاینت‌های قطع شده
					foreach (var dc in disconnected)
					{
						tcpClients.Remove(dc);
						try { dc.Close(); }
						catch { }
					}
				}
			});
		}
	}
}
