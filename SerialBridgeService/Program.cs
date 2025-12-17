using System;
using System.ServiceProcess;


namespace SerialBridgeService
{
	static class Program
	{
		static void Main()
		{
			#if DEBUG
				// اجرای مستقیم سرویس برای دیباگ
				SerialService svc = new SerialService();
				svc.DebugRun();
			#else
				// اجرای استاندارد سرویس ویندوز
				ServiceBase.Run(new ServiceBase[] { new SerialService() });
			#endif
		}
	}
}