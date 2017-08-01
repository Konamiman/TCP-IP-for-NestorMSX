using System;
using System.Collections.Generic;
using System.Linq;
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

        private static TcpipUnapiPlugin Instance;

        private readonly SlotNumber slotNumber;
        private readonly IZ80Processor cpu;
        private IExternallyControlledSlotsSystem slots;

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
            if (functionNumber < Routines.Length)
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
                TCPIP_GET_CAPAB
            };
        }

        private const int ERR_OK = 0;
        private const int ERR_NOT_IMP = 1;
        private const int ERR_INV_PAR = 4;

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
                        0 << 2 | // 2: Resolve host names by querying a DNS server
                        0 << 3 | // 3: Open TCP connections in active mode
                        0 << 4 | // 4: Open TCP connections in passive mode, with specified remote socket
                        0 << 5 | // 5: Open TCP connections in passive mode, with unsepecified remote socket
                        0 << 6 | // 6: Send and receive TCP urgent data
                        0 << 7 | // 7: Explicitly set the PUSH bit when sending TCP data
                        0 << 8 | // 8: Send data to a TCP connection before the ESTABLISHED state is reached
                        0 << 9 | // 9: Flush the output buffer of a TCP connection
                        0 << 10 | // 10: Open UDP connections
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

                    cpu.Registers.B = 0; //Link type = Unknown

                    break;

                case 2:
                    cpu.Registers.BC = 0x0404;
                    cpu.Registers.DE = 0x0404;
                    cpu.Registers.HL = 0;
                    break;

                case 3:
                    cpu.Registers.HL = 576;
                    cpu.Registers.DE = 576;
                    break;
            }

            return ERR_INV_PAR;
        }
    }
}
