using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Konamiman.Z80dotNet;

namespace Konamiman.NestorMSX.Plugins.TcpipUnapi
{
    public partial class TcpipUnapiPlugin
    {
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
            while (slots[namePointer] != 0)
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

                return ERR_OK;
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
                , null);

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
                cpu.Registers.C = lastIpResolvedWasDirectIp ? (byte)1 : (byte)0;

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
    }
}
