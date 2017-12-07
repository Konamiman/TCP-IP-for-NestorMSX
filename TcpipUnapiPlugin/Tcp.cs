using System;
using System.Linq;
using System.Net.NetworkInformation;
using Konamiman.Z80dotNet;

namespace Konamiman.NestorMSX.Plugins.TcpipUnapi
{
    public partial class TcpipUnapiPlugin
    {
        private readonly TcpConnection[] tcpConnections = new TcpConnection[MaxTcpConnections];

        private byte TCPIP_TCP_OPEN()
        {
            var connectionIndex = -1;
            for (var i = 0; i < MaxTcpConnections; i++)
            {
                if (tcpConnections[i] == null || tcpConnections[i].IsClosed)
                {
                    connectionIndex = i;
                    break;
                }
            }

            if (connectionIndex == -1)
                return ERR_NO_FREE_CONN;

            if (NoNetworkAvailable())
                return ERR_NO_NETWORK;

            var paramsAddress = cpu.Registers.HL;
            var remoteIp = new byte[]
                    { slots[paramsAddress], slots[paramsAddress + 1], slots[paramsAddress + 2], slots[paramsAddress + 3] };
            var remotePort = NumberUtils.CreateUshort(slots[paramsAddress + 4], slots[paramsAddress + 5]);
            var localPort = NumberUtils.CreateUshort(slots[paramsAddress + 6], slots[paramsAddress + 7]);

            if (localPort == 0xFFFF)
            {
                do
                {
                    localPort = (ushort)new Random().Next(16384, 32767);
                }
                while (TcpConnection.LocalPortIsInUse(localPort));
            }
            else if( TcpConnection.LocalPortIsInUse(localPort))
            {
                return ERR_CONN_EXISTS;
            }

            var flags = slots[paramsAddress + 10];
            var isPassive = flags.GetBit(0) == 1;
            var isResident = flags.GetBit(1) == 1;

            if (isPassive && remoteIp.Any(b => b != 0))
                return ERR_NOT_IMP;

            if (!isPassive && remoteIp.All(b => b == 0))
                return ERR_INV_PAR;

            if (!isPassive &&
                tcpConnections.Any(
                    c =>
                        c != null &&
                        c.LocalPort == localPort &&
                        c.RemoteEndpoint.Address.Equals(remoteIp) &&
                        c.RemoteEndpoint.Port == remotePort))
                return ERR_CONN_EXISTS;

            var connection = isPassive ?
                TcpConnection.CreatePassive(localPort) :
                TcpConnection.CreateActive(localPort, remoteIp, remotePort);
            connection.IsTransient = !isResident;

            tcpConnections[connectionIndex] = connection;

            cpu.Registers.B = (byte) (connectionIndex + 1);
            return ERR_OK;
        }

        private TcpConnection GetTcpConnection(byte connectionNumber)
        {
            if (connectionNumber == 0 || connectionNumber >= (MaxTcpConnections + 1))
                return null;

            var connection = tcpConnections[connectionNumber - 1];

            if (connection?.IsClosed == true)
                return null;

            return connection;
        }

        private byte TCPIP_TCP_CLOSE()
        {
            Action<int> action = index =>
                tcpConnections[index]?.Close();

            return TcpAbortOrClose(action);
        }

        private byte TCPIP_TCP_ABORT()
        {
            Action<int> action = index =>
            {
                tcpConnections[index]?.Abort();
                tcpConnections[index] = null;
            };

            return TcpAbortOrClose(action);
        }

        private byte TcpAbortOrClose(Action<int> action)
        {
            var connectionIndex = cpu.Registers.B - 1;

            if (connectionIndex == -1)
            {
                for (var i = 0; i < MaxTcpConnections; i++)
                {
                    if (tcpConnections[i]?.IsTransient == true)
                        action(i);
                }
                return ERR_OK;
            }

            if (connectionIndex >= MaxTcpConnections || tcpConnections[connectionIndex] == null)
                return ERR_NO_CONN;

            action(connectionIndex);
            return ERR_OK;
        }

        private byte TCPIP_TCP_STATE()
        {
            cpu.Registers.C = 0;

            var connection = GetTcpConnection(cpu.Registers.B);
            if (connection == null)
                return ERR_NO_CONN;

            var connectionState = connection.GetState();
            if (connectionState == TcpState.Unknown)
                cpu.Registers.B = 0;
            else
                cpu.Registers.B = (byte)(((int)connectionState) - 1);

            var infoBlockPointer = cpu.Registers.HL;
            if (infoBlockPointer != 0)
            {
                void PutUshortInMem(int value, int address)
                {
                    var bytes = BitConverter.GetBytes(value.ToUShort());
                    slots[address] = bytes[BitConverter.IsLittleEndian ? 0 : 1];
                    slots[address + 1] = bytes[BitConverter.IsLittleEndian ? 1 : 0];
                }

                var remoteAddressBytes = connection.RemoteEndpoint.Address.GetAddressBytes();
                for (var i = 0; i < 4; i++)
                    slots[infoBlockPointer + i] = remoteAddressBytes[i];
                PutUshortInMem(connection.RemoteEndpoint.Port, infoBlockPointer + 4);
                PutUshortInMem(connection.LocalPort, infoBlockPointer + 6);
            }

            cpu.Registers.HL = Math.Min(connection.AvailableCount, ushort.MaxValue).ToShort();
            cpu.Registers.DE = 0;
            cpu.Registers.IX = -1;

            return ERR_OK;
        }

        private byte TCPIP_TCP_SEND()
        {
            var connection = GetTcpConnection(cpu.Registers.B);
            if (connection == null)
                return ERR_NO_CONN;

            if (!connection.CanSend())
                return ERR_CONN_STATE;

            if ((cpu.Registers.C & 0b11111100) != 0)
                return ERR_INV_PAR;

            var dataAddress = cpu.Registers.DE;
            var dataLength = cpu.Registers.HL.ToUShort();
            if (dataLength == 0)
                return ERR_OK;

            var mustPush = (cpu.Registers.C & 1) == 1;

            var data = new byte[dataLength];
            for (var i = 0; i < dataLength; i++)
                data[i] = slots[dataAddress + i];

            connection.Send(data, mustPush);

            return ERR_OK;
        }

        private byte TCPIP_TCP_RCV()
        {
            var connection = GetTcpConnection(cpu.Registers.B);
            if (connection == null)
                return ERR_NO_CONN;

            if (!connection.CanReceive())
                return ERR_CONN_STATE;

            var dataAddress = cpu.Registers.DE;
            var dataLength = cpu.Registers.HL.ToUShort();

            cpu.Registers.BC = 0;
            cpu.Registers.HL = 0;

            dataLength = (ushort) Math.Min(connection.AvailableCount, dataLength);
            if (dataLength == 0)
                return ERR_OK;

            var data = connection.Receive(dataLength);

            for (var i = 0; i < dataLength; i++)
                slots[dataAddress + i] = data[i];

            cpu.Registers.BC = dataLength.ToShort();

            return ERR_OK;
        }

        private byte TCPIP_TCP_FLUSH()
        {
            var connection = GetTcpConnection(cpu.Registers.B);
            if (connection == null)
                return ERR_NO_CONN;

            if (!connection.CanSend())
                return ERR_CONN_STATE;

            connection.Flush();

            return ERR_OK;
        }
    }
}
