namespace Konamiman.NestorMSX.Plugins.TcpipUnapi
{
    public partial class TcpipUnapiPlugin
    {
        private byte TCPIP_CONFIG_AUTOIP()
        {
            if (cpu.Registers.B > 1 || cpu.Registers.C > 1)
                return ERR_NOT_IMP;

            //We don't support manual setting of any IP address,
            //although per the spec we should

            cpu.Registers.C = 3;
            return ERR_OK;
        }

        private byte TCPIP_CONFIG_IP()
        {
            if (cpu.Registers.B > 6)
                return ERR_NOT_IMP;

            //We don't support manual setting of any IP address,
            //although per the spec we should

            return ERR_OK;
        }

        private byte TCPIP_CONFIG_TTL()
        {
            if (cpu.Registers.B == 1)
                return ERR_NOT_IMP;
            else if (cpu.Registers.B > 1)
                return ERR_INV_PAR;

            //We should return real TTL and ToS values here

            cpu.Registers.D = 64;
            cpu.Registers.E = 0;

            return ERR_OK;
        }

        private byte TCPIP_CONFIG_PING()
        {
            if (cpu.Registers.B == 1)
                return ERR_NOT_IMP;
            else if (cpu.Registers.B > 1)
                return ERR_INV_PAR;

            //We should perhaps check if the host OS is actually replying to pings...

            cpu.Registers.C = 1;
            return ERR_OK;
        }

        private byte TCPIP_WAIT()
        {
            //For apps that expect interrupts enabled after calling TCPIP_WAIT
            cpu.Registers.IFF1 = 1;
            cpu.Registers.IFF2 = 1;

            return ERR_OK;
        }
    }
}
