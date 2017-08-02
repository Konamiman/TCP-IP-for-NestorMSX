using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using Konamiman.NestorMSX.Hardware;
using Konamiman.NestorMSX.Memories;
using Konamiman.NestorMSX.Misc;
using Konamiman.Z80dotNet;

namespace Konamiman.NestorMSX.Plugins.TcpipUnapi
{
    [NestorMSXPlugin("TCP/IP UNAPI")]
    public class TcpipUnapiPlugin
    {
        private const int EntryPointAddress = 0x4000;
        private const int ImplementationNameAddress = 0x4010;
        private const int EXTBIO = 0xFFCA;
        private const int ARG = 0xF847;
        private const string SpecIdentifier = "TCP/IP";
        private const int MaxUdpConnections = 4;

        private static TcpipUnapiPlugin Instance;

        private readonly SlotNumber slotNumber;
        private readonly IZ80Processor cpu;
        private readonly IExternallyControlledSlotsSystem slots;
        private readonly NetworkInterface networkInterface;
        private readonly UnicastIPAddressInformation ipInfo;
        private readonly bool dnsServersAvailable;
        private readonly int mtu;

        public static TcpipUnapiPlugin GetInstance(PluginContext context, IDictionary<string, object> pluginConfig)
        {
            if(Instance == null)
                Instance = new TcpipUnapiPlugin(context, pluginConfig);

            return Instance;
        }

        private TcpipUnapiPlugin(PluginContext context, IDictionary<string, object> pluginConfig)
        {
            slotNumber = new SlotNumber((byte)pluginConfig["NestorMSX.slotNumber"]);
            cpu = context.Cpu;
            slots = context.SlotsSystem;
            cpu.BeforeInstructionFetch += Cpu_BeforeInstructionFetch;

            bool hasIp(NetworkInterface iface, string ip)
            {
                return iface.GetIPProperties().UnicastAddresses.Any(a => a.Address.ToString() == ip);
            }

            bool hasIpv4Address(NetworkInterface iface)
            {
                return iface.GetIPProperties().UnicastAddresses.Any(a => IsIPv4(a.Address));
            }

            var ipAddress = pluginConfig.GetValueOrDefault("ipAddress", "");
            var networkInterfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(i => i.Supports(NetworkInterfaceComponent.IPv4));
            if (ipAddress == "")
            {
                networkInterface = 
                    networkInterfaces.FirstOrDefault(i => hasIpv4Address(i) && i.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    ?? networkInterfaces.FirstOrDefault(i => hasIpv4Address(i));
                ipInfo = networkInterface?.GetIPProperties().UnicastAddresses.First(a => IsIPv4(a.Address));
            }
            else
            {
                networkInterface = networkInterfaces.FirstOrDefault(i => hasIp(i, ipAddress));
                ipInfo = networkInterface?.GetIPProperties().UnicastAddresses.First(a => a.Address.ToString() == ipAddress);
            }

            if (networkInterface == null)
            {
                throw new Exception(ipAddress == "" ?
                    "No IPv4 network interfaces available" :
                    $"There is no network interface with the IP address {ipAddress}");
            }

            dnsServersAvailable = networkInterface.GetIPProperties().DnsAddresses.Any();
            mtu = (short)Math.Min(32767, networkInterface.GetIPProperties().GetIPv4Properties().Mtu);
            
            InitRoutinesArray();
        }

        private void Cpu_BeforeInstructionFetch(object sender, BeforeInstructionFetchEventArgs e)
        {
            if (cpu.Registers.PC == EXTBIO)
                HandleExtbioCall();
                
            if (cpu.Registers.PC == EntryPointAddress && slots.GetCurrentSlot(1) == slotNumber)
                HandleEntryPointCall();
        }

        public IMemory GetMemory()
        {
            var romContents =
                new byte[ImplementationNameAddress-0x4000].Concat(
                Encoding.ASCII.GetBytes("TCP/IP UNAPI plugin for NestorMSX\0"))
                .ToArray();

            return new PlainRom(romContents, 1);
        }

        private byte[] GetMemoryContents(int address, int length)
        {
            var contents = new byte[length];
            for (var i = 0; i < length; i++)
                contents[i] = cpu.Memory[address + i];
            return contents;
        }

        private void HandleExtbioCall()
        {
            if (cpu.Registers.DE != 0x2222 || cpu.Registers.A == 0xFF)
                return;

            var suppliedSpecIdentifier = Encoding.ASCII.GetString(GetMemoryContents(ARG, SpecIdentifier.Length + 1));
            if(!suppliedSpecIdentifier.Equals(SpecIdentifier+"\0", StringComparison.InvariantCultureIgnoreCase))
                return;

            if (cpu.Registers.A == 0)
            {
                cpu.Registers.B++;
                return;
            }

            if (cpu.Registers.A == 1)
            {
                cpu.Registers.A = slotNumber;
                cpu.Registers.B = 0xFF;
                cpu.Registers.HL = EntryPointAddress;
                cpu.ExecuteRet();
                return;
            }

            cpu.Registers.A--;
        }

        private void HandleEntryPointCall()
        {
            var functionNumber = cpu.Registers.A;

            if (functionNumber == 29)
            {
                cpu.Registers.IFF1 = 1;
                cpu.Registers.IFF1 = 2;
            }

            if (functionNumber < Routines.Length && Routines[functionNumber] != null)
                cpu.Registers.A = Routines[functionNumber]();
            else
                cpu.Registers.A = ERR_NOT_IMP;

            cpu.ExecuteRet();
        }

        private Func<byte>[] Routines;
        private void InitRoutinesArray()
        {
            Routines = new Func<byte>[]
            {
                UNAPI_GET_INFO,
                TCPIP_GET_CAPAB,
                TCPIP_GET_IPINFO,
                TCPIP_NET_STATE,
                null, //TCPIP_SEND_ECHO
                null, //TCPIP_RCV_ECHO
                TCPIP_DNS_Q,
                TCPIP_DNS_S,
                TCPIP_UDP_OPEN,
                TCPIP_UDP_CLOSE,
                TCPIP_UDP_STATE,
                TCPIP_UDP_SEND,
                TCPIP_UDP_RCV
            };
        }

        private const int ERR_OK = 0;
        private const int ERR_NOT_IMP = 1;
        private const int ERR_NO_NETWORK = 2;
        private const int ERR_NO_DATA = 3;
        private const int ERR_INV_PAR = 4;
        private const int ERR_QUERY_EXISTS = 5;
        private const int ERR_INV_IP = 6;
        private const int ERR_NO_DNS = 7;
        private const int ERR_DNS = 8;
        private const int ERR_NO_FREE_CONN = 9;
        private const int ERR_CONN_EXISTS = 10;
        private const int ERR_NO_CONN = 11;
        private const int ERR_LARGE_DGRAM = 14;

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

        private bool IsIPv4(IPAddress ipAddress) =>
            ipAddress.AddressFamily == AddressFamily.InterNetwork;

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

        private bool NoNetworkAvailable()
        {
            return !(networkInterface.OperationalStatus == OperationalStatus.Up || networkInterface.OperationalStatus == OperationalStatus.Unknown);
        }

        private bool dnsQueryInProgress = false;
        private byte[] lastIpResolved = null;
        private bool lastIpResolvedWasDirectIp = false;
        private byte? lastDnsError = null;
        private byte TCPIP_DNS_Q()
        {
            var flags = cpu.Registers.B;
            if (flags.GetBit(2) == 1 && dnsQueryInProgress)
            {
                return ERR_QUERY_EXISTS;
            }
            if (flags.GetBit(0) == 1)
            {
                dnsQueryInProgress = false;
                lastDnsError = 0;
                return ERR_OK;
            }

            if (NoNetworkAvailable())
                return ERR_NO_NETWORK;

            if (!dnsServersAvailable)
                return ERR_NO_DNS;

            var namePointer = cpu.Registers.HL;
            var nameBytes = new List<byte>();
            while(slots[namePointer] != 0)
                nameBytes.Add(slots[namePointer++]);
            var name = Encoding.ASCII.GetString(nameBytes.ToArray());

            var wasIp = IPAddress.TryParse(name, out IPAddress parsedIp)
                && IsIPv4(parsedIp);
            if (wasIp)
            {
                lastIpResolved = parsedIp.GetAddressBytes();
                lastIpResolvedWasDirectIp = true;
                cpu.Registers.B = 1;
                cpu.Registers.L = lastIpResolved[0];
                cpu.Registers.H = lastIpResolved[1];
                cpu.Registers.E = lastIpResolved[2];
                cpu.Registers.D = lastIpResolved[3];
            }

            if (flags.GetBit(1) == 1)
            {
                return ERR_INV_IP;
            }

            dnsQueryInProgress = true;
            Dns.BeginGetHostAddresses(name,
                ar =>
                {
                    dnsQueryInProgress = false;
                    IPAddress[] addresses;
                    try
                    {
                        addresses = Dns.EndGetHostAddresses(ar);
                    }
                    catch (Exception ex)
                    {
                        if (ex is SocketException sockEx)
                            lastDnsError = UnapiDnsErrorFromSocketError(sockEx.SocketErrorCode);
                        else
                            lastDnsError = 0;

                        return;
                    }
                    var address = addresses.FirstOrDefault(IsIPv4);
                    lastIpResolved = address?.GetAddressBytes();
                    lastIpResolvedWasDirectIp = false;
                    lastDnsError = null;
                }
                ,null);

            cpu.Registers.B = 0;
            return ERR_OK;
        }

        private static byte UnapiDnsErrorFromSocketError(SocketError socketError)
        {
            switch (socketError)
            {
                case SocketError.ConnectionAborted:
                case SocketError.HostDown:
                    return 2;

                case SocketError.HostNotFound:
                case SocketError.NoData:
                    return 3;

                case SocketError.NetworkDown:
                    return 19;

                case SocketError.ConnectionRefused:
                case SocketError.ConnectionReset:
                    return 5;

                case SocketError.TimedOut:
                    return 17;

                default:
                    return 0;
            }
        }

        private byte TCPIP_DNS_S()
        {
            if (dnsQueryInProgress)
            {
                cpu.Registers.B = 1;
                cpu.Registers.C = 0;
                return ERR_OK;
            }

            var clearResults = cpu.Registers.B.GetBit(0) == 1;

            if (lastDnsError != null)
            {
                cpu.Registers.B = lastDnsError.Value;
                if (clearResults)
                    lastDnsError = null;
                return ERR_DNS;
            }

            if (lastIpResolved != null)
            {
                cpu.Registers.B = 2;
                cpu.Registers.C = lastIpResolvedWasDirectIp ? (byte)1 :(byte)0;

                cpu.Registers.L = lastIpResolved[0];
                cpu.Registers.H = lastIpResolved[1];
                cpu.Registers.E = lastIpResolved[2];
                cpu.Registers.D = lastIpResolved[3];

                if (clearResults)
                    lastIpResolved = null;
                return ERR_OK;
            }

            cpu.Registers.B = 0;
            return ERR_OK;
        }

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
                return new UdpDatagram {Data = data, RemoteEndpoint = remoteEndpoint};
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

            if(port != null && udpConnections.Any(c => c?.Port == port.Value))
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
                    cpu.Registers.B = (byte)(i+1);
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
            var destIp = new IPAddress(new byte[] {slots[paramsAddress], slots[paramsAddress+1], slots[paramsAddress+2], slots[paramsAddress+3]});
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
