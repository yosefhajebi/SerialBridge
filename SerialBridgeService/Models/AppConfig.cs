using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace SerialBridgeService.Models
{
	[Serializable]
	public class SerialPortConfig
	{
		public string Name { get; set; }
		public int BaudRate { get; set; }
		public int DataBits { get; set; }
		public int Parity { get; set; }
		public int StopBits { get; set; }
		public int Handshake { get; set; }
		public int TimeOut { get; set; }
		public int DecimalPlaces { get; set; }
		
	}

	[Serializable]
	public class TcpTarget
	{
		public string Ip { get; set; }
		public int Port { get; set; }
	}

	[Serializable]
	public class TriggerConfig
	{
		public int Port { get; set; }
	}

	[Serializable]
	public class LoggingConfig
	{
		public string FilePath { get; set; }
	}

	[Serializable]
	public class AppConfig
	{
		public TriggerConfig Trigger { get; set; }
		public List<SerialPortConfig> SerialPorts { get; set; }
		public List<TcpTarget> TcpTargets { get; set; }
		public LoggingConfig Logging { get; set; }

		public static AppConfig Load()
		{
			string path = AppDomain.CurrentDomain.BaseDirectory + "appconfig.xml";
			XmlSerializer xs = new XmlSerializer(typeof(AppConfig));
			using (var fs = new FileStream(path, FileMode.Open))
				return (AppConfig)xs.Deserialize(fs);
		}
	}
}
