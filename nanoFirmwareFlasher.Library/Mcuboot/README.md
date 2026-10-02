# MCUmgr SMP Protocol Tracer

`McumgrEventSource` (ETW source name: **`nanoFramework-McumgrClient`**) records every SMP frame
sent and received by `McumgrClient`, making it straightforward to debug protocol
implementation issues and verify the byte-level data exchange with a device.

---

## How it works

`McumgrClient` calls `McumgrEventSource.Log` at three points:

| Event | Opcode | When |
|---|---|---|
| `SmpTxFrame` | Send | Just before a frame is written to the serial port |
| `SmpRxFrame` | Receive | When a complete, CRC-verified frame is decoded |
| `SmpRxTimeout` | Info | When the receive deadline expires (shows whatever bytes arrived) |

Each event line contains: op-code, group/command name, sequence number, payload length,
header bytes as hex, payload bytes as hex (first 64 bytes), and a wall-clock timestamp.
Round-trip time is included on `SmpRxFrame`; elapsed time and received-byte dump on
`SmpRxTimeout`.

---

## Debug build — Visual Studio Output window

In a **Debug** build all events emit unconditionally via `Debug.WriteLine`.
No configuration is required.

1. Open the **Output** window (`View → Output` or `Ctrl+Alt+O`).
2. Select **Debug** from the *Show output from* drop-down.
3. Run the tool or a test with the debugger attached (F5) or without (Ctrl+F5,
   if `Debugger.Launch()` or a debug listener is configured).

Sample output for a single-chunk upload followed by an image-state query:

```
SMP TX  Write Image/Upload  seq=0x03 payloadLength=140  14:22:05.123
        header =[02-00-00-8C-00-01-03-01]
        payload=[A4-64-64-61-74-61-58-80-00-01-02-03 (+128 more bytes)]
SMP RX  WriteResponse Image/Upload  seq=0x03 payLen=5  round-trip=00.1823  14:22:05.306
        pld=[A1-62-72-63-00]
SMP TX  Read Image/State  seq=0x04 payloadLength=1  14:22:05.310
        header =[00-00-00-01-00-01-04-00]
        payload=[A0]
SMP RX  ReadResponse Image/State  seq=0x04 payLen=87  round-trip=00.0941  14:22:05.404
        pld=[A1-66-69-6D-61-67-65-73-81-A7-65-69-6D-61-67-65-73 (+71 more bytes)]
```

A timeout looks like:

```
SMP RX  Image/Upload *** TIMEOUT ***  seq=0x05 elapsed=05.0001  receivedBytes=3
        rcv=[06-09-41]
```

The three received bytes (`06-09-41`) show a partial start-of-frame marker followed by
a truncated base64 line — useful for confirming that framing bytes are being sent but
the device is not completing its response.

---

## dotnet-trace / PerfView (Release builds, no recompile)

> **Note:** The current implementation emits trace data exclusively through
> `Debug.WriteLine` (active in Debug builds only). The `[EventSource]` / `[Event]`
> attributes provide the ETW metadata scaffolding; to enable proper ETW emission in
> Release builds (for `dotnet-trace`, PerfView, or a custom `EventListener`), add
> `WriteEvent(...)` calls inside each event method and use primitive parameter types.

If `WriteEvent` is added in the future, the commands below will work:

```powershell
# Install the tool once
dotnet tool install -g dotnet-trace

# Collect while running nanoff
dotnet-trace collect --providers "nanoFramework-McumgrClient:0x1:5" -- nanoff list images mcuboot serialport COM3

# Or attach to a running process (replace <PID>)
dotnet-trace collect --process-id <PID> --providers "nanoFramework-McumgrClient:0x1:5"
```

Provider string format: `<name>:<keywords-hex>:<level>` — level 5 = Verbose.

Open the resulting `.nettrace` file in **Visual Studio Diagnostics**, **PerfView**, or
convert it with:

```powershell
dotnet-trace convert --format Speedscope trace.nettrace
```

---

## Custom EventListener (test / tooling integration)

An `EventListener` can intercept events in-process. Again, this requires `WriteEvent`
calls inside the event methods to produce data; the snippet below shows the wiring:

```csharp
using System.Diagnostics.Tracing;

sealed class SmpTraceListener : EventListener
{
    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        if (eventSource.Name == "nanoFramework-McumgrClient")
        {
            EnableEvents(eventSource, EventLevel.Verbose);
        }
    }

    protected override void OnEventWritten(EventWrittenEventArgs e)
    {
        Console.WriteLine($"[{e.EventName}] {string.Join(", ", e.Payload ?? [])}");
    }
}

// In your test or Main:
using var listener = new SmpTraceListener();
using var client = new McumgrClient("COM3");
client.Open();
await client.GetImageListAsync();   // events will appear on Console
```

---

## Adjusting payload truncation

`McumgrEventSource.FormatHex` truncates payloads at 64 bytes by default to keep output
readable during bulk uploads. To see the full payload during a debugging session, call
`FormatHex` directly with a higher `maxBytes` value, or temporarily increase the default
in `McumgrEventSource.cs`.
