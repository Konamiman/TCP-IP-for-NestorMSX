# TCP/IP UNAPI plugin for NestorMSX #


## What is this? ##

This plugin provides Internet connectivity to [NestorMSX](http://github.com/konamiman/NestorMSX) by implementing the [TCP/IP UNAPI specification](http://www.konamiman.com/msx/msx-e.html#unapi).

For now it only implements domain name resolution and sending/receiving UDP datagrams.


## How to use ##

1. Build the plugin with Visual Studio and copy it to the `plugins` directory of your NestorMSX install.

2. Modify the `machine.config` file of the machine configuration you will use (a good one would be _MSX2 with Nextor_) and add the following to the `slots` section, modifying the slot number appropriately:

```
    "2": {
      "type": "TCP/IP UNAPI",
      "ipAddress: "192.168.0.1"
    }
```

`ipAddress` is optional and is used to select the host network interface that will be queried for connection information (such as the local and DNS IP addresses or the MTU). If omitted, the first IPv4-capable interface will be used.

## But who am I? ##

I'm [Konamiman, the MSX freak](http://www.konamiman.com). No more, no less.