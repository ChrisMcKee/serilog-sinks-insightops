using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Serilog.Formatting.Compact;

namespace Serilog.Sinks.InsightOps.ConsoleTest
{
    public class Program
    {
        static void Main()
        {
            // If something is wrong with our Serilog setup, 
            // lets make sure we can see what the problem is.
            //Debugging.SelfLog.Enable(msg => Console.WriteLine(msg));
            Debugging.SelfLog.Enable(Console.Error);

            Console.WriteLine("Starting.");

            Thread listenerThread = new Thread(StartFakeLogEndpoint);
            listenerThread.IsBackground = true;
            listenerThread.Start();

            var settings = new InsightOpsSinkSettings
            {
                //Token = Guid.Empty.ToString(),
                Token = Guid.NewGuid().ToString(),
                Region = "au", // au, eu, jp or us.,
                UseSsl = false,
                Debug = true,
                DataHubAddress = "localhost",
                DataHubPort = 8085,
                IsUsingDataHub = true
            };

            // Create our logger.
            var log = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.InsightOps(settings)
                //.WriteTo.InsightOps(settings, new RenderedCompactJsonFormatter())
                .WriteTo.Console()
                //.WriteTo.Console(new CompactJsonFormatter())
                //.WriteTo.Seq("http://localhost:5301")
                .CreateLogger();


            // Log some fake info.
            var position = new { Latitude = 25, Longitude = 134 };
            var elapsedMs = 34;
            log.Information("Processed {@Position} in {Elapsed:000} ms", position, elapsedMs);
            log.Information("Processed {@Position} in {Elapsed:000} ms", position, elapsedMs);
            log.Information("Processed {@Position} in {Elapsed:000} ms", position, elapsedMs);
            log.Information("Processed {@Position} in {Elapsed:000} ms", position, elapsedMs);
            log.Information("Processed {@Position} in {Elapsed:000} ms", position, elapsedMs);
            log.Information("Processed {@Position} in {Elapsed:000} ms", position, elapsedMs);

            Console.WriteLine("Flushing and closing...");

            Thread.Sleep(TimeSpan.FromMinutes(1));
            log.Dispose();

            Serilog.Log.CloseAndFlush();

            listenerThread.Interrupt();
            Console.WriteLine("Finished.");
        }

        static void StartFakeLogEndpoint()
        {
            TcpListener listener = new TcpListener(IPAddress.Any, 8085);
            listener.Start();

            try
            {
                while (true)
                {
                    Console.WriteLine("Waiting for a connection...");
                    TcpClient client = listener.AcceptTcpClient();
                    Console.WriteLine("Client connected!");

                    // Start handling the client in a separate thread
                    Thread clientThread = new Thread(() => HandleClient(client));
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

        static void HandleClient(TcpClient client)
        {
            NetworkStream stream = client.GetStream();

            try
            {
                // Send HTTP 200 OK response once
                string httpResponse = "HTTP/1.1 200 OK\r\n" +
                                      "Content-Type: text/plain\r\n" +
                                      "Content-Length: 2\r\n" +
                                      "\r\n" +
                                      "OK";
                byte[] responseData = Encoding.UTF8.GetBytes(httpResponse);
                stream.Write(responseData, 0, responseData.Length);

                byte[] buffer = new byte[1024];
                while (true)
                {
                    int bytesRead = stream.Read(buffer, 0, buffer.Length);
                    if (bytesRead == 0)
                    {
                        Console.WriteLine("Client disconnected.");
                        break;
                    }
                    Console.WriteLine("Received: " + Encoding.UTF8.GetString(buffer, 0, bytesRead));
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
}
