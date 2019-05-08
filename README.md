# TCP/IP UNAPI plugin for NestorMSX #


## What is this? ##

This plugin provides Internet connectivity to [NestorMSX](http://github.com/konamiman/NestorMSX) by implementing the [TCP/IP UNAPI specification](http://www.konamiman.com/msx/msx-e.html#unapi). It doesn't implement the full specification, but enough of it to be useful for most people (and for developers of networking applications in particular).

What's supported:

* Domain name resolution (DNS)
* UDP connections
* TCP connections (active and passive)

What's not supported:

* Sending/receiving raw IP datagrams
* Sending ICMP echo requests (aka PINGs)
* Manually setting any of the IP addresses (they are all taken from the host OS)
* Enabling/disabling responses to received PINGs (this is handled by the host OS)
* TTL and ToS: can't change the values, and when reading them (with `TCPIP_CONFIG_TTL`) the returned values are always the same (TTL=64, ToS=0).

The underlying host OS networking API (in particular, the classes from .NET's `System.Net` and `System.Net.Sockets` namespaces) is used directly, so there are some limitations on what you can do (you can't e.g. open a passive TCP connection using a local port already in use). This shouldn't be an issue in most cases.


## How to use ##

1. Download the plugin DLL from "Releases" (or build it with Visual Studio) and copy it to the `plugins` directory of your NestorMSX install.

2. Open the `machine.config` file of the machine configuration you will use (a good one would be _MSX2 with Nextor_) and add the following to the `slots` section, modifying the slot number appropriately:

```
    "<slot number>": {
      "type": "TCP/IP UNAPI",
      "ipAddress": "192.168.0.1"
    }
```

`ipAddress` is optional and is used to select the host network interface that will be queried for connection information (such as the local and DNS IP addresses). If omitted, the first IPv4-capable interface will be used.

3. Launch NestorMSX and that's it, you have a functional TCP/IP UNAPI implementation available (you can check that by using [the UNAPI implementations lister](http://www.konamiman.com/msx/unapi/apilist.com): `apilist tcp/ip`). You don't need to install InterNestor or anything esle, you can use [networking applications](http://www.konamiman.com/msx/msx-e.html#networking) directly.

## But who am I? ##

I'm [Konamiman, the MSX freak](http://www.konamiman.com). No more, no less.
