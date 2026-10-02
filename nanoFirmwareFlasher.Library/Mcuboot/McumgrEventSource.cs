// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Text;

namespace nanoFramework.Tools.FirmwareFlasher.Mcuboot
{
    /// <summary>
    /// ETW/Debug event source for MCUmgr SMP protocol tracing.
    /// Event source name: <c>nanoFramework-McumgrClient</c>.
    /// </summary>
    /// <remarks>
    /// In Debug builds with the <c>TRACE</c> compiler symbol defined, every event writes to
    /// <c>Debug.WriteLine</c> and is immediately visible in the Visual Studio Output window
    /// (no setup required).
    /// Otherwise the output is suppressed; attach an <see cref="EventListener"/> or
    /// use <c>dotnet-trace</c> / PerfView with the provider name
    /// <c>nanoFramework-McumgrClient</c> to capture events at runtime.
    /// </remarks>
    [EventSource(Name = "nanoFramework-McumgrClient")]
    internal sealed class McumgrEventSource : EventSource
    {
        /// <summary>Singleton instance used by <see cref="McumgrClient"/>.</summary>
        public static McumgrEventSource Log { get { return s_log.Value; } }

        private static readonly Lazy<McumgrEventSource> s_log =
            new Lazy<McumgrEventSource>(() => new McumgrEventSource());

        private McumgrEventSource() { }

        private static string GetGroupName(SmpGroup group) => group switch
        {
            SmpGroup.Os => "OS",
            SmpGroup.Image => "Image",
            SmpGroup.NanoFramework => "nanoFramework",
            _ => $"grp({(ushort)group})"
        };

        private static string GetCommandName(SmpGroup group, byte id) => (group, id) switch
        {
            (SmpGroup.Os, (byte)OsCommandId.Echo) => "Echo",
            (SmpGroup.Os, (byte)OsCommandId.Reset) => "Reset",
            (SmpGroup.Os, (byte)OsCommandId.McumgrParameters) => "McumgrParameters",
            (SmpGroup.Image, (byte)ImageCommandId.State) => "State",
            (SmpGroup.Image, (byte)ImageCommandId.Upload) => "Upload",
            (SmpGroup.NanoFramework, (byte)NfCommandId.DeploymentUpload) => "DeploymentUpload",
            (SmpGroup.NanoFramework, (byte)NfCommandId.DeploymentStatus) => "DeploymentStatus",
            (SmpGroup.NanoFramework, (byte)NfCommandId.DeploymentErase) => "DeploymentErase",
            (SmpGroup.NanoFramework, (byte)NfCommandId.DeviceInfo) => "DeviceInfo",
            _ => $"0x{id:X02}"
        };

        /// <summary>
        /// Formats <paramref name="bytes"/> as dash-separated uppercase hex
        /// (e.g. <c>A0-B1-C2</c>), truncated to the first <paramref name="maxBytes"/> bytes.
        /// </summary>
        internal static string FormatHex(byte[] bytes, int maxBytes = 64)
        {
            if (bytes is null || bytes.Length == 0)
            {
                return "(empty)";
            }

            int count = Math.Min(maxBytes, bytes.Length);
            var sb = new StringBuilder(count * 3);

            for (int i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    sb.Append('-');
                }

                sb.AppendFormat("{0:X2}", bytes[i]);
            }

            if (bytes.Length > maxBytes)
            {
                sb.Append($" (+{bytes.Length - maxBytes} more bytes)");
            }

            return sb.ToString();
        }

        /// <summary>Emitted immediately before an SMP frame is written to the serial port.</summary>
        [Event(1, Opcode = EventOpcode.Send)]
        public void SmpTxFrame(SmpOpCode op, SmpGroup group, byte id, byte seq, int payloadLen, McumgrSmpFrame frame)
        {
#if DEBUG && TRACE
            Debug.WriteLine($"SMP TX  {op} {GetGroupName(group)}/{GetCommandName(group, id)}  seq=0x{seq:X02} payloadLength={payloadLen}  {DateTime.Now:HH:mm:ss.fff}");
            Debug.WriteLine($"        header =[{FormatHex(frame.Header.ToBytes())}]");
            Debug.WriteLine($"        payload=[{FormatHex(frame.Payload)}]");
#endif
        }

        /// <summary>Emitted when a complete SMP frame has been decoded from the serial port.</summary>
        [Event(2, Opcode = EventOpcode.Receive)]
        public void SmpRxFrame(SmpOpCode op, SmpGroup group, byte id, byte seq, int payloadLen, byte[] payload, TimeSpan roundTrip)
        {
#if DEBUG && TRACE
            Debug.WriteLine($"SMP RX  {op} {GetGroupName(group)}/{GetCommandName(group, id)}  seq=0x{seq:X02} payLen={payloadLen}  round-trip={roundTrip:ss\\.ffff}  {DateTime.Now:HH:mm:ss.fff}");
            Debug.WriteLine($"        pld=[{FormatHex(payload)}]");
#endif
        }

        /// <summary>Emitted when the receive deadline expires before a complete frame arrives.</summary>
        [Event(3, Opcode = EventOpcode.Info)]
        public void SmpRxTimeout(SmpGroup group, byte id, byte seq, TimeSpan elapsed, byte[] receivedBytes)
        {
#if DEBUG && TRACE
            Debug.WriteLine($"SMP RX  {GetGroupName(group)}/{GetCommandName(group, id)} *** TIMEOUT ***  seq=0x{seq:X02} elapsed={elapsed:ss\\.ffff}  receivedBytes={receivedBytes?.Length ?? 0}");
            Debug.WriteLine($"        rcv=[{FormatHex(receivedBytes ?? Array.Empty<byte>())}]");
#endif
        }
    }
}
