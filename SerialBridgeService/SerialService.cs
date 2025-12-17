using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;

namespace SerialBridgeService
{
	public partial class SerialService : ServiceBase
	{
		private CancellationTokenSource cts;

		public SerialService()
		{
			InitializeComponent();
		}

		protected override void OnStart(string[] args)
		{
			cts = new CancellationTokenSource();
			Task.Run(() => Run(cts.Token));
		}

		protected override void OnStop()
		{
			cts.Cancel();
		}

		public void DebugRun()
		{
			Run(CancellationToken.None);
		}

		private void Run(CancellationToken token)
		{
			var logic = new WorkerLogic();
			logic.Start(token);
		}
	}
}
