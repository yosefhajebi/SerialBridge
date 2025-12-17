/*
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public static class Logger
{
    private static string logFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
    private static readonly long maxFileSizeBytes = 10 * 1024 * 1024; // 10MB
    private static readonly ConcurrentQueue<string> _queue = new ConcurrentQueue<string>();
    private static readonly AutoResetEvent _event = new AutoResetEvent(false);
    private static readonly object _initLock = new object();
    private static bool _running = false;

    private static string _currentLogFile;
    private static DateTime _currentDate;

    public static bool DebugMode { get; set; }
    public static bool ConsoleColorMode { get; set; } 

    static Logger()
    {
	    DebugMode = false;
	    ConsoleColorMode = true;
        EnsureFolder(logFolder);
        StartWorker();
    }

    public static void Init(string folderPath)
    {
        if (!string.IsNullOrWhiteSpace(folderPath))
            logFolder = folderPath;

        EnsureFolder(logFolder);
    }

    private static void EnsureFolder(string path)
    {
        try
        {
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
        }
        catch
        {
            logFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs_Fallback");
            Directory.CreateDirectory(logFolder);
        }
    }

    public static void Write(string msg)
    {
        try
        {
            //string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {msg}";
            string line = string.Format("{0:yyyy-MM-dd HH:mm:ss}  {1}", DateTime.Now, msg);

            _queue.Enqueue(line);

            if (ConsoleColorMode)
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine(line);
                Console.ResetColor();
            }

            _event.Set();
        }
        catch { }
    }

    public static void WriteFrame(byte[] frame)
    {
        if (frame == null || frame.Length < 8) return;

        string statusDesc = "Unknown";
        ConsoleColor color = ConsoleColor.White;

        switch (frame[1])
        {
            case 0xE0: statusDesc = "Unstable"; color = ConsoleColor.Yellow; break;
            case 0xE1: statusDesc = "Stable"; color = ConsoleColor.Green; break;
            case 0xE2: statusDesc = "Error"; color = ConsoleColor.Red; break;
            case 0xE3: statusDesc = "Overload"; color = ConsoleColor.Magenta; break;
        }

        string weightStr = Encoding.ASCII.GetString(frame, 2, 6);
        decimal weight;
        decimal.TryParse(weightStr.Insert(weightStr.Length - 3, "."), out weight);

        //string logLine = $"PU850 Frame → Status=0x{frame[1]:X2}({statusDesc}), Weight={weight:0.###}";
        string logLine = string.Format(
	        "PU850 Frame → Status=0x{0:X2}({1}), Weight={2:0.###}",
	        frame[1],
	        statusDesc,
	        weight
        );


        if (DebugMode)
        {
            Write("[FRAME] " + logLine);

            if (ConsoleColorMode)
            {
                Console.ForegroundColor = color;
                Console.WriteLine("[FRAME] " + logLine);
                Console.ResetColor();
            }
        }
    }

    private static void StartWorker()
    {
        lock (_initLock)
        {
            if (_running) return;

            _running = true;

            Task.Factory.StartNew(() =>
            {
                while (_running)
                {
                    _event.WaitOne(500);
                    ProcessQueue();
                }

                // پردازش باقی‌مانده‌ها
                ProcessQueue();
            }, TaskCreationOptions.LongRunning);
        }
    }

    private static void ProcessQueue()
    {
        EnsureLogFile();

        string line;
        while (_queue.TryDequeue(out line))
        {
            try
            {
                using (var fs = new FileStream(_currentLogFile, FileMode.Append, FileAccess.Write, FileShare.Read))
                using (var sw = new StreamWriter(fs, Encoding.UTF8))
                {
                    sw.WriteLine(line);
                }
            }
            catch { }
        }
    }

    private static void EnsureLogFile()
    {
        DateTime today = DateTime.Today;

        // ایجاد فایل روز جدید یا اگر فایل پر شده باشد
        if (_currentLogFile == null ||
            _currentDate != today ||
            (File.Exists(_currentLogFile) && new FileInfo(_currentLogFile).Length > maxFileSizeBytes))
        {
            _currentDate = today;

            string fileBase = Path.Combine(logFolder, today.ToString("yyyy-MM-dd"));
            string file = fileBase + ".log";
            int index = 1;

            while (File.Exists(file) &&
                   new FileInfo(file).Length > maxFileSizeBytes)
            {
                file = String.Format("{0}_{1}.log",fileBase,index);
                index++;
            }

            _currentLogFile = file;
        }
    }

    public static void Stop()
    {
        _running = false;
        _event.Set();
    }
}
*/
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public static class Logger
{
    private static readonly long MaxFileSizeBytes = 10 * 1024 * 1024; // 10MB
    private static readonly ConcurrentQueue<string> Queue = new ConcurrentQueue<string>();
    private static readonly AutoResetEvent Signal = new AutoResetEvent(false);

    private static bool _running;
    private static string _logFolder;
    private static string _currentLogFile;
    private static DateTime _currentDate;

    static Logger()
    {
        // مسیر استاندارد برای Windows Service با LocalSystem
        string basePath = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        _logFolder = Path.Combine(basePath, "SerialBridge", "Logs");

        EnsureFolder(_logFolder);
        StartWorker();
    }

    public static void Write(string message)
    {
        try
        {
            //string line = String.Format("{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}");
            string line = string.Format(
	            "{0:yyyy-MM-dd HH:mm:ss}  {1}",
	            DateTime.Now,
	            message
            );
            Queue.Enqueue(line);
            Signal.Set();
        }
        catch (Exception ex)
        {
            WriteEventLog("Logger enqueue error: " + ex.Message);
        }
    }

    private static void StartWorker()
    {
        if (_running) return;
        _running = true;

        Task.Factory.StartNew(() =>
        {
            while (_running)
            {
                Signal.WaitOne(1000);
                Flush();
            }

            Flush();
        }, TaskCreationOptions.LongRunning);
    }

    private static void Flush()
    {
        try
        {
            EnsureLogFile();

            using (var fs = new FileStream(_currentLogFile, FileMode.Append, FileAccess.Write, FileShare.Read))
            using (var sw = new StreamWriter(fs, Encoding.UTF8))
            {
	            string line;
	            while (Queue.TryDequeue(out line))
	            {
		            sw.WriteLine(line);
	            }
            }
        }
        catch (Exception ex)
        {
            WriteEventLog("Logger write error: " + ex.Message);
        }
    }

    private static void EnsureLogFile()
    {
        DateTime today = DateTime.Today;

        if (_currentLogFile == null ||
            _currentDate != today ||
            (File.Exists(_currentLogFile) && new FileInfo(_currentLogFile).Length > MaxFileSizeBytes))
        {
            _currentDate = today;

            string baseName = Path.Combine(_logFolder, today.ToString("yyyy-MM-dd"));
            string file = baseName + ".log";
            int index = 1;

            while (File.Exists(file) && new FileInfo(file).Length > MaxFileSizeBytes)
            {
				file = String.Format("{0}_{1}.log", baseName, index);
                index++;
            }

            _currentLogFile = file;
        }
    }

    private static void EnsureFolder(string path)
    {
        try
        {
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
        }
        catch (Exception ex)
        {
            WriteEventLog("Cannot create log folder: " + ex.Message);
        }
    }

    private static void WriteEventLog(string message)
    {
        try
        {
            if (!EventLog.SourceExists("YourServiceName"))
                EventLog.CreateEventSource("YourServiceName", "Application");

            EventLog.WriteEntry("YourServiceName", message, EventLogEntryType.Error);
        }
        catch
        {
            // آخرین پناه
            try
            {
                Directory.CreateDirectory(@"C:\Temp");
                File.AppendAllText(@"C:\Temp\ServiceLoggerFallback.log",
					String.Format("{0} - {1}\n", DateTime.Now, message));
            }
            catch { }
        }
    }

    public static void Stop()
    {
        _running = false;
        Signal.Set();
    }
}
