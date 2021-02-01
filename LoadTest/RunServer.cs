﻿using System;
using System.Diagnostics;
using System.Threading;

namespace Telepathy.LoadTest
{
    public class RunServer
    {
        public const int MaxMessageSize = 16 * 1024;
        static long messagesReceived = 0;
        static long dataReceived = 0;

        public static void StartServer(int port)
        {

            // create server
            Server server = new Server(MaxMessageSize);

            // OnData replies and updates statistics
            server.OnData = (connectionId, data) => {
                server.Send(connectionId, new ArraySegment<byte>(data));
                messagesReceived++;
                dataReceived += data.Length;
            };

            server.Start(port);
            int serverFrequency = 60;
            Log.Info("started server");

            Stopwatch stopwatch = Stopwatch.StartNew();

            while (true)
            {
                // tick while receiving. will auto reply.
                while (server.Tick()) {}

                // sleep
                Thread.Sleep(1000 / serverFrequency);

                // report every 10 seconds
                if (stopwatch.ElapsedMilliseconds > 1000 * 2)
                {
                    Log.Info(string.Format("Thread[" + Thread.CurrentThread.ManagedThreadId + "]: Server in={0} ({1} KB/s)  out={0} ({1} KB/s) ReceiveQueue={2}", messagesReceived, (dataReceived * 1000 / (stopwatch.ElapsedMilliseconds * 1024)), server.ReceiveQueueCount.ToString()));
                    stopwatch.Stop();
                    stopwatch = Stopwatch.StartNew();
                    messagesReceived = 0;
                    dataReceived = 0;
                }
            }
        }
    }
}
