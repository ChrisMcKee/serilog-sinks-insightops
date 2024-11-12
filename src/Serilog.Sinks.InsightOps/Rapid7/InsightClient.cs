using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Serilog.Sinks.InsightOps.Rapid7
{
    internal sealed class InsightTcpClient
    {
        private const string DataUrl = "{0}.data.logs.insight.rapid7.com";
        private const int UnsecurePort = 80;
        private const int SecurePort = 443;

        public InsightTcpClient(bool useSsl, bool useDataHub, string serverAddr, int port, string region)
        {
            if (useDataHub)
            {
                _useTls = false; // DataHub does not support receiving log messages over SSL for now.
                TcpPort = port;
                ServerAddr = serverAddr;
                return;
            }

            _useTls = useSsl;
            TcpPort = _useTls ? SecurePort : UnsecurePort;
            ServerAddr = string.Format(DataUrl, region);
        }

        private readonly bool _useTls;
        public int TcpPort { get; }
        private TcpClient _tcpClient;
        private Stream _stream;
        private SslStream _tlsStream;
        public string ServerAddr { get; }

        private Stream ActiveStream => _useTls ? _tlsStream : _stream;

        private static void SetSocketKeepAliveValues(TcpClient tcpClient, int keepAliveTime, int keepAliveInterval)
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                ServicePointManager.SetTcpKeepAlive(true, keepAliveTime, keepAliveInterval);

                const uint dummy = 0;
                var inOptionValues = new byte[Marshal.SizeOf(dummy) * 3];
                const bool onOff = true;

                BitConverter.GetBytes((uint)(onOff ? 1 : 0)).CopyTo(inOptionValues, 0);
                BitConverter.GetBytes((uint)keepAliveTime).CopyTo(inOptionValues, Marshal.SizeOf(dummy));
                BitConverter.GetBytes((uint)keepAliveInterval).CopyTo(inOptionValues, Marshal.SizeOf(dummy) * 2);

                tcpClient.Client.IOControl(IOControlCode.KeepAliveValues, inOptionValues, null);
                return;
            }

#if !NETSTANDARD2_0
            tcpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.TcpKeepAliveTime, keepAliveTime);
            tcpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.TcpKeepAliveInterval, keepAliveInterval);
#endif
        }

        public void Connect()
        {
            _tcpClient = new TcpClient();
            _tcpClient.Connect(ServerAddr, TcpPort);
            _tcpClient.NoDelay = true;

            _tcpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

            try
            {
                SetSocketKeepAliveValues(_tcpClient, 10 * 1000, 1000);
            }
            catch (PlatformNotSupportedException)
            {
                // ignore
            }

            _stream = _tcpClient.GetStream();

            if (!_useTls) return;

            _tlsStream = new SslStream(_stream);
            _tlsStream.AuthenticateAsClient(ServerAddr);
        }

        public async Task ConnectAsync()
        {
            _tcpClient = new TcpClient();
            await _tcpClient.ConnectAsync(ServerAddr, TcpPort);
            _tcpClient.NoDelay = true;

            _tcpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

            try
            {
                SetSocketKeepAliveValues(_tcpClient, 10 * 1000, 1000);
            }
            catch (PlatformNotSupportedException)
            {
                // .NET on Linux does not support modification of that settings at the moment. Defaults applied.
            }

            _stream = _tcpClient.GetStream();

            if (!_useTls) return;

            _tlsStream = new SslStream(_stream);
            await _tlsStream.AuthenticateAsClientAsync(ServerAddr);
        }

#if NETSTANDARD2_0
        public void Write(byte[] buffer, int offset, int count)
        {
            ActiveStream.Write(buffer, offset, count);
            ActiveStream.Flush();
        }
#else
        public void Write(ReadOnlySpan<byte> buffer)
        {
            ActiveStream.Write(buffer);
            ActiveStream.Flush();
        }
#endif
        public async Task WriteAsync(byte[] buffer, int offset, int count)
        {
            #if NETSTANDARD2_0
            await ActiveStream.WriteAsync(buffer, offset, count);
            #else
            await ActiveStream.WriteAsync(buffer.AsMemory(offset, count));
            #endif
            await ActiveStream.FlushAsync();
        }

        public void Close()
        {
            if (_tcpClient == null) return;

            try
            {
                _tcpClient?.Dispose();
                _stream?.Dispose();
                _tlsStream?.Dispose();
            }
            catch
            {
                // ignored
            }
            finally
            {
                _tcpClient = null;
                _stream = null;
                _tlsStream = null;
            }
        }
    }
}
