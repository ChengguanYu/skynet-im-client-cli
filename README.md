# KCP Protocol CLI Client

A C# console application that connects to a remote machine using the KCP (fast and reliable ARQ) protocol over UDP.

## Overview

This CLI client uses the [KcpSharp](https://github.com/yigolden/KcpSharp) library to establish reliable UDP connections via the KCP protocol. KCP provides lower average latency than TCP at the cost of slightly higher bandwidth usage, making it ideal for real-time applications.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) or later
- A KCP server running on the target host/port

## Project Structure

```
im/
├── im.csproj       # Project file with KcpSharp dependency
├── Program.cs      # Main application with command loop and KCP connection logic
├── .env            # Configuration file (HOST and PORT)
└── README.md       # This file
```

## Configuration

Edit the `.env` file in the project root to set the target server:

```env
HOST=127.0.0.1
PORT=12345
```

| Variable | Description                  | Default     |
|----------|------------------------------|-------------|
| `HOST`   | Remote server IP address     | `127.0.0.1` |
| `PORT`   | Remote server port (1-65535) | `12345`     |

Lines starting with `#` are treated as comments and ignored.

## Build

```bash
dotnet build
```

## Run

```bash
dotnet run
```

## Usage

After starting the application, you'll see an interactive prompt:

```
==============================================
  KCP Protocol CLI Client
==============================================
Type 'help' for available commands.

[disconnected]>
```

### Available Commands

| Command              | Description                              |
|----------------------|------------------------------------------|
| `connect`            | Connect to the configured KCP server     |
| `disconnect`         | Disconnect from the server               |
| `send <message>`     | Send a text message to the server        |
| `status`             | Show current connection status           |
| `config`             | Show current HOST/PORT configuration     |
| `help`               | Show available commands                  |
| `quit` / `exit`      | Disconnect and exit the application      |

### Example Session

```
[disconnected]> config
  HOST = 127.0.0.1
  PORT = 12345
[disconnected]> connect
[INFO] Connecting to 127.0.0.1:12345 via KCP...
[OK] Connected to 127.0.0.1:12345 (conversation ID: 1)
[connected]> send Hello, KCP server!
[SENT] Hello, KCP server!
[RECV] Hello from server!
[connected]> status
[STATUS] Connected to 127.0.0.1:12345
[connected]> disconnect
[INFO] Disconnecting...
[OK] Disconnected.
[disconnected]> quit
[INFO] Goodbye.
```

### Graceful Shutdown

- Type `quit` or `exit` to disconnect and exit cleanly
- Press `Ctrl+C` to force shutdown at any time

## Technical Details

- **Protocol**: KCP (reliable UDP with ARQ)
- **Library**: [KcpSharp 0.8.8](https://www.nuget.org/packages/KcpSharp) - Pure C# implementation, no native dependencies
- **Transport**: UDP socket with KCP conversation multiplexing
- **Mode**: Message mode (non-stream) by default
- **MTU**: 1400 bytes (configurable in code)
- **Conversation ID**: 1 (hardcoded for single-connection client)

## Error Handling

The client handles the following error scenarios:

- **Invalid host address**: Reports format errors and cleans up
- **Socket errors**: Catches and reports network-level failures
- **Connection refused**: Reports when the remote server is unreachable
- **Transport closed**: Detects remote disconnection and updates status
- **Send failures**: Reports when messages cannot be delivered
- **Graceful shutdown**: Cleans up all resources on exit

## License

This project is provided as-is for development and testing purposes.
