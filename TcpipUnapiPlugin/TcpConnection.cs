using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Konamiman.NestorMSX.Plugins.TcpipUnapi
{
    public class TcpConnection
    {
        private TcpClient tcpClient;
        private TcpListener listener;

        private TcpConnection()
        {
        }

        public static TcpConnection CreatePassive(int localPort)
        {
            var connection = new TcpConnection();
            connection.isListening = true;
            connection.LocalPort = localPort;

            connection.listener = new TcpListener(IPAddress.Any, localPort);
            connection.listener.Start(1);
            connection.listener.BeginAcceptTcpClient(
                ar =>
                {
                    var self = (TcpConnection)ar.AsyncState;
                    if (!self.isListening)
                        return;

                    try
                    {
                        var client = self.listener.EndAcceptTcpClient(ar);
                        self.listener.Stop();
                        self.listener = null;
                        self.tcpClient = client;
                        self.RemoteEndpoint = (IPEndPoint)client.Client.RemoteEndPoint;
                        self.isListening = false;
                    }
                    catch (SocketException)
                    {
                        self.IsClosed = true;
                    }
                },
                connection);

            return connection;
        }

        public static TcpConnection CreateActive(int localPort, byte[] remoteIp, int remotePort)
        {
            var connection = new TcpConnection();
            connection.LocalPort = localPort;
            connection.RemoteEndpoint = new IPEndPoint(new IPAddress(remoteIp), remotePort);

            var client = new TcpClient(new IPEndPoint(IPAddress.Any, localPort));
            connection.tcpClient = client;

            client.BeginConnect(
                new IPAddress(remoteIp),
                remotePort,
                ar =>
                {
                    var self = (TcpConnection)ar.AsyncState;
                    try
                    {
                        connection.tcpClient.EndConnect(ar);
                    }
                    catch (SocketException)
                    {
                        self.IsClosed = true;
                    }
                },
                connection);

            return connection;
        }

        public void Close()
        {
            try
            {
                listener?.Stop();
                tcpClient?.Client?.Shutdown(SocketShutdown.Send);
                isListening = false;
            }
            catch
            {
                Abort();
            }
        }

        public void Abort()
        {
            isListening = false;
            try
            {
                listener?.Stop();
                tcpClient?.Client?.Dispose();
            }
            catch { }

            IsClosed = true;
        }

        public TcpState GetState()
        {
            if (isListening)
                return TcpState.Listen;

            if (IsClosed)
                return TcpState.Closed;

            try
            {
                var connectionInfo =
                    IPGlobalProperties
                        .GetIPGlobalProperties()
                        .GetActiveTcpConnections()
                        .SingleOrDefault(c =>
                            c.RemoteEndPoint.Address.Equals(RemoteEndpoint.Address) &&
                            c.RemoteEndPoint.Port == RemoteEndpoint.Port &&
                            c.LocalEndPoint.Port == LocalPort);

                if(connectionInfo == null)
                {
                    Abort();
                    return TcpState.Closed;
                }

                return connectionInfo.State;
            }
            catch
            {
                Abort();
                return TcpState.Closed;
            }
        }

        public bool CanSend()
        {
            var state = GetState();
            return state == TcpState.Established || state == TcpState.CloseWait;
        }

        public void Send(byte[] data, bool push)
        {
            if (!CanSend())
                throw new InvalidOperationException("Can't send data in the current connection state");

            try
            {
                var stream = tcpClient.GetStream();
                stream.Write(data, 0, data.Length);
                if (push)
                    stream.Flush();
            }
            catch
            {
                Abort();
            }
        }

        public void Flush()
        {
            try
            {
                var stream = tcpClient.GetStream();
                stream.Flush();
            }
            catch
            {
                Abort();
            }
        }

        public int AvailableCount
        {
            get
            {
                if (IsClosed || isListening)
                    return 0;

                try
                {
                    return tcpClient.Available;
                }
                catch
                {
                    Abort();
                    return 0;
                }
            }
        }

        public bool CanReceive()
        {
            var state = GetState();
            return state >= TcpState.Established;
        }

        public byte[] Receive(int count)
        {
            if (!CanReceive())
                throw new InvalidOperationException("Can't receive data in the current connection state");

            var available = AvailableCount;
            if (available == 0)
                return new byte[0];

            try
            {
                var stream = tcpClient.GetStream();
                var data = new byte[count];
                stream.Read(data, 0, count);
                return data;
            }
            catch
            {
                Abort();
                return new byte[0];
            }
        }

        public bool IsClosed { get; private set; }

        private bool isListening;

        public int LocalPort { get; set; }

        public IPEndPoint RemoteEndpoint { get; private set; }

        public bool IsTransient { get; set; }
    }
}
