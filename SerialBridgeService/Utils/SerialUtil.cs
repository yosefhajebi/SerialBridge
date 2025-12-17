/*
using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SerialBridgeService.Utils
{
	public class WeightEventArgs : EventArgs
	{
		public decimal Weight { get; private set; }
		public bool Stable { get; private set; }
		public bool Error { get; private set; }
		public bool Overload { get; private set; }


		public WeightEventArgs(decimal weight, bool stable, bool error, bool overload)
		{
			Weight = weight;
			Stable = stable;
			Error = error;
			Overload = overload;
		}

		public string WeightString
		{
			get { return string.Format("{0:0.###}", Weight); }
		}
	}

	public class SerialUtil : IDisposable
	{
		private SerialPort _port;
		private TaskCompletionSource<decimal> _tcs;
		private volatile bool _closing;
		private volatile bool _readingActive = false; // فقط وقتی خواندن فعال است پردازش انجام شود

		private readonly Queue<byte[]> _queue = new Queue<byte[]>();
		private readonly AutoResetEvent _event = new AutoResetEvent(false);
		private Task _worker;

		private readonly List<byte> _buffer = new List<byte>();
		private readonly object _lock = new object();

		public decimal LastWeight { get; private set; }
		public bool LastStable { get; private set; }
		public bool LastError { get; private set; }
		public bool LastOverload { get; private set; }
		private int _currentDecimalPlaces = 0;

		public event EventHandler<WeightEventArgs> WeightUpdated;

		public SerialUtil()
		{
			StartWorker();
		}

		public void ReadCom(string portName, int baud, int dataBits, int parity, int stopBits, int handshake)
		{
			ClosePort();

			_port = new SerialPort(portName, baud, (Parity)parity, dataBits, (StopBits)stopBits);
			_port.Handshake = (Handshake)handshake;
			_port.Encoding = Encoding.ASCII;
			_port.DataReceived += OnData;

			try
			{
				_port.Open();
				Logger.Write(string.Format("Serial port opened: {0}", portName));
			}
			catch (Exception ex)
			{
				Logger.Write(string.Format("Cannot open serial port {0}: {1}", portName, ex.Message));
			}
		}

		private void OnData(object sender, SerialDataReceivedEventArgs e)
		{
			if (_closing) return;

			try
			{
				int count = _port.BytesToRead;
				if (count <= 0) return;

				byte[] data = new byte[count];
				_port.Read(data, 0, count);

				lock (_queue)
					_queue.Enqueue(data);

				_event.Set();
			}
			catch { }
		}

		private void StartWorker()
		{
			_worker = Task.Factory.StartNew(() =>
			{
				while (!_closing)
				{
					_event.WaitOne();
					if (_closing) break;

					byte[] chunk = null;
					lock (_queue)
					{
						if (_queue.Count > 0)
							chunk = _queue.Dequeue();
					}

					if (chunk != null)
						ProcessChunk(chunk);
				}
			}, TaskCreationOptions.LongRunning);
		}
		private static string ToHex(IEnumerable<byte> data)
		{
			return string.Join(" ",
				data.Select(b => b.ToString("X2")));
		}
		
		public void SetPrecision(int decimalPlaces=0)
		{
			_currentDecimalPlaces = decimalPlaces;
		}
		private static decimal ApplyPrecision(string asciiWeight, int decimalPlaces)
		{
			decimal raw;
			if (!decimal.TryParse(asciiWeight, out raw))
				return -1;

			if (decimalPlaces <= 0)
				return raw;

			return raw / (decimal)Math.Pow(10, decimalPlaces);
		}
		private readonly Queue<decimal> _recentWeights = new Queue<decimal>();

		private void ProcessChunk(byte[] chunk)
		{
		    if (!_readingActive) return; // فقط وقتی خواندن فعال است پردازش شود

		    lock (_lock)
		    {
		        _buffer.AddRange(chunk);

		        while (true)
		        {
		            int idx = _buffer.FindIndex(b => b == 0xBB);
		            if (idx < 0) break;

		            // حداقل طول فریم: 8 بایت برای وزن مثبت، 9 برای منفی
		            if (_buffer.Count <= idx + 7) break;

		            // لاگ کل بافر به صورت HEX
		            Logger.Write(string.Format("PU850 Buffer HEX → {0}", ToHex(_buffer)));

		            byte status = _buffer[idx + 1];
		            bool isNegative = false;

		            if (status == 0xE0)
		            {
		                isNegative = true;
		                // طول فریم 9 بایت برای وزن منفی
		                if (_buffer.Count <= idx + 8) break;
		                status = _buffer[idx + 2]; // وضعیت واقعی بعد از MSB
		            }

		            // بررسی وضعیت
		            bool isStable = true;//status == 0xE1;
		            bool isError = status == 0xE2;
		            bool isOverload = status == 0xE3;

		            // 6 بایت وزن ASCII
		            int weightStart = isNegative ? idx + 3 : idx + 2;
		            byte[] w6 = _buffer.GetRange(weightStart, 6).ToArray();
		            string s = Encoding.ASCII.GetString(w6);

		            // اعمال Precision
		            int decimalPlaces = _currentDecimalPlaces;
		            s = new string(s.Where(c => char.IsDigit(c) || c == '.' || c == '-').ToArray());
		            decimal weight = ApplyPrecision(s, decimalPlaces);

		            if (isNegative)
		                weight = -weight;

		            LastWeight = weight;
		            LastStable = isStable;
		            LastError = isError;
		            LastOverload = isOverload;

		            if (isStable)
		            {
		                Logger.Write(string.Format(
		                    "PU850 Stable Frame → Raw={0}, Precision={1}, Weight={2}",
		                    s,
		                    decimalPlaces,
		                    weight
		                ));

		                var handler = WeightUpdated;
		                if (handler != null)
			                handler(this, new WeightEventArgs(weight, isStable, isError, isOverload));

		                if (_tcs != null && !_tcs.Task.IsCompleted)
		                    _tcs.TrySetResult(weight);
		            }
		            else if ((isError || isOverload) && _tcs != null && !_tcs.Task.IsCompleted)
		            {
		                _tcs.TrySetException(new Exception(isError ? "PU850 Error" : "PU850 Overload"));
		            }

		            // حذف فریم پردازش شده
		            int frameLength = isNegative ? 9 : 8;
		            _buffer.RemoveRange(idx, frameLength);
		        }
		    }
		}
		
		public async Task<decimal> ReadStableAsync(int timeoutMs = 20000)
		{
			_readingActive = true; // فعال کردن فلگ خواندن
			_tcs = new TaskCompletionSource<decimal>();

			// Task تاخیر برای Timeout
			var delayTask = Task.Delay(timeoutMs);

			// منتظر می‌مانیم تا یا وزن پایدار دریافت شود یا Timeout رخ دهد
			var finished = await Task.WhenAny(_tcs.Task, delayTask);

			if (finished == delayTask)
			{
				// Timeout رخ داد و وزن پایدار دریافت نشد
				if (!_tcs.Task.IsCompleted)
					_tcs.TrySetResult(-1m); // مقدار -1 برای عدم دریافت وزن پایدار
			}

			decimal result = await _tcs.Task;
			_readingActive = false; // بعد از پایان خواندن فلگ غیرفعال شود
			return result;
		}


		private void ClosePort()
		{
			if (_port != null)
			{
				try { _port.DataReceived -= OnData; }
				catch { }
				try { if (_port.IsOpen) _port.Close(); }
				catch { }
				try { _port.Dispose(); }
				catch { }
			}

			_port = null;
		}

		public void Close()
		{
			_closing = true;
			_event.Set();
			ClosePort();
		}

		public void Dispose()
		{
			Close();
		}
	}
}
*/
using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SerialBridgeService.Utils
{
	public class WeightEventArgs : EventArgs
	{
		public decimal Weight { get; private set; }
		public bool Stable { get; private set; }
		public bool Error { get; private set; }
		public bool Overload { get; private set; }

		public WeightEventArgs(decimal weight, bool stable, bool error, bool overload)
		{
			Weight = weight;
			Stable = stable;
			Error = error;
			Overload = overload;
		}
	}

	public class SerialUtil : IDisposable
	{
		private SerialPort _port;
		private TaskCompletionSource<decimal> _tcs;
		private volatile bool _closing;
		private volatile bool _readingActive;

		private readonly Queue<byte[]> _queue = new Queue<byte[]>();
		private readonly AutoResetEvent _event = new AutoResetEvent(false);
		private Task _worker;
		private readonly List<byte> _buffer = new List<byte>();
		private readonly object _lock = new object();

		private int _currentDecimalPlaces;

		private const int STABLE_SAMPLE_COUNT = 3;       // حداقل نمونه پایدار
		private const decimal STABLE_TOLERANCE = 0.3m;  // دامنه تغییرات
		private const int STABLE_TIME_MS = 500;         // حداقل مدت زمان برای پایدار بودن

		private readonly Queue<Tuple<decimal, DateTime>> _stableSamples = new Queue<Tuple<decimal, DateTime>>();

		public decimal LastWeight { get; private set; }
		public bool LastStable { get; private set; }
		public bool LastError { get; private set; }
		public bool LastOverload { get; private set; }

		public event EventHandler<WeightEventArgs> WeightUpdated;

		public SerialUtil()
		{
			StartWorker();
		}

		public void ReadCom(string portName, int baud, int dataBits, int parity, int stopBits, int handshake)
		{
			ClosePort();

			_port = new SerialPort(portName, baud, (Parity)parity, dataBits, (StopBits)stopBits)
			{
				Handshake = (Handshake)handshake,
				Encoding = Encoding.ASCII
			};
			_port.DataReceived += OnData;

			try
			{
				_port.Open();
				Logger.Write(string.Format("Serial port opened: {0}", portName));
			}
			catch (Exception ex)
			{
				Logger.Write(string.Format("Cannot open serial port {0}: {1}", portName, ex.Message));
			}
		}

		private void OnData(object sender, SerialDataReceivedEventArgs e)
		{
			if (_closing) return;

			int count = _port.BytesToRead;
			if (count <= 0) return;

			byte[] data = new byte[count];
			_port.Read(data, 0, count);

			lock (_queue)
				_queue.Enqueue(data);

			_event.Set();
		}

		private void StartWorker()
		{
			_worker = Task.Factory.StartNew(() =>
			{
				while (!_closing)
				{
					_event.WaitOne();
					if (_closing) break;

					byte[] chunk = null;
					lock (_queue)
						if (_queue.Count > 0)
							chunk = _queue.Dequeue();

					if (chunk != null)
						ProcessChunk(chunk);
				}
			}, TaskCreationOptions.LongRunning);
		}

		public void SetPrecision(int decimalPlaces)
		{
			_currentDecimalPlaces = decimalPlaces;
		}

		private static decimal ApplyPrecision(string asciiWeight, int decimalPlaces)
		{
			decimal raw;
			if (!decimal.TryParse(asciiWeight, out raw))
				return -1;

			if (decimalPlaces <= 0)
				return raw;

			return raw / (decimal)Math.Pow(10, decimalPlaces);
		}

		private void ProcessChunk(byte[] chunk)
		{
			if (!_readingActive) return;

			lock (_lock)
			{
				_buffer.AddRange(chunk);

				while (true)
				{
					int idx = _buffer.FindIndex(b => b == 0xBB);
					if (idx < 0) break;
					if (_buffer.Count <= idx + 7) break;

					Logger.Write(string.Format("PU850 FRAME HEX → {0}", ToHex(_buffer)));

					byte status = _buffer[idx + 1];
					bool isNegative = false;

					if (status == 0xE0)
					{
						isNegative = true;
						if (_buffer.Count <= idx + 8) break;
						status = _buffer[idx + 2];
					}

					bool isError = status == 0xE2;
					bool isOverload = status == 0xE3;

					int weightStart = isNegative ? idx + 3 : idx + 2;
					byte[] w6 = _buffer.GetRange(weightStart, 6).ToArray();
					string s = Encoding.ASCII.GetString(w6);
					s = new string(s.Where(c => char.IsDigit(c) || c == '-').ToArray());
					decimal weight = ApplyPrecision(s, _currentDecimalPlaces);
					if (isNegative) weight = -weight;

					LastWeight = weight;
					LastError = isError;
					LastOverload = isOverload;

					Logger.Write(string.Format("Weight parsed: {0}", weight));

					// نمونه‌های پایدار با زمان
					DateTime now = DateTime.UtcNow;
					_stableSamples.Enqueue(Tuple.Create(weight, now));

					while (_stableSamples.Count > STABLE_SAMPLE_COUNT)
						_stableSamples.Dequeue();

					bool stable = false;
					if (_stableSamples.Count == STABLE_SAMPLE_COUNT)
					{
						decimal max = _stableSamples.Max(t => t.Item1);
						decimal min = _stableSamples.Min(t => t.Item1);
						TimeSpan span = _stableSamples.Last().Item2 - _stableSamples.First().Item2;

						stable = (max - min) <= STABLE_TOLERANCE && span.TotalMilliseconds >= STABLE_TIME_MS;
					}

					LastStable = stable;

					if (stable)
					{
						Logger.Write(string.Format("Stable weight detected: {0}", weight));

						if (WeightUpdated != null)
							WeightUpdated(this, new WeightEventArgs(weight, true, isError, isOverload));

						if (_tcs != null && !_tcs.Task.IsCompleted)
							_tcs.TrySetResult(weight);
					}

					if ((isError || isOverload) && _tcs != null && !_tcs.Task.IsCompleted)
						_tcs.TrySetException(new Exception(isError ? "PU850 Error" : "PU850 Overload"));

					int frameLength = isNegative ? 9 : 8;
					_buffer.RemoveRange(idx, frameLength);
				}
			}
		}

		private static string ToHex(IEnumerable<byte> data)
		{
			return string.Join("-", data.Select(b => b.ToString("X2")));
		}

		public async Task<decimal> ReadStableAsync(int timeoutMs = 20000)
		{
			_readingActive = true;
			_tcs = new TaskCompletionSource<decimal>();

			Task delay = Task.Delay(timeoutMs);
			Task finished = await Task.WhenAny(_tcs.Task, delay);

			if (finished == delay && !_tcs.Task.IsCompleted)
			{
				Logger.Write("ReadStableAsync timeout");
				_tcs.TrySetResult(-1);
			}

			decimal result = await _tcs.Task;
			_readingActive = false;
			Logger.Write(string.Format("Read stable weight {0}", result));
			return result;
		}

		private void ClosePort()
		{
			if (_port == null) return;
			try { _port.DataReceived -= OnData; }
			catch { }
			try { _port.Close(); }
			catch { }
			try { _port.Dispose(); }
			catch { }
			_port = null;
		}

		public void Close()
		{
			_closing = true;
			_event.Set();
			ClosePort();
		}

		public void Dispose()
		{
			Close();
		}
	}
}
