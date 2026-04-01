// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.IO.Ports;

namespace nanoFramework.Tools.FirmwareFlasher.Esp32Serial
{
    /// <summary>
    /// Implements RFC 1055 SLIP (Serial Line Internet Protocol) byte-stuffing framing
    /// used by the ESP32 ROM bootloader serial protocol.
    /// </summary>
    internal static class SlipFraming
    {
        /// <summary>SLIP frame delimiter byte.</summary>
        internal const byte FrameEnd = 0xC0;

        /// <summary>SLIP escape byte.</summary>
        internal const byte FrameEsc = 0xDB;

        /// <summary>Escaped replacement for <see cref="FrameEnd"/> inside a frame.</summary>
        internal const byte FrameEscEnd = 0xDC;

        /// <summary>Escaped replacement for <see cref="FrameEsc"/> inside a frame.</summary>
        internal const byte FrameEscEsc = 0xDD;

        /// <summary>
        /// Wrap a raw payload in a SLIP frame (0xC0 ... 0xC0) with byte-stuffing.
        /// </summary>
        /// <param name="payload">Raw payload bytes to encode.</param>
        /// <returns>A SLIP-framed byte array ready to send over serial.</returns>
        internal static byte[] Encode(byte[] payload)
        {
            // Worst case: every byte needs escaping (2x) + 2 frame delimiters
            using var ms = new MemoryStream(payload.Length * 2 + 2);

            ms.WriteByte(FrameEnd);

            for (int i = 0; i < payload.Length; i++)
            {
                byte b = payload[i];

                switch (b)
                {
                    case FrameEnd:
                        ms.WriteByte(FrameEsc);
                        ms.WriteByte(FrameEscEnd);
                        break;

                    case FrameEsc:
                        ms.WriteByte(FrameEsc);
                        ms.WriteByte(FrameEscEsc);
                        break;

                    default:
                        ms.WriteByte(b);
                        break;
                }
            }

            ms.WriteByte(FrameEnd);

            return ms.ToArray();
        }

        /// <summary>
        /// Decode a SLIP-encoded payload by removing byte-stuffing.
        /// The input should NOT include the framing 0xC0 delimiters.
        /// </summary>
        /// <param name="encoded">SLIP-encoded bytes (without frame delimiters).</param>
        /// <returns>The decoded raw payload.</returns>
        internal static byte[] Decode(byte[] encoded)
        {
            using var ms = new MemoryStream(encoded.Length);

            for (int i = 0; i < encoded.Length; i++)
            {
                byte b = encoded[i];

                if (b == FrameEsc)
                {
                    i++;

                    if (i >= encoded.Length)
                    {
                        throw new InvalidOperationException("SLIP frame ends with incomplete escape sequence.");
                    }

                    byte next = encoded[i];

                    switch (next)
                    {
                        case FrameEscEnd:
                            ms.WriteByte(FrameEnd);
                            break;

                        case FrameEscEsc:
                            ms.WriteByte(FrameEsc);
                            break;

                        default:
                            throw new InvalidOperationException($"SLIP frame contains invalid escape sequence: 0xDB 0x{next:X2}.");
                    }
                }
                else
                {
                    ms.WriteByte(b);
                }
            }

            return ms.ToArray();
        }

        /// <summary>
        /// Read one complete SLIP frame from the serial port.
        /// Returns the de-stuffed payload (without framing bytes).
        /// </summary>
        /// <param name="port">Open serial port to read from.</param>
        /// <param name="timeoutMs">Maximum time in milliseconds to wait for a complete frame.</param>
        /// <returns>The decoded payload of the SLIP frame.</returns>
        /// <exception cref="TimeoutException">No complete frame received within the timeout.</exception>
        internal static byte[] ReadFrame(SerialPort port, int timeoutMs)
        {
            int originalTimeout = port.ReadTimeout;
            port.ReadTimeout = timeoutMs;

            try
            {
                // Skip leading garbage and find the start of a frame
                // The ESP32 ROM may emit garbage bytes before the first frame delimiter
                int b;

                do
                {
                    b = port.ReadByte();
                }
                while (b != FrameEnd);

                // Now read frame body bytes, collecting into a buffer
                using var body = new MemoryStream(256);
                bool inEscape = false;

                while (true)
                {
                    b = port.ReadByte();

                    if (b == FrameEnd)
                    {
                        if (inEscape)
                        {
                            throw new InvalidOperationException("SLIP frame contains incomplete escape sequence (0xDB followed by end-of-frame 0xC0).");
                        }

                        // End of frame — but only if we have data
                        // (consecutive 0xC0 bytes are treated as a single delimiter)
                        if (body.Length > 0)
                        {
                            break;
                        }

                        // Empty frame body — keep waiting for an actual data frame
                        continue;
                    }

                    if (b == FrameEsc)
                    {
                        inEscape = true;
                        continue;
                    }

                    if (inEscape)
                    {
                        inEscape = false;

                        switch (b)
                        {
                            case FrameEscEnd:
                                body.WriteByte(FrameEnd);
                                break;

                            case FrameEscEsc:
                                body.WriteByte(FrameEsc);
                                break;

                            default:
                                throw new InvalidOperationException($"SLIP frame contains invalid escape sequence: 0xDB 0x{b:X2}.");
                        }
                    }
                    else
                    {
                        body.WriteByte((byte)b);
                    }
                }

                return body.ToArray();
            }
            catch (TimeoutException)
            {
                throw new TimeoutException($"No complete SLIP frame received within {timeoutMs}ms.");
            }
            finally
            {
                port.ReadTimeout = originalTimeout;
            }
        }
    }
}
