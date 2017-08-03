using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Konamiman.Z80dotNet;

namespace Konamiman.NestorMSX.Plugins.TcpipUnapi
{
    public partial class TcpipUnapiPlugin
    {
        private class UdpDatagram
        {
            public IPEndPoint RemoteEndpoint { get; set; }
            public byte[] Data { get; set; }
        }

        private class UdpConnection
        {
            public int Port { get; set; }
            public UdpClient Client { get; set; }
            public bool IsTransient { get; set; }
            private UdpDatagram BufferedReceivedDatagram { get; set; }

            public bool HasIncomingDataAvailable =>
                BufferedReceivedDatagram != null || Client.Available > 0;

            public ushort SizeOfLastReceivedDatagram
            {
                get
                {
                    if (BufferedReceivedDatagram != null)
                        return (ushort)BufferedReceivedDatagram.Data.Length;

                    BufferedReceivedDatagram = GetDatagramFromNetwork();
                    return (ushort)(BufferedReceivedDatagram?.Data?.Length ?? 0);
                }
            }

            public UdpDatagram GetDatagram()
            {
                if (BufferedReceivedDatagram != null)
                {
                    var datagram = BufferedReceivedDatagram;
                    BufferedReceivedDatagram = null;
                    return datagram;
                }

                return GetDatagramFromNetwork();
            }

            private UdpDatagram GetDatagramFromNetwork()
            {
                if (Client.Available == 0)
                    return null;

                IPEndPoint remoteEndpoint = null;
                var data = Client.Receive(ref remoteEndpoint);
                return new UdpDatagram { Data = data, RemoteEndpoint = remoteEndpoint };
            }
        }

        private readonly UdpConnection[] udpConnections = new UdpConnection[MaxUdpConnections];

        private byte TCPIP_UDP_OPEN()
        {
            ushort? port = cpu.Registers.HL.ToUShort();

            if (port == 0xFFFF)
                port = null;

            if (port == 0 || port >= 0xFFF0 || cpu.Registers.B > 1)
                return ERR_INV_PAR;

            if (udpConnections.All(c => c != null))
                return ERR_NO_FREE_CONN;

            if (port != null && udpConnections.Any(c => c?.Port == port.Value))
                return ERR_CONN_EXISTS;

            var client = port == null ? new UdpClient() : new UdpClient((int)port);
            for (var i = 0; i < MaxUdpConnections; i++)
            {
                if (udpConnections[i] == null)
                {
                    udpConnections[i] = new UdpConnection
                    {
                        Client = client,
                        IsTransient = cpu.Registers.B == 0,
                        Port = ((IPEndPoint)client.Client.LocalEndPoint).Port
                    };
                    cpu.Registers.B = (byte)(i + 1);
                    break;
                }
            }

            return ERR_OK;
        }

        private UdpConnection GetUdpConnection(byte connectionNumber)
        {
            if (connectionNumber == 0 || connectionNumber >= (MaxUdpConnections + 1))
                return null;

            return udpConnections[connectionNumber - 1];
        }

        private byte TCPIP_UDP_CLOSE()
        {
            var connection = cpu.Registers.B - 1;

            if (connection == -1)
            {
                for (var i = 0; i < MaxUdpConnections; i++)
                {
                    if (udpConnections[i]?.IsTransient == true)
                    {
                        udpConnections[i].Client.Close();
                        udpConnections[i] = null;
                    }
                }
                return ERR_OK;
            }

            if (connection >= MaxUdpConnections || udpConnections[connection] == null)
                return ERR_NO_CONN;

            udpConnections[connection].Client.Close();
            udpConnections[connection] = null;
            return ERR_OK;
        }

        private byte TCPIP_UDP_STATE()
        {
            var connection = GetUdpConnection(cpu.Registers.B);
            if (connection == null)
                return ERR_NO_CONN;

            cpu.Registers.HL = connection.Port.ToShort();
            cpu.Registers.B = connection.HasIncomingDataAvailable ? (byte)0 : (byte)1;
            cpu.Registers.DE = connection.SizeOfLastReceivedDatagram.ToShort();

            return ERR_OK;
        }

        private byte TCPIP_UDP_SEND()
        {
            var connection = GetUdpConnection(cpu.Registers.B);
            if (connection == null)
                return ERR_NO_CONN;

            if (NoNetworkAvailable())
                return ERR_NO_NETWORK;

            var paramsAddress = cpu.Registers.DE;
            var destIp = new IPAddress(new byte[] { slots[paramsAddress], slots[paramsAddress + 1], slots[paramsAddress + 2], slots[paramsAddress + 3] });
            var destPort = NumberUtils.CreateUshort(slots[paramsAddress + 4], slots[paramsAddress + 5]);
            var dataSize = NumberUtils.CreateUshort(slots[paramsAddress + 6], slots[paramsAddress + 7]);

            if (dataSize > mtu - 28)
                return ERR_LARGE_DGRAM;

            if (dataSize == 0)
                return ERR_OK;

            var data = new byte[dataSize];
            var dataAddress = cpu.Registers.HL;
            for (var i = 0; i < dataSize; i++)
                data[i] = slots[dataAddress + i];

            var remoteEndpoint = new IPEndPoint(destIp, destPort);
            connection.Client.Send(data, dataSize, remoteEndpoint);

            return ERR_OK;
        }

        private byte TCPIP_UDP_RCV()
        {
            var connection = GetUdpConnection(cpu.Registers.B);
            if (connection == null)
                return ERR_NO_CONN;

            if (!connection.HasIncomingDataAvailable)
                return ERR_NO_DATA;

            var datagram = connection.GetDatagram();
            var maxDataSize = cpu.Registers.DE;
            var addressForData = cpu.Registers.HL;

            var dataSizeToCopy = Math.Min(maxDataSize, datagram.Data.Length);
            for (var i = 0; i < dataSizeToCopy; i++)
                slots[addressForData + i] = datagram.Data[i];

            var remoteIpBytes = datagram.RemoteEndpoint.Address.GetAddressBytes();
            cpu.Registers.L = remoteIpBytes[0];
            cpu.Registers.H = remoteIpBytes[1];
            cpu.Registers.E = remoteIpBytes[2];
            cpu.Registers.D = remoteIpBytes[3];

            cpu.Registers.IX = datagram.RemoteEndpoint.Port.ToShort();
            cpu.Registers.BC = dataSizeToCopy.ToShort();

            return ERR_OK;
        }
    }
}
