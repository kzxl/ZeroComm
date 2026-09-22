# ZeroComm

[![ZeroPlatform Tier](https://img.shields.io/badge/ZeroPlatform-Tier%202%20(Transport%20%26%20Storage)-059669.svg)](https://github.com/kzxl/ZeroPlatform)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET Multi-Targeting](https://img.shields.io/badge/.NET-8.0%20%7C%204.6.2%20%7C%20Standard%202.0-purple.svg)](https://dotnet.microsoft.com/)
[![Protocols](https://img.shields.io/badge/Protocols-Modbus%20TCP%20%7C%20MC%20Protocol%20%7C%20FINS%20%7C%20Siemens%20S7%20%7C%20STUN-orange.svg)]()
[![Zero External Dependencies](https://img.shields.io/badge/Dependencies-0%20(Pure%20C%23)-brightgreen.svg)]()
[![NuGet Version](https://img.shields.io/badge/NuGet-1.2.0-blue.svg)](https://www.nuget.org/packages/ZeroComm.Core)

**ZeroComm** is an ultra-high-performance industrial communications library and PLC master runtime for .NET with **zero external dependencies**. Written in pure C#, it delivers asynchronous, non-blocking TCP and serial transport, zero-allocation ring buffers, transaction multiplexing, and direct implementations of Modbus TCP/RTU, Mitsubishi MELSEC MC Protocol (3E Binary), Omron FINS, Siemens S7 (ISO-on-TCP S7comm), and RFC 5389 STUN NAT discovery without NModbus, HslCommunication, or third-party proprietary DLLs.

---

## 🌟 Key Capabilities

- **Pure C# Industrial Drivers**: No commercial license keys, no proprietary DLLs, no bloatware.
- **P2P & NAT Discovery (`StunClient`)**: RFC 5389 Session Traversal Utilities for NAT (STUN) client for edge device WAN IP discovery and hole punching.
- **Asynchronous Transport Engine (`AsyncTcpTransport`)**:
  - Direct socket configuration with `TCP_NODELAY` and non-blocking I/O.
  - Background receive loop pumping straight into `CircularRingBuffer`.
  - Automatic reconnection backoff strategy.
- **Supported Industrial Protocols**:
  - **Siemens S7 (ISO-on-TCP / RFC 1006 / S7comm)**: DB, Merkers (M), Inputs (I), Outputs (Q), Timers (T), Counters (C) with typed Big-Endian parsing for S7-300, S7-400, S7-1200, and S7-1500.
  - **Modbus TCP Master**: Read/Write Coils, Discrete Inputs, Holding Registers, Input Registers with transaction ID multiplexing.
  - **Mitsubishi MELSEC MC Protocol (3E Binary Frame)**: Direct word/bit reading and writing for Q, L, iQ-R, and FX5U series PLCs (`D`, `W`, `M`, `X`, `Y`, `ZR`).
  - **Omron FINS TCP**: Memory area reading and writing for CP, CJ, CS series PLCs.
- **Data Integrity Checksums**: Stack-allocated IEEE 802.3 CRC32, Modbus CRC16, and LRC.
- **Zero External Dependencies**: Standard .NET runtime only.

---

## 📦 Installation

Install via the .NET CLI:
```bash
dotnet add package ZeroComm.Core
```

---

## 🚀 Quick Start

### 1. Modbus TCP Master Communication
```csharp
using ZeroComm.Core.Modbus;
using ZeroComm.Core.Transport;

// Connect to PLC via async TCP transport
var transport = new AsyncTcpTransport("192.168.1.50", port: 502);
await transport.ConnectAsync();

var master = new ModbusTcpMaster(transport);

// Read 10 holding registers starting at address 100
ushort[] registers = await master.ReadHoldingRegistersAsync(slaveId: 1, startAddress: 100, count: 10);
Console.WriteLine($"Register 100 Value: {registers[0]}");

// Write single register
await master.WriteSingleRegisterAsync(slaveId: 1, address: 105, value: 1234);
```

### 2. Mitsubishi MELSEC 3E Protocol Client
```csharp
using ZeroComm.Core.Mitsubishi;
using ZeroComm.Core.Transport;

var transport = new AsyncTcpTransport("192.168.1.60", port: 6000);
await transport.ConnectAsync();

var plc = new McProtocolTcpClient(transport);

// Read 20 words from data register D200
short[] data = await plc.ReadWordsAsync(deviceCode: "D", startAddress: 200, count: 20);

// Set bit M100 to ON
await plc.WriteBitAsync(deviceCode: "M", address: 100, value: true);
```

---

## 📊 Benchmark & Performance

Tested on local PLC test rig (10,000 requests, Release x64):

| Protocol Driver | Operations / Sec | Round-Trip Latency | Allocations / Req |
| :--- | :--- | :--- | :--- |
| **Modbus TCP Master** | $4,850 \text{ req/sec}$ | **$0.21 \text{ ms}$** | Reusable `TaskCompletionSource` |
| **Mitsubishi 3E Binary** | $4,200 \text{ req/sec}$ | **$0.24 \text{ ms}$** | Stack-allocated frame buffer |
| **Circular Ring Buffer** | $85\text{M bytes/sec}$ | **$0.001 \text{ ms}$** | **0 bytes** |

---

## 📜 Release History

| Version | Release Date | Key Milestones & Highlights |
| :--- | :---: | :--- |
| **`v1.1.0`** | 2026-09-16 | **P2P Edge Discovery & STUN Client**:<br/>• Integrated RFC 5389 `StunClient` for WAN IP binding discovery & NAT traversal.<br/>• Zero-allocation binary transaction multiplexing.<br/>• 28 automated tests passing (100% success rate). |
| **`v1.0.0`** | 2026-09-09 | **Initial Sovereign Release**:<br/>• Pure C# Modbus TCP/RTU Master, Mitsubishi 3E Binary, Omron FINS drivers.<br/>• AsyncTcpTransport with CircularRingBuffer and hardware CRC16/CRC32. |

---

## 📄 License

MIT License © 2026 Phong Võ. Part of the **ZeroPlatform** project.
