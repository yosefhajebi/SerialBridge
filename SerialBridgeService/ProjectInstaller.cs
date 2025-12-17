using System.ComponentModel;
using System.ServiceProcess;
using System.Diagnostics;	   
using System.Configuration.Install;

namespace SerialBridgeService
{
    [RunInstaller(true)]
    public class ProjectInstaller : Installer
    {
        private ServiceProcessInstaller processInstaller;
        private ServiceInstaller serviceInstaller;
        private EventLogInstaller eventLogInstaller;

        public ProjectInstaller()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.processInstaller = new ServiceProcessInstaller();
            this.serviceInstaller = new ServiceInstaller();
            this.eventLogInstaller = new EventLogInstaller();

            // -------------------------------------------------
            // 1) اجرای سرویس با یک حساب کاربری محدود (Custom User)
            // -------------------------------------------------
            this.processInstaller.Account = ServiceAccount.User;
            this.processInstaller.Username = "DOMAIN\\SerialServiceUser"; // ← کاربر محدود در دامین/لوکال
            this.processInstaller.Password = "P@ssw0rd123";               // ← بهتر است بعداً با MSI ست شود

            // -------------------------------------------------
            // 2) تنظیمات سرویس
            // -------------------------------------------------
            this.serviceInstaller.ServiceName = "SerialBridgeService";
            this.serviceInstaller.DisplayName = "Serial Bridge Service";
            this.serviceInstaller.Description = "Reads COM port data and forwards it via TCP upon trigger.";
            this.serviceInstaller.StartType = ServiceStartMode.Automatic;
            this.serviceInstaller.DelayedAutoStart = true; // ← شروع تأخیری

            // -------------------------------------------------
            // 3) تنظیمات Service Recovery
            // -------------------------------------------------
			//this.serviceInstaller.FailureActionsFlag = true;
			//this.serviceInstaller.RestartDelay = 60000;
			//this.serviceInstaller.FailureActions = new FailureAction[]
			//{
			//	new FailureAction(RecoverAction.Restart, 2000),
			//	new FailureAction(RecoverAction.Restart, 2000),
			//	new FailureAction(RecoverAction.Restart, 2000)
			//};

            // -------------------------------------------------
            // 4) Event Log Installer
            // -------------------------------------------------
            this.eventLogInstaller.Source = "SerialBridgeService";
            this.eventLogInstaller.Log = "Application";

            // -------------------------------------------------
            // 5) Active InstallUtil Logging (با MSI سازگار)
            // -------------------------------------------------
            this.Installers.Add(new InstallerLogWriterInstaller());

            // -------------------------------------------------
            // 6) اضافه‌کردن همه‌ی Installers
            // -------------------------------------------------
            this.Installers.AddRange(new Installer[]
            {
                this.processInstaller,
                this.serviceInstaller,
                this.eventLogInstaller
            });
        }
    }

    // -------------------------------------------------
    // اضافه کردن یک Installer برای ثبت لاگ عمل نصب در فایل
    // -------------------------------------------------
    public class InstallerLogWriterInstaller : Installer
    {
        public override void Install(System.Collections.IDictionary stateSaver)
        {
            base.Install(stateSaver);
            Log("Install started.");
        }

        public override void Commit(System.Collections.IDictionary savedState)
        {
            base.Commit(savedState);
            Log("Install committed.");
        }

        public override void Rollback(System.Collections.IDictionary savedState)
        {
            base.Rollback(savedState);
            Log("Install rolled back.");
        }

        public override void Uninstall(System.Collections.IDictionary savedState)
        {
            base.Uninstall(savedState);
            Log("Service uninstalled.");
        }

        private void Log(string msg)
        {
            try
            {
                System.IO.File.AppendAllText("C\\SerialBridgeService_Install.log", msg + "");
            }
            catch { }
        }
    }
}