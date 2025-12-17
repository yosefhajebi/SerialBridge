using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SerialBridgeService.Models;
using SerialBridgeService.Utils;

namespace SerialBridgeService
{
	//public class WorkerLogic
	//{
	//	private readonly AppConfig config;
	//	private readonly SemaphoreSlim comLock = new SemaphoreSlim(1, 1);
	//	private readonly SerialUtil scale;

	//	public WorkerLogic()
	//	{
	//		config = AppConfig.Load();
	//		Logger.Init(config.Logging.FilePath);

	//		// SerialUtil event-driven
	//		scale = new SerialUtil();
	//		scale.WeightUpdated += Scale_WeightUpdated;
	//	}

	//	// Event برای انتشار داده به HardwareBridge
	//	private void Scale_WeightUpdated(object sender, WeightEventArgs e)
	//	{
	//		HardwareBridge.PublishWeight(e);
	//	}

	//	public void Start(CancellationToken token)
	//	{
	//		Logger.Write("Service started.");

	//		var listener = new TcpListener(IPAddress.Any, config.Trigger.Port);
	//		listener.Start();
	//		Logger.Write("Trigger listener started on port " + config.Trigger.Port);

	//		try
	//		{
	//			while (!token.IsCancellationRequested)
	//			{
	//				if (listener.Pending())
	//				{
	//					TcpClient client = listener.AcceptTcpClient();
	//					// برای .NET 4.5: بدون استفاده از `_ =`
	//					HandleTrigger(client);
	//				}
	//				Thread.Sleep(10);
	//			}
	//		}
	//		finally
	//		{
	//			listener.Stop();
	//		}
	//	}

	//	private async void HandleTrigger(TcpClient client)
	//	{
	//		try
	//		{
	//			NetworkStream stream = client.GetStream();
	//			byte[] buffer = new byte[1024];
	//			int len = await stream.ReadAsync(buffer, 0, buffer.Length);
	//			string cmd = Encoding.UTF8.GetString(buffer, 0, len).Trim();

	//			Logger.Write("Trigger received: " + cmd);

	//			if (cmd.StartsWith("SEND", StringComparison.OrdinalIgnoreCase))
	//			{
	//				string[] parts = cmd.Split(':');
	//				if (parts.Length != 2)
	//				{
	//					Logger.Write("Invalid command format. Expected SEND:COMx");
	//					return;
	//				}

	//				string targetPortName = parts[1].Trim().ToUpper();
	//				var portConfig = config.SerialPorts.FirstOrDefault(p => p.Name.ToUpper() == targetPortName);

	//				if (portConfig == null)
	//				{
	//					Logger.Write(string.Format("Port {0} not found in config.", targetPortName));
	//					return;
	//				}

	//				string weightStr = "-1";

	//				await comLock.WaitAsync(); // جلوگیری از باز شدن همزمان پورت
	//				try
	//				{
	//					scale.ReadCom(
	//						portConfig.Name,
	//						portConfig.BaudRate,
	//						portConfig.DataBits,
	//						portConfig.Parity,
	//						portConfig.StopBits,
	//						portConfig.Handshake
	//					);

	//					// دریافت وزن پایدار با Task
	//					decimal stableWeight = await scale.ReadStableAsync();
	//					weightStr = stableWeight.ToString("0.###");

	//					Logger.Write(string.Format("Read stable weight {0} from {1}", weightStr, portConfig.Name));
	//				}
	//				catch (Exception ex)
	//				{
	//					Logger.Write(string.Format("Serial read error ({0}): {1}", targetPortName, ex.Message));
	//					weightStr = "-1";
	//				}
	//				finally
	//				{
	//					comLock.Release();
	//				}

	//				// ارسال وزن به همان کلاینت TCP
	//				try
	//				{
	//					byte[] responseBytes = Encoding.ASCII.GetBytes(weightStr + "\r\n");
	//					await stream.WriteAsync(responseBytes, 0, responseBytes.Length);
	//					Logger.Write("Sent weight " + weightStr + " to TCP client");
	//				}
	//				catch (Exception ex)
	//				{
	//					Logger.Write("TCP send error: " + ex.Message);
	//				}
	//			}

	//			stream.Close();
	//			client.Close();
	//		}
	//		catch (Exception ex)
	//		{
	//			Logger.Write("Trigger error: " + ex.Message);
	//		}
	//	}
	//}
	public class WorkerLogic
{
    private readonly AppConfig config;
    private readonly SemaphoreSlim comLock = new SemaphoreSlim(1, 1);
    private readonly SerialUtil scale;

    public WorkerLogic()
    {
	    
	    config = AppConfig.Load();
        //Logger.Init(config.Logging.FilePath);
        scale = new SerialUtil();
        scale.WeightUpdated += Scale_WeightUpdated;
    }

    // فلگ برای لاگ Event فقط یک بار در هر Trigger
    private bool _eventWeightLogged = false;

    private void Scale_WeightUpdated(object sender, WeightEventArgs e)
    {
        if (!_eventWeightLogged)
        {
            //Logger.Write($"WeightUpdated Event → Weight={e.Weight:0.###}");
            Logger.Write(string.Format("WeightUpdated Event → Weight={0:0.###}", e.Weight));

            _eventWeightLogged = true; // بعد از اولین لاگ، دیگر Event لاگ نمی‌کند
        }

        HardwareBridge.PublishWeight(e); // انتشار همواره انجام شود
    }

    public void Start(CancellationToken token)
    {
        Logger.Write("Service started.");

        var listener = new TcpListener(IPAddress.Any, config.Trigger.Port);
        listener.Start();
        Logger.Write("Trigger listener started on port " + config.Trigger.Port);

        try
        {
            while (!token.IsCancellationRequested)
            {
                if (listener.Pending())
                {
                    TcpClient client = listener.AcceptTcpClient();
                    HandleTrigger(client);
                }
                Thread.Sleep(10);
            }
        }
        finally
        {
            listener.Stop();
        }
    }

	private async void HandleTrigger(TcpClient client)
	{
		try
		{
			NetworkStream stream = client.GetStream();
			byte[] buffer = new byte[1024];
			int len = await stream.ReadAsync(buffer, 0, buffer.Length);
			string cmd = Encoding.UTF8.GetString(buffer, 0, len).Trim();

			Logger.Write("Trigger received: " + cmd);

			if (cmd.StartsWith("SEND", StringComparison.OrdinalIgnoreCase))
			{
				string[] parts = cmd.Split(':');
				if (parts.Length != 2)
				{
					Logger.Write("Invalid command format. Expected SEND:COMx");
					client.Close();
					return;
				}

				string targetPortName = parts[1].Trim().ToUpper();
				var portConfig = config.SerialPorts.FirstOrDefault(p => p.Name.ToUpper() == targetPortName);

				if (portConfig == null)
				{
					Logger.Write(string.Format("Port {0} not found in config.", targetPortName));
					client.Close();
					return;
				}

				string weightStr = "-1";

				await comLock.WaitAsync();
				try
				{
					scale.SetPrecision(portConfig.DecimalPlaces);
					scale.ReadCom(
						portConfig.Name,
						portConfig.BaudRate,
						portConfig.DataBits,
						portConfig.Parity,
						portConfig.StopBits,
						portConfig.Handshake
					);

					// فقط وزن پایدار را دریافت کن
					decimal stableWeight = await scale.ReadStableAsync(portConfig.TimeOut);
					
					weightStr = stableWeight.ToString("0.###");

					Logger.Write(string.Format("Read stable weight {0} from {1}", weightStr, portConfig.Name));
				}
				catch (Exception ex)
				{
					Logger.Write(string.Format("Serial read error ({0}): {1}", targetPortName, ex.Message));
					weightStr = "-1";
				}
				finally
				{
					comLock.Release();
				}

				try
				{
					byte[] responseBytes = Encoding.ASCII.GetBytes(weightStr + "\r\n");
					await stream.WriteAsync(responseBytes, 0, responseBytes.Length);
					Logger.Write("Sent weight " + weightStr + " to TCP client");
				}
				catch (Exception ex)
				{
					Logger.Write("TCP send error: " + ex.Message);
				}
			}

			stream.Close();
			client.Close();
		}
		catch (Exception ex)
		{
			Logger.Write("Trigger error: " + ex.Message);
		}
	}

}

}
