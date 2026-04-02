// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace nanoFramework.Tools.FirmwareFlasher.Esp32Serial
{
    /// <summary>
    /// Parsed response packet from the ESP32 bootloader.
    /// 
    /// Response packet structure (Device → Host):
    ///   Byte 0:      Direction (0x01 = response)
    ///   Byte 1:      Command opcode
    ///   Bytes 2-3:   Data length (little-endian uint16)
    ///   Bytes 4-7:   Value (little-endian uint32)
    ///   Bytes 8+:    Data payload (includes status bytes at end)
    /// 
    /// Status trailer:
    ///   ROM bootloader: 4 bytes [status, error, 0x00, 0x00]
    ///   Stub loader:    2 bytes [status, error]
    /// </summary>
    internal class Esp32ResponsePacket
    {
        /// <summary>Response direction byte.</summary>
        internal const byte ResponseDirection = 0x01;

        /// <summary>Minimum response packet size (header only, no extra data).</summary>
        internal const int MinimumPacketSize = 8;

        /// <summary>Number of status trailer bytes from the ROM bootloader.</summary>
        internal const int RomStatusSize = 4;

        /// <summary>Number of status trailer bytes from the stub loader.</summary>
        internal const int StubStatusSize = 2;

        /// <summary>The command this is a response to.</summary>
        internal Esp32Command Command { get; }

        /// <summary>The 4-byte value field from the response header.</summary>
        internal uint Value { get; }

        /// <summary>Payload data (excluding the status trailer).</summary>
        internal byte[] Data { get; }

        /// <summary>Status code from the response (0 = success).</summary>
        internal byte StatusCode { get; }

        /// <summary>Error detail code when <see cref="StatusCode"/> is non-zero.</summary>
        internal byte ErrorCode { get; }

        /// <summary>Whether the response indicates success.</summary>
        internal bool IsSuccess => StatusCode == 0;

        private Esp32ResponsePacket(
            Esp32Command command,
            uint value,
            byte[] data,
            byte statusCode,
            byte errorCode)
        {
            Command = command;
            Value = value;
            Data = data;
            StatusCode = statusCode;
            ErrorCode = errorCode;
        }

        /// <summary>
        /// Parse a de-framed SLIP payload into a structured response.
        /// </summary>
        /// <param name="payload">The raw decoded payload from a SLIP frame (without frame delimiters).</param>
        /// <param name="isStubRunning">If true, expect 2-byte status trailer (stub); otherwise 4-byte (ROM).</param>
        /// <returns>A parsed response packet.</returns>
        /// <exception cref="InvalidOperationException">The payload is too short or has invalid direction.</exception>
        internal static Esp32ResponsePacket Parse(byte[] payload, bool isStubRunning = false)
        {
            if (payload == null || payload.Length < MinimumPacketSize)
            {
                throw new InvalidOperationException(
                    $"ESP32 response packet too short: expected at least {MinimumPacketSize} bytes, got {payload?.Length ?? 0}.");
            }

            byte direction = payload[0];

            if (direction != ResponseDirection)
            {
                throw new InvalidOperationException(
                    $"ESP32 response has invalid direction: expected 0x{ResponseDirection:X2}, got 0x{direction:X2}.");
            }

            var command = (Esp32Command)payload[1];
            ushort dataLength = (ushort)(payload[2] | (payload[3] << 8));
            uint value = Esp32CommandPacket.ReadUInt32LE(payload, 4);

            // The data section follows the 8-byte header
            byte[] resultData;
            byte statusCode = 0;
            byte errorCode = 0;

            if (payload.Length > MinimumPacketSize)
            {
                int availableData = payload.Length - MinimumPacketSize;

                // Status trailer size depends on whether ROM or stub is responding:
                //   ROM:  4 bytes [status, error, 0x00, 0x00]
                //   Stub: 2 bytes [status, error]
                int statusSize = isStubRunning ? StubStatusSize : RomStatusSize;

                if (availableData >= statusSize)
                {
                    int dataSize = availableData - statusSize;
                    int statusOffset = MinimumPacketSize + dataSize;

                    // Extract status from first 2 bytes of the status trailer
                    statusCode = payload[statusOffset];
                    errorCode = payload[statusOffset + 1];

                    // Extract data before the status trailer
                    if (dataSize > 0)
                    {
                        resultData = new byte[dataSize];
                        Buffer.BlockCopy(payload, MinimumPacketSize, resultData, 0, dataSize);
                    }
                    else
                    {
                        resultData = Array.Empty<byte>();
                    }
                }
                else
                {
                    // Not enough bytes for full status trailer; treat all as data
                    resultData = new byte[availableData];
                    Buffer.BlockCopy(payload, MinimumPacketSize, resultData, 0, availableData);
                }
            }
            else
            {
                resultData = Array.Empty<byte>();
            }

            return new Esp32ResponsePacket(command, value, resultData, statusCode, errorCode);
        }

        /// <summary>
        /// Throw a descriptive exception if this response indicates failure.
        /// </summary>
        /// <exception cref="Esp32BootloaderException">The response indicates an error.</exception>
        internal void ThrowIfError()
        {
            if (!IsSuccess)
            {
                throw new Esp32BootloaderException(Command, ErrorCode);
            }
        }

        /// <summary>
        /// Get a human-readable description of an ESP32 bootloader error code.
        /// </summary>
        internal static string GetErrorDescription(byte errorCode)
        {
            return errorCode switch
            {
                0x05 => "Received message is invalid",
                0x06 => "Failed to act on received message",
                0x07 => "Invalid CRC in message",
                0x08 => "Flash write error",
                0x09 => "Flash read error",
                0x0A => "Flash read length error",
                0x0B => "Deflate error",
                _ => $"Unknown error (0x{errorCode:X2})"
            };
        }
    }

    /// <summary>
    /// Exception thrown when the ESP32 bootloader returns an error response.
    /// </summary>
    internal class Esp32BootloaderException : Exception
    {
        /// <summary>The command that failed.</summary>
        internal Esp32Command Command { get; }

        /// <summary>The bootloader error code.</summary>
        internal byte ErrorCode { get; }

        internal Esp32BootloaderException(Esp32Command command, byte errorCode)
            : base($"ESP32 bootloader error for command {command}: {Esp32ResponsePacket.GetErrorDescription(errorCode)}")
        {
            Command = command;
            ErrorCode = errorCode;
        }
    }
}
