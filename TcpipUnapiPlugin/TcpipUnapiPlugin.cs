using System;
using System.Collections.Generic;
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
    public partial class TcpipUnapiPlugin
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

        private bool IsIPv4(IPAddress ipAddress) =>
            ipAddress.AddressFamily == AddressFamily.InterNetwork;

        private bool NoNetworkAvailable()
        {
            return !(networkInterface.OperationalStatus == OperationalStatus.Up || networkInterface.OperationalStatus == OperationalStatus.Unknown);
        }
    }
}
