using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Benchmark;

public static class FakeRapid7
{
    public static void StartFakeLogEndpoint(int port)
    {
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();

        try
        {
            while (true)
            {
                var client = listener.AcceptTcpClient();
                // Start handling the client in a separate thread
                var clientThread = new Thread(() => HandleClient(client));
                clientThread.Start();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Exception: " + ex.Message);
        }
        finally
        {
            listener.Stop();
        }
    }

    private static void HandleClient(TcpClient client)
    {
        var stream = client.GetStream();

        try
        {
            // Send HTTP 200 OK response once
            const string httpResponse = "HTTP/1.1 200 OK\r\n" +
                                        "Content-Type: text/plain\r\n" +
                                        "Content-Length: 2\r\n" +
                                        "\r\n" +
                                        "OK";
            var responseData = Encoding.UTF8.GetBytes(httpResponse);
            stream.Write(responseData, 0, responseData.Length);

            var buffer = new byte[1024];
            while (true)
            {
                var bytesRead = stream.Read(buffer, 0, buffer.Length);
                if (bytesRead == 0)
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Client handling exception: " + ex.Message);
        }
        finally
        {
            stream.Close();
            client.Close();
        }
    }
}
