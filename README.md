# TCP/IP UNAPI plugin for NestorMSX #


## What is this? ##

This plugin provides Internet connectivity to [NestorMSX](http://github.com/konamiman/NestorMSX) by implementing the [TCP/IP UNAPI specification](http://www.konamiman.com/msx/msx-e.html#unapi).

For now it does nothing, though: only the UNAPI_GET_INFO and TCPIP_GET_CAPAB routines are implemented, so the implementation will be detected by the `apilist.com` tool and you can use the `tcpip.com` tool with the `f` parameter, but that's it.


## How to use ##

1. Build the plugin with Visual Studio and copy it to the `plugins` directory of your NestorMSX install.

2. Modify the `machine.config` file of the machine configuration you will use (a good one would be _MSX2 with Nextor_) and add the following to the `slots` section, modifying the slot number appropriately:

```
    "2": {
	  "type": "TCP/IP UNAPI"
	}
```

## But who am I? ##

I'm [Konamiman, the MSX freak](http://www.konamiman.com). No more, no less.