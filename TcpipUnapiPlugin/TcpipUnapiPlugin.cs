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
                UNAPI_GET_INFO
            };
        }

        private const int ERR_OK = 0;
        private const int ERR_NOT_IMP = 1;

        private byte UNAPI_GET_INFO()
        {
            cpu.Registers.HL = ImplementationNameAddress;
            cpu.Registers.DE = 0x0100;
            cpu.Registers.BC = 0x0100;
            return ERR_OK;
        }
    }
}
