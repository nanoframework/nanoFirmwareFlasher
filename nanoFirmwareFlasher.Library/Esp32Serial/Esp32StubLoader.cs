// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Text;
using System.Threading;

namespace nanoFramework.Tools.FirmwareFlasher.Esp32Serial
{
    /// <summary>
    /// Uploads a pre-compiled stub loader into the ESP32 chip's RAM and starts it.
    /// After the stub is running, the bootloader client can use enhanced commands
    /// (compressed writes, baud rate change, MD5 verification).
    /// </summary>
    internal static class Esp32StubLoader
    {
        /// <summary>Default block size for memory uploads.</summary>
        private const int MemBlockSize = 0x1800; // 6 KB — matches esptool

        /// <summary>Timeout for MEM_END (stub starts executing, may take a moment).</summary>
        private const int MemEndTimeoutMs = 10_000;

        /// <summary>The "OHAI" greeting sent by the stub after it starts running.</summary>
        private static readonly byte[] StubGreeting = Encoding.ASCII.GetBytes("OHAI");

        /// <summary>
        /// Upload and execute the stub loader for the detected chip type.
        /// Returns true if the stub was successfully uploaded and is running.
        /// Returns false if no stub is available for the chip (ROM mode continues).
        /// </summary>
        /// <param name="client">Connected bootloader client (must be synced).</param>
        /// <param name="chipType">Chip type string (e.g. "esp32", "esp32s3").</param>
        /// <param name="verbosity">Verbosity level for output.</param>
        /// <returns>True if stub is now running; false if ROM-only mode.</returns>
        internal static bool UploadStub(
            Esp32BootloaderClient client,
            string chipType,
            VerbosityLevel verbosity = VerbosityLevel.Quiet)
        {
            // Try to load the stub image for this chip
            var stub = Esp32StubImage.TryLoad(chipType);

            if (stub == null)
            {
                if (verbosity >= VerbosityLevel.Detailed)
                {
                    OutputWriter.WriteLine($"No stub loader available for {chipType}, using ROM bootloader.");
                }

                return false;
            }

            if (verbosity >= VerbosityLevel.Detailed)
            {
                OutputWriter.WriteLine($"Uploading stub loader for {chipType}...");
            }

            // Upload text segment
            UploadSegment(client, stub.Text, stub.TextStart);

            // Upload data segment (if present)
            if (stub.Data.Length > 0)
            {
                UploadSegment(client, stub.Data, stub.DataStart);
            }

            // Execute the stub by sending MEM_END with the entry point
            ExecuteStub(client, stub.Entry);

            // Wait for the "OHAI" greeting from the stub
            WaitForGreeting(client);

            // Mark the client as running the stub
            client.IsStubRunning = true;

            if (verbosity >= VerbosityLevel.Detailed)
            {
                OutputWriter.WriteLine("Stub loader is running.");
            }

            return true;
        }

        /// <summary>
        /// Upload a memory segment (text or data) to the chip's RAM.
        /// Uses MEM_BEGIN + MEM_DATA sequence.
        /// </summary>
        private static void UploadSegment(
            Esp32BootloaderClient client,
            byte[] data,
            uint address)
        {
            int numBlocks = (data.Length + MemBlockSize - 1) / MemBlockSize;

            // MEM_BEGIN: [total_size:4][num_blocks:4][block_size:4][offset:4]
            byte[] beginData = new byte[16];
            Esp32CommandPacket.WriteUInt32LE(beginData, 0, (uint)data.Length);
            Esp32CommandPacket.WriteUInt32LE(beginData, 4, (uint)numBlocks);
            Esp32CommandPacket.WriteUInt32LE(beginData, 8, (uint)MemBlockSize);
            Esp32CommandPacket.WriteUInt32LE(beginData, 12, address);

            var response = client.SendCommand(Esp32Command.MemBegin, beginData);
            response.ThrowIfError();

            // MEM_DATA: send each block
            for (int seq = 0; seq < numBlocks; seq++)
            {
                int offset = seq * MemBlockSize;
                int remaining = data.Length - offset;
                int chunkSize = Math.Min(MemBlockSize, remaining);

                byte[] block = new byte[chunkSize];
                Buffer.BlockCopy(data, offset, block, 0, chunkSize);

                // MEM_DATA payload: [data_size:4][sequence_num:4][0:4][0:4][block_data]
                byte[] payload = new byte[16 + chunkSize];
                Esp32CommandPacket.WriteUInt32LE(payload, 0, (uint)chunkSize);
                Esp32CommandPacket.WriteUInt32LE(payload, 4, (uint)seq);
                Esp32CommandPacket.WriteUInt32LE(payload, 8, 0);
                Esp32CommandPacket.WriteUInt32LE(payload, 12, 0);
                Buffer.BlockCopy(block, 0, payload, 16, chunkSize);

                uint checksum = Esp32CommandPacket.CalculateChecksum(block);

                var dataResponse = client.SendCommand(
                    Esp32Command.MemData,
                    payload,
                    checksum,
                    timeoutMs: 10_000);

                dataResponse.ThrowIfError();
            }
        }

        /// <summary>
        /// Send MEM_END to begin executing the stub at the entry point.
        /// Some chips don't send a response to MEM_END before jumping to the stub,
        /// so we use a short timeout and ignore failures (matching esptool behavior).
        /// </summary>
        private static void ExecuteStub(Esp32BootloaderClient client, uint entryPoint)
        {
            // MEM_END: [execute_flag:4][entry_point:4]
            // execute_flag: 0 = execute entry point
            byte[] data = new byte[8];
            Esp32CommandPacket.WriteUInt32LE(data, 0, 0); // 0 = execute
            Esp32CommandPacket.WriteUInt32LE(data, 4, entryPoint);

            try
            {
                var response = client.SendCommand(
                    Esp32Command.MemEnd,
                    data,
                    timeoutMs: 500); // Short timeout — some chips jump without responding

                response.ThrowIfError();
            }
            catch (TimeoutException)
            {
                // Expected on some chip revisions — stub may already be running
            }
        }

        /// <summary>
        /// Wait for the stub's "OHAI" greeting response.
        /// The stub sends "OHAI" as raw bytes (not SLIP framed) after it starts.
        /// </summary>
        private static void WaitForGreeting(Esp32BootloaderClient client)
        {
            // The stub sends "OHAI" as raw ASCII bytes after startup
            // We need to read raw bytes, not SLIP frames
            var port = client.Port;
            byte[] buffer = new byte[4];
            int bytesRead = 0;
            int timeoutMs = 5000;

            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

            try
            {
                while (bytesRead < 4 && DateTime.UtcNow < deadline)
                {
                    try
                    {
                        int remainingMs = Math.Max(100, (int)(deadline - DateTime.UtcNow).TotalMilliseconds);
                        port.ReadTimeout = remainingMs;

                        int b = port.ReadByte();

                        if (b < 0)
                        {
                            continue;
                        }

                        // Check if this byte matches the expected greeting sequence
                        if ((byte)b == StubGreeting[bytesRead])
                        {
                            buffer[bytesRead] = (byte)b;
                            bytesRead++;
                        }
                        else
                        {
                            // Reset if it doesn't match
                            bytesRead = 0;

                            // Check if this byte starts the greeting
                            if ((byte)b == StubGreeting[0])
                            {
                                buffer[0] = (byte)b;
                                bytesRead = 1;
                            }
                        }
                    }
                    catch (TimeoutException)
                    {
                        break;
                    }
                }
            }
            finally
            {
                // Always restore default timeout
                port.ReadTimeout = Esp32BootloaderClient.DefaultTimeoutMs;
            }

            if (bytesRead < 4)
            {
                throw new EspToolExecutionException(
                    "Stub loader failed to start: did not receive 'OHAI' greeting.");
            }
        }
    }
}
