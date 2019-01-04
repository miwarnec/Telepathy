﻿// original source: https://docs.microsoft.com/en-us/dotnet/framework/network-programming/asynchronous-server-socket-example
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Telepathy
{
    public class Server : Common
    {
        // listener
        Thread listenerThread;

        // clients with <clientId, socket>
        SafeDictionary<int, Socket> clients = new SafeDictionary<int, Socket>();

        // connectionId counter
        // (right now we only use it from one listener thread, but we might have
        //  multiple threads later in case of WebSockets etc.)
        // -> static so that another server instance doesn't start at 0 again.
        static int counter = 0;

        // public next id function in case someone needs to reserve an id
        // (e.g. if hostMode should always have 0 connection and external
        //  connections should start at 1, etc.)
        public static int NextConnectionId()
        {
            int id = Interlocked.Increment(ref counter);

            // it's very unlikely that we reach the uint limit of 2 billion.
            // even with 1 new connection per second, this would take 68 years.
            // -> but if it happens, then we should throw an exception because
            //    the caller probably should stop accepting clients.
            // -> it's hardly worth using 'bool Next(out id)' for that case
            //    because it's just so unlikely.
            if (id == int.MaxValue)
            {
                throw new Exception("connection id limit reached: " + id);
            }

            return id;
        }

        // Thread signal.
        ManualResetEvent allDone = new ManualResetEvent(false);

        public bool Active { get { return listenerThread != null && listenerThread.IsAlive; } }

        public void Start(int port)
        {
            // not if already started
            if (Active) return;

            // Establish the local endpoint for the socket.
            IPAddress ipAddress = IPAddress.Any;
            IPEndPoint localEndPoint = new IPEndPoint(ipAddress, port);

            // Create a TCP/IP socket.
            Socket listener = new Socket(ipAddress.AddressFamily,
                SocketType.Stream, ProtocolType.Tcp );

            // Bind the socket to the local endpoint and listen for incoming connections.
            Logger.Log("Server: starting listener thread on port=" + port);
            listenerThread = new Thread(() =>
            {
                try
                {
                    // 1000 backlog makes sense for benchmarks etc.
                    listener.Bind(localEndPoint);
                    listener.Listen(1000);

                    while (true)
                    {
                        // Set the event to nonsignaled state.
                        allDone.Reset();

                        // Start an asynchronous socket to listen for connections.
                        listener.BeginAccept(
                            new AsyncCallback(AcceptCallback),
                            listener);

                        // Wait until a connection is made before continuing.
                        allDone.WaitOne();
                    }

                }
                catch (Exception e)
                {
                    Console.WriteLine(e.ToString());
                }
                Logger.Log("Server thread ended");
            });
            listenerThread.IsBackground = true;
            listenerThread.Start();
        }

        void AcceptCallback(IAsyncResult ar)
        {
            // Signal the main thread to continue.
            allDone.Set();

            // Get the socket that handles the client request.
            Socket listener = (Socket)ar.AsyncState;
            Socket handler = listener.EndAccept(ar);

            // generate the next connection id (thread safely)
            int connectionId = NextConnectionId();
            // TODO if debug?
            //Logger.Log("Server: client connected. connectionId=" + connectionId);

            // Create the state object.
            StateObject state = new StateObject();
            state.connectionId = connectionId;
            state.workSocket = handler;

            // add to clients
            clients.Add(connectionId, handler);

            // add connected event to queue
            messageQueue.Enqueue(new Message(connectionId, EventType.Connected, null));

            // start receiving the 4 header bytes
            handler.BeginReceive(state.header, 0, 4, 0,
                new AsyncCallback(ReadHeaderCallback), state);
        }

        public bool Send(int connectionId, byte[] content)
        {
            // find the connection
            Socket socket;
            if (clients.TryGetValue(connectionId, out socket))
            {
                Send(socket, content);
                return true;
            }
            Logger.LogWarning("Server.Send: invalid connectionId: " + connectionId);
            return false;
        }

        public void Stop()
        {
            // only if started
            if (!Active) return;

            Logger.Log("Server: stopping...");

            // stop listening to connections so that no one can connect while we
            // close the client connections
            listenerThread.Abort();

            // close all client connections.
            List<Socket> connections = clients.GetValues();
            foreach (Socket socket in connections)
            {
                // C#'s built in TcpClient wraps this in try/finally too
                try
                {
                    socket.Shutdown(SocketShutdown.Both);
                }
                catch {} // will get an exception if not connected anymore, etc.
                finally
                {
                    socket.Close();
                }
            }

            // clear clients list
            clients.Clear();
        }

        // get connection info in case it's needed (IP etc.)
        // (we should never pass the TcpClient to the outside)
        public bool GetConnectionInfo(int connectionId, out string address)
        {
            // find the connection
            Socket socket;
            if (clients.TryGetValue(connectionId, out socket))
            {
                address = ((IPEndPoint)socket.RemoteEndPoint).Address.ToString();
                return true;
            }
            address = null;
            return false;
        }

        public bool Disconnect(int connectionId)
        {
            // find the connection
            Socket socket;
            if (clients.TryGetValue(connectionId, out socket))
            {
                socket.Shutdown(SocketShutdown.Both);
                socket.Close();
                clients.Remove(connectionId);
                Logger.Log("Server.Disconnect connectionId:" + connectionId);
                return true;
            }
            return false;
        }
    }
}