using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Serilog.Debugging;

namespace Serilog.Sinks.InsightOps.Rapid7
{
     public sealed class AsyncLogger
    {
        // Size of the internal event queue.
        private const int QueueSize = 32768;

        // Limit on individual log length i.e. 2^16
        private const int LogLengthLimit = 65536;

        // Limit on recursion for appending long logs to queue
        private const int RecursionLimit = 32;

        // Minimal delay between attempts to reconnect in milliseconds.
        private const int MinDelay = 100;

        // Maximal delay between attempts to reconnect in milliseconds.
        private const int MaxDelay = 10000;

        // Appender signature - used for debugging messages.
        private const string InternalLogPrefix = "R7Insight: {0}";

        // Error message displayed when invalid token is detected.
        private const string InvalidTokenMessage = "\n\nIt appears your log token value is invalid or missing.\n\n";

        // Error message displayed when queue overflow occurs.
        private const string QueueOverflowMessage = "\n\nInsight logger buffer queue overflow. Message dropped.\n\n";

        // Error message displayed when region is not provided.
        private const string NoRegionMessage = "\n\nNo region is configured, please make sure one is configured; e.g: 'eu', 'us'.\n\n";

        // Newline char to trim from message for formatting.
        private static readonly char[] _trimChars = { '\r', '\n' };

#if NETSTANDARD2_0
        /** Non-Unix and Unix Newline */
        private static readonly string[] _posixNewline = { "\r\n", "\n" };
#endif

        /** Linux new-line */
        private const char NixNewLine = '\n';

        /** Unicode line separator character */
        private const string LineSeparator = "\u2028";

        // Restricted symbols that should not appear in host name.
        // See http://support.microsoft.com/kb/228275/en-us for details.
        private static readonly Regex _forbiddenHostNameChars = new Regex(@"[/\\\[\]\""\:\;\|\<\>\+\=\,\?\* _]{1,}", RegexOptions.Compiled);

        // UTF-8 output character set.
        private static readonly UTF8Encoding _utf8 = new UTF8Encoding(false,true);

        //static list of all the queues the log-appender might be managing.
        private static readonly ConcurrentBag<BlockingCollection<string>> _allQueues = new ConcurrentBag<BlockingCollection<string>>();

        /// <summary>
        /// Determines if the queue is empty after waiting the specified waitTime.
        /// Returns true or false if the underlying queues are empty.
        /// </summary>
        /// <param name="waitTime">The length of time the method should block before giving up waiting for it to empty.</param>
        /// <returns>True if the queue is empty, false if there are still items waiting to be written.</returns>
        public static bool AreAllQueuesEmpty(TimeSpan waitTime)
        {
            var start = DateTime.UtcNow;
            var then = DateTime.UtcNow;

            while (start.Add(waitTime) > then)
            {
                if (_allQueues.All(x => x.Count == 0))
                    return true;

                Thread.Sleep(100);
                then = DateTime.UtcNow;
            }

            return _allQueues.All(x => x.Count == 0);
        }

        public AsyncLogger()
        {
            _queue = new BlockingCollection<string>(QueueSize);
            _threadCancellationTokenSource = new CancellationTokenSource();
            _allQueues.Add(_queue);

            _workerThread = new Thread(Run);
        }

        private string _logToken = "";
        private bool _debugEnabled = false;
        private bool _useTls = false;

        // Properties for defining location of DataHub instance if one is used.
        private bool _useDataHub = false; // By default, R7Insight service is used instead of DataHub instance.
        private string _dataHubAddr = "";
        private int _dataHubPort = 0;

        // Properties to define host name of user's machine and define user-specified log ID.
        private bool _useHostName = false; // Defines whether to prefix log message with HostName or not.
        private string _hostName = ""; // User-defined or auto-defined host name (if not set in config. file)
        private string _logId = ""; // User-defined log ID to be prefixed to the log message.

        private string _logRegion = ""; // Mandatory region option, e.g: us, eu

        // Sets DataHub usage flag.
        public void SetIsUsingDataHub(bool useDataHub)
        {
            _useDataHub = useDataHub;
        }

        // Sets DataHub instance address.
        public void SetDataHubAddr(string dataHubAddr)
        {
            _dataHubAddr = dataHubAddr;
        }

        // Sets the port on which DataHub instance is waiting for log messages.
        public void SetDataHubPort(int port)
        {
            _dataHubPort = port;
        }

        public void SetToken(string token)
        {
            _logToken = token;
        }

        public void SetDebug(bool debug)
        {
            _debugEnabled = debug;
        }

        public void SetUseSsl(bool useTls)
        {
            _useTls = useTls;
        }

        public void SetUseHostName(bool useHostName)
        {
            _useHostName = useHostName;
        }

        public void SetHostName(string hostName)
        {
            _hostName = hostName;
        }

        public void SetLogId(string logId)
        {
            _logId = logId;
        }

        public void SetRegion(string region)
        {
            _logRegion = region;
        }

        private readonly BlockingCollection<string> _queue;
        private Thread _workerThread;
        private CancellationTokenSource _threadCancellationTokenSource;
        private readonly Random _random = new Random();

        private InsightTcpClient _insightTcpClient;
        private bool _isRunning;

        private string _logMessagePrefix = string.Empty;

        private void Run()
        {
            try
            {
                // Open connection.
                ReopenConnection();

                if (_useHostName) ConfigureHostName();
                if (_logId != string.Empty) _logMessagePrefix = _logId + " ";
                if (_useHostName) _logMessagePrefix += _hostName;
                var isPrefixEmpty = _logMessagePrefix == string.Empty;

                // Flag that is set if logMessagePrefix is empty.

                var cancellationToken = _threadCancellationTokenSource.Token;

                // Send data in queue.
                while (!cancellationToken.IsCancellationRequested)
                {
                    ProcessQueueItem(isPrefixEmpty, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                if (_debugEnabled) WriteDebugMessages("Asynchronous socket client was interrupted.", ex);
            }
        }

        private void ProcessQueueItem(bool isPrefixEmpty, CancellationToken cancellationToken)
        {
            // added debug here
            if (_debugEnabled) WriteDebugMessages("Await queue data");

#if !NETSTANDARD2_0
            var logLine = StringBuilderCache.Acquire();
#else
            var logLine = new StringBuilder();
#endif
            // Take data from queue.
            var line = _queue.Take(cancellationToken);
            if (_debugEnabled) WriteDebugMessages("Queue data obtained");

            // Replace newline chars with line separator to format multi-line events nicely.
#if NETSTANDARD2_0
                    for (var index = 0; index < _posixNewline.Length; index++)
                    {
                        var newline = _posixNewline[index];
                        line = line.Replace(newline, LineSeparator);
                    }
#else
            line = line.ReplaceLineEndings(LineSeparator);
#endif

            // Don't append token to data-hub targeted messages
            if (!_useDataHub) logLine.Append(_logToken);

            // Add prefixes: LogID and HostName if they are defined.
            if (!isPrefixEmpty) logLine.Append(_logMessagePrefix);

            logLine.Append(line);
            logLine.Append(NixNewLine);

#if !NETSTANDARD2_0
            var data = _utf8.GetBytes(StringBuilderCache.GetStringAndRelease(logLine));
#else
            var data = _utf8.GetBytes(logLine.ToString());
#endif
            // Process Buffer-Queue
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (_debugEnabled) WriteDebugMessages("Write data");
#if NETSTANDARD2_0
                    _insightTcpClient.Write(data, 0, data.Length);
#else
                    _insightTcpClient.Write(data);
#endif

                    if (_debugEnabled) WriteDebugMessages("Write complete");
                }
                catch (IOException e)
                {
                    if (_debugEnabled) WriteDebugMessages("IOException during write, reopen: ", e);
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    // Reopen the lost connection.
                    ReopenConnection();
                    continue;
                }

                break;
            }
        }

        private void ConfigureHostName()
        {
            // If LogHostName is set to “true”, but HostName is not defined -
            // try to get host name from Environment.
            if (string.IsNullOrEmpty(_hostName))
            {
                try
                {
                    if (_debugEnabled) WriteDebugMessages("HostName parameter is not defined - trying to get it from System.Environment.MachineName");

                    var hostName = Environment.MachineName;
                    _hostName = "HostName=" + hostName + " ";
                }
                catch (Exception ex)
                {
                    // Cannot get host name automatically, so assume that HostName is not used
                    // and log message is sent without it.
                    _useHostName = false;
                    if (_debugEnabled) WriteDebugMessages("Failed to get HostName parameter using System.Environment.MachineName. Log messages will not be prefixed by HostName", ex);
                }
                return;
            }

            if (!CheckIfHostNameValid(_hostName))
            {
                // If user-defined host name is incorrect - we cannot use it
                // and log message is sent without it.
                _useHostName = false;
                if (_debugEnabled) WriteDebugMessages("HostName parameter contains prohibited characters. Log messages will not be prefixed by HostName");
            }
            else
            {
                _hostName = "HostName=" + _hostName + " ";
            }
        }

        private void OpenConnection()
        {
            try
            {
                if (_insightTcpClient == null)
                {
                    // Create TCP Client instance providing all needed parameters. If DataHub-related properties
                    // have not been overridden by log4net or NLog configurators, then DataHub is not used,
                    // because m_UseDataHub == false by default.
                    _insightTcpClient = new InsightTcpClient(_useTls, _useDataHub, _dataHubAddr, _dataHubPort, _logRegion);
                }

                _insightTcpClient.Connect();
            }
            catch (Exception ex)
            {
                throw new IOException("An error occurred while opening the connection.", ex);
            }
        }

        private void ReopenConnection()
        {
            if (_debugEnabled) WriteDebugMessages("ReopenConnection");
            CloseConnection();

            var cancellationToken = _threadCancellationTokenSource.Token;

            var rootDelay = MinDelay;
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    OpenConnection();
                    return;
                }
                catch (Exception ex)
                {
                    WriteDebugMessages($"Unable to connect to Rapid7 Insight API at {(_insightTcpClient != null ? _insightTcpClient.ServerAddr : "null")}:{_insightTcpClient?.TcpPort ?? 0}", ex);
                }

                rootDelay *= 2;
                if (rootDelay > MaxDelay)
                    rootDelay = MaxDelay;

                var waitFor = rootDelay + _random.Next(rootDelay);
                WriteDebugMessages($"Waiting {waitFor} ms for retry");

                cancellationToken.WaitHandle.WaitOne(waitFor);
            }
        }

        private void CloseConnection()
        {
            _insightTcpClient?.Close();
        }

        private bool IsConfigured()
        {
            if (string.IsNullOrEmpty(_logRegion))
            {
                WriteDebugMessages(NoRegionMessage);
                return false;
            }
            if (GetIsValidGuid(_logToken))
                return true;

            WriteDebugMessages(InvalidTokenMessage);
            return false;
        }

        private static bool CheckIfHostNameValid(string hostName)
        {
            return !_forbiddenHostNameChars.IsMatch(hostName); // Returns false if reg.ex. matches any of forbidden chars.
        }

        private static bool GetIsValidGuid(string guidString)
        {
            if (string.IsNullOrEmpty(guidString))
                return false;

            return Guid.TryParse(guidString, out var newGuid) && newGuid != Guid.Empty;
        }

        private void WriteDebugMessages(string message, Exception ex)
        {
            if (!_debugEnabled)
                return;

            SelfLog.WriteLine(InternalLogPrefix, string.Concat(message, ex.ToString()));
        }

        private void WriteDebugMessages(string message)
        {
            if (!_debugEnabled)
                return;

            SelfLog.WriteLine(InternalLogPrefix, message);
        }

        private void WriteDebugMessagesFormat<T>(string message, T arg0)
        {
            if (!_debugEnabled)
                return;

            WriteDebugMessages(string.Format(message, arg0));
        }

        public void QueueLogEvent(string line)
        {
            QueueLogEntry(line, RecursionLimit);
        }

#if !NETSTANDARD2_0
        public void QueueLogEvent(Span<string> line)
        {
            QueueLogEntry(line, RecursionLimit);
        }
#endif

        public void InterruptWorker()
        {
            if (!_isRunning) return;

            try
            {
                _threadCancellationTokenSource.Cancel();
                _workerThread.Join(1000);
            }
            finally
            {
                _threadCancellationTokenSource = new CancellationTokenSource();
                _workerThread = new Thread(Run);
                _isRunning = false;
            }
        }

        public bool FlushQueue(TimeSpan waitTime)
        {
            var cancellationToken = _threadCancellationTokenSource.Token;

            var startTime = DateTime.UtcNow;
            while (_queue.Count != 0)
            {
                if (!_isRunning)
                    break;

                if (cancellationToken.IsCancellationRequested)
                    break;

                cancellationToken.WaitHandle.WaitOne(100);
                if (DateTime.UtcNow - startTime > waitTime)
                    break;
            }
            return _queue.Count == 0;
        }

#if !NETSTANDARD2_0
        private void QueueLogEntry(Span<string> line, int limit)
        {
            while (true)
            {
                if (limit == 0)
                {
                    if (_debugEnabled) WriteDebugMessagesFormat("Message longer than {0}", RecursionLimit * LogLengthLimit);
                    return;
                }

                if (_debugEnabled) WriteDebugMessagesFormat("Adding Line: {0}", line.ToString());

                if (!_isRunning)
                {
                    // If in DataHub mode credentials are ignored.
                    if (!_useDataHub && IsConfigured() || _useDataHub)
                    {
                        if (_debugEnabled) WriteDebugMessages("Starting Rapid7 Insight asynchronous socket client.");
                        _workerThread.Name = "Rapid7 Insight Log Appender";
                        _workerThread.IsBackground = true;
                        _workerThread.Start();
                        _isRunning = true;
                    }
                }

                if (_debugEnabled) WriteDebugMessagesFormat("Queueing: {0}", line.ToString());

                var chunkedEvent = line.TrimEnd("\r").TrimEnd("\n");

                if (chunkedEvent.Length > LogLengthLimit)
                {
                    AddChunkToQueue(chunkedEvent[0..LogLengthLimit]);
                    line = chunkedEvent[LogLengthLimit..];
                    limit -= 1;
                    continue;
                }

                AddChunkToQueue(chunkedEvent);

                break;
            }
        }
#endif

        private void QueueLogEntry(string line, int limit)
        {
            while (true)
            {
                if (limit == 0)
                {
                    if (_debugEnabled) WriteDebugMessagesFormat("Message longer than {0}", RecursionLimit * LogLengthLimit);
                    return;
                }

                if (_debugEnabled) WriteDebugMessagesFormat("Adding Line: {0}", line);
                if (!_isRunning)
                {
                    // If in DataHub mode credentials are ignored.
                    if (!_useDataHub && IsConfigured() || _useDataHub)
                    {
                        if (_debugEnabled) WriteDebugMessages("Starting Rapid7 Insight asynchronous socket client.");
                        _workerThread.Name = "Rapid7InsightOpsLogAppender";
                        _workerThread.IsBackground = true;
                        _workerThread.Start();
                        _isRunning = true;
                    }
                }

                if (_debugEnabled) WriteDebugMessagesFormat("Queueing: {0}", line);

                var chunkedEvent = line.TrimEnd(_trimChars);
                if (chunkedEvent.Length > LogLengthLimit)
                {
                    AddChunkToQueue(chunkedEvent.Substring(0, LogLengthLimit));
                    line = chunkedEvent.Substring(LogLengthLimit);
                    limit -= 1;
                    continue;
                }

                AddChunkToQueue(chunkedEvent);

                break;
            }
        }

        private void AddChunkToQueue(ReadOnlySpan<string> chunkedEvent)
        {
            // Try to append data to queue.
            if (_queue.TryAdd(chunkedEvent.ToString())) return;

            // If queue is full, remove the oldest message and try again.
            WriteDebugMessages(QueueOverflowMessage);
            _queue.Take();
            _queue.TryAdd(chunkedEvent.ToString());
        }

        private void AddChunkToQueue(string chunkedEvent)
        {
            // Try to append data to queue.
            if (_queue.TryAdd(chunkedEvent)) return;

            // If queue is full, remove the oldest message and try again.
            WriteDebugMessages(QueueOverflowMessage);
            _queue.Take();
            _queue.TryAdd(chunkedEvent);
        }
    }
}
