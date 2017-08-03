using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using Konamiman.Z80dotNet;

namespace Konamiman.NestorMSX.Plugins.TcpipUnapi
{
    public partial class TcpipUnapiPlugin
    {
        private byte UNAPI_GET_INFO()
        {
            cpu.Registers.HL = ImplementationNameAddress;
            cpu.Registers.DE = 0x0100;
            cpu.Registers.BC = 0x0100;
            return ERR_OK;
        }

        private byte TCPIP_GET_CAPAB()
        {
            switch (cpu.Registers.B)
            {
                case 1:
                    cpu.Registers.HL =
                        0 << 0 | // 0: Send ICMP echo messages(PINGs) and retrieve the answers
                        0 << 1 | // 1: Resolve host names by querying a local hosts file or database
                        1 << 2 | // 2: Resolve host names by querying a DNS server
                        0 << 3 | // 3: Open TCP connections in active mode
                        0 << 4 | // 4: Open TCP connections in passive mode, with specified remote socket
                        0 << 5 | // 5: Open TCP connections in passive mode, with unsepecified remote socket
                        0 << 6 | // 6: Send and receive TCP urgent data
                        0 << 7 | // 7: Explicitly set the PUSH bit when sending TCP data
                        0 << 8 | // 8: Send data to a TCP connection before the ESTABLISHED state is reached
                        0 << 9 | // 9: Flush the output buffer of a TCP connection
                        1 << 10 | // 10: Open UDP connections
                        0 << 11 | // 11: Open raw IP connections
                        0 << 12 | // 12: Explicitly set the TTL and TOS for outgoing datagrams
                        0 << 13 | // 13: Explicitly set the automatic reply to PINGs on or off
                        1 << 14 // 14: Automatically obtain the IP addresses, by using DHCP or an equivalent protocol
                        ;

                    cpu.Registers.DE =
                        0 << 0 | // 0: Physical link is point to point
                        0 << 1 | // 1: Physical link is wireless
                        0 << 2 | // 2: Connection pool is shared by TCP, UDP and raw IP
                        0 << 3 | // 3: Checking network state requires sending a packet in looback mode, or other expensive procedure
                        1 << 4 | // 4: The TCP/ IP handling code is assisted by external hardware
                        1 << 5 | // 5: The loopback address(127.x.x.x) is supported
                        0 << 6 | // 6: A host name resolution cache is implemented
                        0 << 7 | // 7: IP datagram fragmentation is supported
                        0 << 8 // 8: User timeout suggested when opening a TCP connection is actually applied
                        ;

                    if (networkInterface.NetworkInterfaceType == NetworkInterfaceType.Slip)
                        cpu.Registers.B = 1;
                    else if (networkInterface.NetworkInterfaceType == NetworkInterfaceType.Ppp)
                        cpu.Registers.B = 2;
                    else if (networkInterface.NetworkInterfaceType.ToString().Contains("Ethernet"))
                        cpu.Registers.B = 3;
                    else
                        cpu.Registers.B = 0;

                    break;

                case 2:
                    cpu.Registers.B = 0;
                    cpu.Registers.C = MaxUdpConnections;
                    cpu.Registers.D = 0;
                    cpu.Registers.E = (byte)udpConnections.Count(c => c == null);
                    cpu.Registers.HL = 0;
                    break;

                case 3:
                    cpu.Registers.HL = mtu.ToShort();
                    cpu.Registers.DE = mtu.ToShort();
                    break;
            }

            return ERR_INV_PAR;
        }

        private byte TCPIP_GET_IPINFO()
        {
            IPAddress getDnsAddress(int index)
            {
                var addresses = networkInterface.GetIPProperties().DnsAddresses;
                return addresses.Count >= index ? null : addresses[index];
            }

            IPAddress ip = null;
            switch (cpu.Registers.B)
            {
                case 1:
                    ip = ipInfo.Address;
                    break;
                case 3:
                    ip = ipInfo.IPv4Mask;
                    break;
                case 4:
                    ip = networkInterface.GetIPProperties().GatewayAddresses.FirstOrDefault(a => IsIPv4(a.Address))?.Address;
                    break;
                case 5:
                    ip = getDnsAddress(0);
                    break;
                case 6:
                    ip = getDnsAddress(1);
                    break;
            }

            if (ip == null)
                return ERR_INV_PAR;

            var ipBytes = ip.GetAddressBytes();
            cpu.Registers.L = ipBytes[0];
            cpu.Registers.H = ipBytes[1];
            cpu.Registers.E = ipBytes[2];
            cpu.Registers.D = ipBytes[3];
            return ERR_OK;
        }

        private byte TCPIP_NET_STATE()
        {
            switch (networkInterface.OperationalStatus)
            {
                case OperationalStatus.Up:
                    cpu.Registers.B = 2;
                    break;
                case OperationalStatus.Unknown:
                    cpu.Registers.B = 255;
                    break;
                default:
                    cpu.Registers.B = 0;
                    break;
            }

            return ERR_OK;
        }
    }
}
