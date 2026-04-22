// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;

namespace nanoFramework.Tools.FirmwareFlasher.Esp32Serial
{
    /// <summary>
    /// Controls flash read/write/erase operations via the ESP32 bootloader protocol.
    /// Handles the FLASH_BEGIN/FLASH_DATA/FLASH_END command sequence for writes,
    /// READ_FLASH for reads, and ERASE_FLASH/ERASE_REGION for erases.
    /// </summary>
    internal class Esp32FlashController
    {
        /// <summary>Default flash write block size for ROM bootloader (16 KB).</summary>
        internal const int RomFlashBlockSize = 0x4000;

        /// <summary>Erase block size (4 KB, minimum erase unit for NOR flash).</summary>
        internal const int EraseBlockSize = 0x1000;

        /// <summary>Read block size (4 KB).</summary>
        internal const int ReadBlockSize = 0x1000;

        /// <summary>Fill byte used for padding the last block.</summary>
        private const byte PadByte = 0xFF;

        /// <summary>Timeout for flash begin (handles erase, can be slow).</summary>
        private const int FlashBeginTimeoutMs = 120_000;

        /// <summary>Timeout for mass erase.</summary>
        private const int MassEraseTimeoutMs = 120_000;

        /// <summary>Timeout for flash data write operations.</summary>
        private const int FlashDataTimeoutMs = 10_000;

        /// <summary>Timeout for flash read operations.</summary>
        private const int FlashReadTimeoutMs = 30_000;

        /// <summary>Maximum number of unacknowledged read blocks in flight (matches esptool FLASH_READ_MAX_INFLIGHT).</summary>
        private const uint FlashReadMaxInFlight = 64;

        private readonly Esp32BootloaderClient _client;
        private readonly Esp32ChipConfig _config;

        /// <summary>
        /// Create a flash controller bound to a connected bootloader client.
        /// </summary>
        /// <param name="client">Connected bootloader client.</param>
        /// <param name="config">Chip configuration (for block size and other parameters).</param>
        internal Esp32FlashController(Esp32BootloaderClient client, Esp32ChipConfig config)
        {
            _client = client;
            _config = config;
        }

        #region Flash Write

        /// <summary>
        /// Write binary data to flash at the specified address.
        /// Uses the FLASH_BEGIN/FLASH_DATA/FLASH_END protocol sequence.
        /// The ROM bootloader erases the target region as part of FLASH_BEGIN.
        /// </summary>
        /// <param name="address">Flash start address.</param>
        /// <param name="data">Binary data to write.</param>
        /// <param name="progress">Optional progress callback (bytesWritten, totalBytes).</param>
        internal void WriteFlash(
            uint address,
            byte[] data,
            Action<int, int> progress = null)
        {
            int blockSize = _config.FlashWriteBlockSize;
            int totalBlocks = (data.Length + blockSize - 1) / blockSize;
            int totalSize = data.Length;

            // Step 1: FLASH_BEGIN — initiates flash write and erases the target region
            SendFlashBegin(totalSize, totalBlocks, blockSize, address);

            // Step 2: FLASH_DATA — send each block
            for (int seq = 0; seq < totalBlocks; seq++)
            {
                int offset = seq * blockSize;
                int remaining = totalSize - offset;
                int chunkSize = Math.Min(blockSize, remaining);

                // Build the padded block (pad to blockSize with 0xFF)
                byte[] block = new byte[blockSize];

                if (chunkSize < blockSize)
                {
                    // Fill with pad byte first, then copy data
                    for (int i = 0; i < blockSize; i++)
                    {
                        block[i] = PadByte;
                    }
                }

                Buffer.BlockCopy(data, offset, block, 0, chunkSize);

                SendFlashData(block, seq);

                progress?.Invoke(Math.Min(offset + blockSize, totalSize), totalSize);
            }

            // Step 3: FLASH_END — stay in bootloader (action=1 means don't reboot)
            SendFlashEnd(reboot: false);
        }

        /// <summary>
        /// Write multiple partitions to flash.
        /// Each entry maps a flash address to a file path.
        /// </summary>
        /// <param name="partsToWrite">Dictionary mapping flash address → file path.</param>
        /// <param name="progress">Optional progress callback (fileName, bytesWritten, totalBytes).</param>
        internal void WriteFlash(
            Dictionary<int, string> partsToWrite,
            Action<string, int, int> progress = null)
        {
            foreach (var part in partsToWrite)
            {
                uint address = (uint)part.Key;
                string filePath = part.Value;

                byte[] data = File.ReadAllBytes(filePath);
                string fileName = Path.GetFileName(filePath);

                WriteFlash(address, data, (current, total) =>
                {
                    progress?.Invoke(fileName, current, total);
                });
            }
        }

        /// <summary>
        /// Send FLASH_BEGIN command.
        /// Data format: [total_size:4][num_blocks:4][block_size:4][offset:4]
        /// The ROM bootloader erases the region as part of this command.
        /// </summary>
        private void SendFlashBegin(int totalSize, int numBlocks, int blockSize, uint offset)
        {
            byte[] data = new byte[16];
            Esp32CommandPacket.WriteUInt32LE(data, 0, (uint)totalSize);
            Esp32CommandPacket.WriteUInt32LE(data, 4, (uint)numBlocks);
            Esp32CommandPacket.WriteUInt32LE(data, 8, (uint)blockSize);
            Esp32CommandPacket.WriteUInt32LE(data, 12, offset);

            // FLASH_BEGIN may take a long time due to flash erase
            var response = _client.SendCommand(
                Esp32Command.FlashBegin,
                data,
                timeoutMs: FlashBeginTimeoutMs);

            response.ThrowIfError();
        }

        /// <summary>
        /// Send FLASH_DATA command for a single block.
        /// Data format: [data_size:4][sequence_num:4][0:4][0:4][block_data:blockSize]
        /// </summary>
        private void SendFlashData(byte[] block, int sequenceNumber)
        {
            // Header: 16 bytes before the actual data
            byte[] payload = new byte[16 + block.Length];
            Esp32CommandPacket.WriteUInt32LE(payload, 0, (uint)block.Length);
            Esp32CommandPacket.WriteUInt32LE(payload, 4, (uint)sequenceNumber);
            Esp32CommandPacket.WriteUInt32LE(payload, 8, 0);
            Esp32CommandPacket.WriteUInt32LE(payload, 12, 0);
            Buffer.BlockCopy(block, 0, payload, 16, block.Length);

            // Checksum covers the actual block data, not the header
            uint checksum = Esp32CommandPacket.CalculateChecksum(block);

            var response = _client.SendCommand(
                Esp32Command.FlashData,
                payload,
                checksum,
                FlashDataTimeoutMs);

            response.ThrowIfError();
        }

        /// <summary>
        /// Send FLASH_END command.
        /// Data format: [action:4] where 0=reboot, 1=stay in bootloader.
        /// </summary>
        private void SendFlashEnd(bool reboot)
        {
            byte[] data = new byte[4];
            Esp32CommandPacket.WriteUInt32LE(data, 0, reboot ? 0u : 1u);

            var response = _client.SendCommand(Esp32Command.FlashEnd, data);
            response.ThrowIfError();
        }

        #endregion

        #region Compressed Flash Write (Stub Only)

        /// <summary>Default block size for compressed writes (matches esptool stub).</summary>
        internal const int CompressedBlockSize = 0x4000;

        /// <summary>
        /// Write binary data to flash using deflate compression (requires stub loader).
        /// Significantly faster for data with high compressibility.
        /// </summary>
        /// <param name="address">Flash start address.</param>
        /// <param name="data">Uncompressed binary data to write.</param>
        /// <param name="progress">Optional progress callback (bytesWritten, totalBytes).</param>
        /// <exception cref="InvalidOperationException">Stub loader is not running.</exception>
        internal void WriteFlashCompressed(
            uint address,
            byte[] data,
            Action<int, int> progress = null)
        {
            byte[] compressed = CompressZlib(data);
            WriteFlashCompressed(address, data, compressed, progress);
        }

        /// <summary>
        /// Write pre-compressed binary data to flash using deflate compression (requires stub loader).
        /// The caller supplies both uncompressed and compressed data so that compression
        /// does not need to be repeated when the caller already has the compressed form.
        /// Progress callback receives (compressedBytesSent, compressedTotal).
        /// </summary>
        internal void WriteFlashCompressed(
            uint address,
            byte[] data,
            byte[] compressed,
            Action<int, int> progress = null)
        {
            if (!_client.IsStubRunning)
            {
                throw new InvalidOperationException(
                    "Compressed flash writes require the stub loader to be running.");
            }

            int blockSize = CompressedBlockSize;
            int totalBlocks = (compressed.Length + blockSize - 1) / blockSize;

            // FLASH_DEFL_BEGIN: [erase_size:4 (uncompressed)][num_blocks:4][block_size:4][offset:4]
            SendFlashDeflBegin(data.Length, totalBlocks, blockSize, address);

            // FLASH_DEFL_DATA: send each compressed block
            for (int seq = 0; seq < totalBlocks; seq++)
            {
                int offset = seq * blockSize;
                int remaining = compressed.Length - offset;
                int chunkSize = Math.Min(blockSize, remaining);

                byte[] block = new byte[chunkSize];
                Buffer.BlockCopy(compressed, offset, block, 0, chunkSize);

                SendFlashDeflData(block, seq);

                // Report compressed bytes sent
                progress?.Invoke(Math.Min(offset + chunkSize, compressed.Length), compressed.Length);
            }

            // FLASH_DEFL_END
            SendFlashDeflEnd(reboot: false);
        }

        /// <summary>
        /// Write multiple partitions to flash using compression (requires stub).
        /// Falls back to uncompressed writes if stub is not running.
        /// </summary>
        internal void WriteFlashCompressed(
            Dictionary<int, string> partsToWrite,
            Action<string, int, int> progress = null)
        {
            foreach (var part in partsToWrite)
            {
                uint partAddress = (uint)part.Key;
                string filePath = part.Value;

                byte[] data = File.ReadAllBytes(filePath);
                string fileName = Path.GetFileName(filePath);

                WriteFlashCompressed(partAddress, data, (current, total) =>
                {
                    progress?.Invoke(fileName, current, total);
                });
            }
        }

        private void SendFlashDeflBegin(int uncompressedSize, int numBlocks, int blockSize, uint offset)
        {
            byte[] data = new byte[16];
            Esp32CommandPacket.WriteUInt32LE(data, 0, (uint)uncompressedSize);
            Esp32CommandPacket.WriteUInt32LE(data, 4, (uint)numBlocks);
            Esp32CommandPacket.WriteUInt32LE(data, 8, (uint)blockSize);
            Esp32CommandPacket.WriteUInt32LE(data, 12, offset);

            var response = _client.SendCommand(
                Esp32Command.FlashDeflBegin,
                data,
                timeoutMs: FlashBeginTimeoutMs);

            response.ThrowIfError();
        }

        private void SendFlashDeflData(byte[] compressedBlock, int sequenceNumber)
        {
            byte[] payload = new byte[16 + compressedBlock.Length];
            Esp32CommandPacket.WriteUInt32LE(payload, 0, (uint)compressedBlock.Length);
            Esp32CommandPacket.WriteUInt32LE(payload, 4, (uint)sequenceNumber);
            Esp32CommandPacket.WriteUInt32LE(payload, 8, 0);
            Esp32CommandPacket.WriteUInt32LE(payload, 12, 0);
            Buffer.BlockCopy(compressedBlock, 0, payload, 16, compressedBlock.Length);

            uint checksum = Esp32CommandPacket.CalculateChecksum(compressedBlock);

            var response = _client.SendCommand(
                Esp32Command.FlashDeflData,
                payload,
                checksum,
                FlashDataTimeoutMs);

            response.ThrowIfError();
        }

        private void SendFlashDeflEnd(bool reboot)
        {
            byte[] data = new byte[4];
            Esp32CommandPacket.WriteUInt32LE(data, 0, reboot ? 0u : 1u);

            var response = _client.SendCommand(Esp32Command.FlashDeflEnd, data);
            response.ThrowIfError();
        }

        /// <summary>
        /// Compress data using zlib format (2-byte header + deflate stream + 4-byte Adler-32).
        /// Compatible with both .NET Framework 4.7.2 and .NET 8.0.
        /// </summary>
        internal static byte[] CompressZlib(byte[] data)
        {
            using (var output = new MemoryStream())
            {
                // Zlib header: CMF=0x78 (deflate, 32K window), FLG=0x9C (default compression, check bits)
                output.WriteByte(0x78);
                output.WriteByte(0x9C);

                // Deflate-compressed data
                using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
                {
                    deflate.Write(data, 0, data.Length);
                }

                // Adler-32 checksum (big-endian)
                uint adler = ComputeAdler32(data);
                output.WriteByte((byte)((adler >> 24) & 0xFF));
                output.WriteByte((byte)((adler >> 16) & 0xFF));
                output.WriteByte((byte)((adler >> 8) & 0xFF));
                output.WriteByte((byte)(adler & 0xFF));

                return output.ToArray();
            }
        }

        /// <summary>
        /// Compute Adler-32 checksum for zlib trailer.
        /// </summary>
        internal static uint ComputeAdler32(byte[] data)
        {
            const uint MOD_ADLER = 65521;
            uint a = 1, b = 0;

            for (int i = 0; i < data.Length; i++)
            {
                a = (a + data[i]) % MOD_ADLER;
                b = (b + a) % MOD_ADLER;
            }

            return (b << 16) | a;
        }

        #endregion

        #region MD5 Verification (Stub Only)

        /// <summary>
        /// Calculate the MD5 hash of a flash region (requires stub).
        /// </summary>
        /// <param name="address">Flash start address.</param>
        /// <param name="length">Number of bytes to hash.</param>
        /// <returns>16-byte MD5 hash.</returns>
        /// <exception cref="InvalidOperationException">Stub is not running.</exception>
        internal byte[] FlashMd5(uint address, int length)
        {
            if (!_client.IsStubRunning)
            {
                throw new InvalidOperationException(
                    "MD5 verification requires the stub loader to be running.");
            }

            // SPI_FLASH_MD5: [address:4][length:4][0:4][0:4]
            byte[] cmdData = new byte[16];
            Esp32CommandPacket.WriteUInt32LE(cmdData, 0, address);
            Esp32CommandPacket.WriteUInt32LE(cmdData, 4, (uint)length);
            Esp32CommandPacket.WriteUInt32LE(cmdData, 8, 0);
            Esp32CommandPacket.WriteUInt32LE(cmdData, 12, 0);

            // MD5 calculation can be slow for large regions
            int timeoutMs = Math.Max(FlashReadTimeoutMs, (length / EraseBlockSize) * 200);

            var response = _client.SendCommand(
                Esp32Command.SpiFlashMd5,
                cmdData,
                timeoutMs: timeoutMs);

            response.ThrowIfError();

            // The stub returns the MD5 as 32 hex characters in the response data
            // or as 16 raw bytes depending on the stub version
            byte[] responseData = response.Data;

            if (responseData != null && responseData.Length >= 16)
            {
                // If response is 32 bytes, it's hex-encoded; if 16 bytes, it's raw
                if (responseData.Length >= 32)
                {
                    // Hex-encoded MD5 string
                    byte[] md5 = new byte[16];

                    for (int i = 0; i < 16; i++)
                    {
                        md5[i] = (byte)((HexVal(responseData[i * 2]) << 4) | HexVal(responseData[i * 2 + 1]));
                    }

                    return md5;
                }

                // Raw 16-byte MD5
                byte[] rawMd5 = new byte[16];
                Buffer.BlockCopy(responseData, 0, rawMd5, 0, 16);
                return rawMd5;
            }

            throw new EspToolExecutionException("Invalid MD5 response from stub.");
        }

        private static int HexVal(byte b)
        {
            if (b >= (byte)'0' && b <= (byte)'9')
            {
                return b - (byte)'0';
            }

            if (b >= (byte)'a' && b <= (byte)'f')
            {
                return b - (byte)'a' + 10;
            }

            if (b >= (byte)'A' && b <= (byte)'F')
            {
                return b - (byte)'A' + 10;
            }

            throw new FormatException($"Invalid hex character 0x{b:X2} in MD5 response.");
        }

        #endregion

        #region Flash Erase

        /// <summary>
        /// Erase the entire flash memory.
        /// This is a long-running operation (typically 5-30 seconds depending on flash size).
        /// </summary>
        internal void EraseFlash()
        {
            var response = _client.SendCommand(
                Esp32Command.EraseFlash,
                timeoutMs: MassEraseTimeoutMs);

            response.ThrowIfError();
        }

        /// <summary>
        /// Erase a specific region of flash.
        /// Both address and length must be aligned to the erase block size (4 KB).
        /// </summary>
        /// <param name="startAddress">Start address (must be 4KB aligned).</param>
        /// <param name="length">Length in bytes (must be 4KB aligned).</param>
        /// <exception cref="ArgumentException">Address or length not 4KB aligned.</exception>
        internal void EraseRegion(uint startAddress, uint length)
        {
            if ((startAddress % EraseBlockSize) != 0)
            {
                throw new ArgumentException(
                    $"Start address 0x{startAddress:X8} must be aligned to 4KB (0x{EraseBlockSize:X}).",
                    nameof(startAddress));
            }

            if ((length % EraseBlockSize) != 0)
            {
                throw new ArgumentException(
                    $"Length 0x{length:X8} must be aligned to 4KB (0x{EraseBlockSize:X}).",
                    nameof(length));
            }

            byte[] data = new byte[8];
            Esp32CommandPacket.WriteUInt32LE(data, 0, startAddress);
            Esp32CommandPacket.WriteUInt32LE(data, 4, length);

            // Erase can take significant time for large regions
            int timeoutMs = (int)Math.Max(
                Esp32BootloaderClient.EraseTimeoutMs,
                (length / EraseBlockSize) * 500);

            var response = _client.SendCommand(
                Esp32Command.EraseRegion,
                data,
                timeoutMs: timeoutMs);

            response.ThrowIfError();
        }

        #endregion

        #region Flash Parameter Configuration

        /// <summary>ESP image magic byte that identifies a valid bootloader image.</summary>
        private const byte EspImageMagic = 0xE9;

        /// <summary>
        /// Flash mode encoding for the bootloader image header byte 2.
        /// Maps mode name → byte value (matching esptool.py FLASH_MODES).
        /// </summary>
        private static readonly Dictionary<string, byte> FlashModeEncoding = new(StringComparer.OrdinalIgnoreCase)
        {
            ["qio"] = 0,
            ["qout"] = 1,
            ["dio"] = 2,
            ["dout"] = 3,
        };

        /// <summary>
        /// Flash frequency encoding for bootloader image header (low nibble of byte 3).
        /// Maps MHz value → nibble value (matching esptool.py FLASH_FREQUENCY).
        /// </summary>
        private static readonly Dictionary<int, byte> FlashFreqEncoding = new()
        {
            [80] = 0x0F,
            [40] = 0x00,
            [26] = 0x01,
            [20] = 0x02,
        };

        /// <summary>
        /// Flash size encoding for bootloader image header (high nibble of byte 3).
        /// Maps flash size in bytes → nibble value (matching esptool.py FLASH_SIZES).
        /// </summary>
        private static readonly Dictionary<int, byte> FlashSizeEncoding = new()
        {
            [1 * 1024 * 1024] = 0x00,
            [2 * 1024 * 1024] = 0x10,
            [4 * 1024 * 1024] = 0x20,
            [8 * 1024 * 1024] = 0x30,
            [16 * 1024 * 1024] = 0x40,
            [32 * 1024 * 1024] = 0x50,
            [64 * 1024 * 1024] = 0x60,
            [128 * 1024 * 1024] = 0x70,
        };

        /// <summary>
        /// Send SPI_SET_PARAMS command (0x0B) to configure the flash chip parameters.
        /// This must be called after SPI_ATTACH and before flash write operations.
        /// Tells the bootloader/stub the size and geometry of the flash chip.
        /// Format: [fl_id:4][total_size:4][block_size:4][sector_size:4][page_size:4][status_mask:4]
        /// </summary>
        /// <param name="flashSizeBytes">Total flash size in bytes.</param>
        internal void SendSpiSetParams(int flashSizeBytes)
        {
            byte[] data = new byte[24];
            Esp32CommandPacket.WriteUInt32LE(data, 0, 0);                            // fl_id = 0
            Esp32CommandPacket.WriteUInt32LE(data, 4, (uint)flashSizeBytes);         // total_size
            Esp32CommandPacket.WriteUInt32LE(data, 8, 64 * 1024);                    // block_size = 64KB
            Esp32CommandPacket.WriteUInt32LE(data, 12, 4 * 1024);                    // sector_size = 4KB
            Esp32CommandPacket.WriteUInt32LE(data, 16, 256);                         // page_size = 256
            Esp32CommandPacket.WriteUInt32LE(data, 20, 0xFFFF);                      // status_mask = 0xFFFF

            var response = _client.SendCommand(Esp32Command.SpiSetParams, data);
            response.ThrowIfError();
        }

        /// <summary>
        /// Patch the bootloader image header with flash mode, size, and frequency parameters.
        /// Only modifies the image if the address matches the bootloader offset and the image starts
        /// with the ESP image magic byte (0xE9).
        /// After patching, recalculates the SHA-256 digest if one is appended (byte 23 == 1).
        /// This matches esptool.py's _update_image_flash_params() behavior.
        /// </summary>
        /// <param name="address">Flash address where this image will be written.</param>
        /// <param name="imageData">The raw image bytes.</param>
        /// <param name="flashMode">Flash mode name (e.g. "dio", "qio").</param>
        /// <param name="flashFreqMHz">Flash frequency in MHz (e.g. 40, 80).</param>
        /// <param name="flashSizeBytes">Detected flash size in bytes.</param>
        /// <returns>The (possibly modified) image data.</returns>
        internal byte[] PatchBootloaderImageHeader(
            uint address,
            byte[] imageData,
            string flashMode,
            int flashFreqMHz,
            int flashSizeBytes)
        {
            // Only patch the bootloader image (at the bootloader flash offset)
            if (address != (uint)_config.BootloaderAddress)
            {
                return imageData;
            }

            // Image must be at least 24 bytes and start with the magic byte
            if (imageData.Length < 24 || imageData[0] != EspImageMagic)
            {
                return imageData;
            }

            // Current header values
            byte imgFlashMode = imageData[2];
            byte imgFlashSizeFreq = imageData[3];

            // Parse flash mode
            if (FlashModeEncoding.TryGetValue(flashMode, out byte modeVal))
            {
                imgFlashMode = modeVal;
            }

            // Parse flash frequency (low nibble of byte 3)
            byte imgFlashFreq = (byte)(imgFlashSizeFreq & 0x0F);
            if (FlashFreqEncoding.TryGetValue(flashFreqMHz, out byte freqVal))
            {
                imgFlashFreq = freqVal;
            }

            // Parse flash size (high nibble of byte 3)
            byte imgFlashSize = (byte)(imgFlashSizeFreq & 0xF0);
            if (FlashSizeEncoding.TryGetValue(flashSizeBytes, out byte sizeVal))
            {
                imgFlashSize = sizeVal;
            }

            byte newFlashParams = (byte)(imgFlashSize | imgFlashFreq);

            // Check if anything actually changed
            if (imgFlashMode == imageData[2] && newFlashParams == imageData[3])
            {
                return imageData;
            }

            // Make a copy to avoid modifying the caller's array
            byte[] patched = new byte[imageData.Length];
            Buffer.BlockCopy(imageData, 0, patched, 0, imageData.Length);

            patched[2] = imgFlashMode;
            patched[3] = newFlashParams;

            // Check if SHA-256 digest is appended (byte 23 = extended header byte 15)
            // The extended header starts at byte 8; byte at offset 8+15=23 indicates SHA append
            if (patched[23] == 1)
            {
                patched = RecalculateImageSha256(patched);
            }

            return patched;
        }

        /// <summary>
        /// Recalculate the SHA-256 digest in a bootloader image.
        /// The image format is: [header+segments data][32-byte SHA-256 digest][optional trailing data].
        /// The SHA-256 covers everything before the digest.
        /// </summary>
        private static byte[] RecalculateImageSha256(byte[] image)
        {
            // Find the data length by parsing the image structure.
            // ESP image format: [1 magic][1 segments][1 flash_mode][1 flash_size_freq]
            //                   [4 entrypoint][16 extended_header][segments...]
            // Each segment: [4 load_addr][4 data_len][data_len bytes]
            // After all segments: [1 checksum byte, aligned to 16 bytes]
            // Then: [32 bytes SHA-256] if append_digest==1
            int segmentCount = image[1];
            int offset = 24; // 8 byte header + 16 byte extended header

            for (int i = 0; i < segmentCount; i++)
            {
                if (offset + 8 > image.Length)
                {
                    return image; // Malformed image, don't modify
                }

                uint segLen = BitConverter.ToUInt32(image, offset + 4);
                offset += 8 + (int)segLen;
            }

            // After segments, there's a checksum byte (padded to 16-byte alignment)
            int alignedEnd = ((offset + 1 + 15) / 16) * 16;
            int dataLength = alignedEnd;

            // Verify we have room for the SHA-256 digest (32 bytes)
            if (dataLength + 32 > image.Length)
            {
                return image; // Not enough room for SHA, don't modify
            }

            // Calculate SHA-256 over the image data (everything before the digest)
            byte[] sha256;
            using (var hasher = SHA256.Create())
            {
                sha256 = hasher.ComputeHash(image, 0, dataLength);
            }

            // Write the new digest
            Buffer.BlockCopy(sha256, 0, image, dataLength, 32);

            return image;
        }

        #endregion

        #region Flash Read

        /// <summary>
        /// Read flash contents from the specified address.
        /// Note: READ_FLASH is only available in the stub loader, not the ROM bootloader.
        /// For ROM-only mode, this will throw if the stub is not running.
        /// </summary>
        /// <param name="address">Flash start address to read from.</param>
        /// <param name="length">Number of bytes to read.</param>
        /// <param name="progress">Optional progress callback (bytesRead, totalBytes).</param>
        /// <returns>The flash contents as a byte array.</returns>
        internal byte[] ReadFlash(
            uint address,
            int length,
            Action<int, int> progress = null)
        {
            // READ_FLASH command data format: [address:4][length:4][block_size:4][max_in_flight:4]
            // max_in_flight=64 matches esptool's FLASH_READ_MAX_INFLIGHT — the stub
            // can send up to 64 blocks before needing an ACK for flow control.
            byte[] cmdData = new byte[16];
            Esp32CommandPacket.WriteUInt32LE(cmdData, 0, address);
            Esp32CommandPacket.WriteUInt32LE(cmdData, 4, (uint)length);
            Esp32CommandPacket.WriteUInt32LE(cmdData, 8, (uint)ReadBlockSize);
            Esp32CommandPacket.WriteUInt32LE(cmdData, 12, FlashReadMaxInFlight);

            var response = _client.SendCommand(
                Esp32Command.ReadFlash,
                cmdData,
                timeoutMs: FlashReadTimeoutMs);

            response.ThrowIfError();

            // Read data blocks from the serial port.
            // Protocol (matching esptool's read_flash):
            //   1. Stub sends SLIP-framed data blocks (up to max_in_flight ahead)
            //   2. After each block, client sends a 4-byte SLIP ACK with total bytes received
            //   3. Stub uses ACK for flow control (won't exceed max_in_flight unacked blocks)
            //   4. After all data, stub sends a 16-byte MD5 digest frame
            byte[] result = new byte[length];
            int bytesRead = 0;

            while (bytesRead < length)
            {
                byte[] block = SlipFraming.ReadFrame(_client.Port, FlashReadTimeoutMs);

                // Copy received data into the result buffer (cap at remaining length)
                int copyLen = Math.Min(block.Length, length - bytesRead);
                Buffer.BlockCopy(block, 0, result, bytesRead, copyLen);
                bytesRead += copyLen;

                // ACK: SLIP-encode the total bytes received so far
                byte[] ack = new byte[4];
                Esp32CommandPacket.WriteUInt32LE(ack, 0, (uint)bytesRead);
                byte[] ackFrame = SlipFraming.Encode(ack);
                _client.Port.Write(ackFrame, 0, ackFrame.Length);
                _client.Port.BaseStream.Flush();

                progress?.Invoke(bytesRead, length);
            }

            // Read and discard the final MD5 digest frame
            try
            {
                SlipFraming.ReadFrame(_client.Port, Esp32BootloaderClient.DefaultTimeoutMs);
            }
            catch (TimeoutException)
            {
                // Some bootloaders don't send the MD5 frame — ignore
            }

            return result;
        }

        /// <summary>
        /// Read flash contents and save to a file.
        /// </summary>
        /// <param name="outputPath">Path to the output file.</param>
        /// <param name="address">Flash start address.</param>
        /// <param name="length">Number of bytes to read.</param>
        /// <param name="progress">Optional progress callback (bytesRead, totalBytes).</param>
        internal void ReadFlashToFile(
            string outputPath,
            uint address,
            int length,
            Action<int, int> progress = null)
        {
            byte[] data = ReadFlash(address, length, progress);
            File.WriteAllBytes(outputPath, data);
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Calculate the number of erase blocks needed to cover the given size.
        /// Used internally and exposed for testing.
        /// </summary>
        /// <param name="size">Size in bytes.</param>
        /// <param name="blockSize">Block size in bytes.</param>
        /// <returns>Number of blocks (rounded up).</returns>
        internal static int CalculateBlockCount(int size, int blockSize)
        {
            return (size + blockSize - 1) / blockSize;
        }

        /// <summary>
        /// Pad data to a multiple of block size with 0xFF bytes.
        /// Used internally and exposed for testing.
        /// </summary>
        /// <param name="data">Original data.</param>
        /// <param name="blockSize">Block size to pad to.</param>
        /// <returns>Padded data (or original if already aligned).</returns>
        internal static byte[] PadToBlockSize(byte[] data, int blockSize)
        {
            int remainder = data.Length % blockSize;

            if (remainder == 0)
            {
                return data;
            }

            int paddedLength = data.Length + (blockSize - remainder);
            byte[] padded = new byte[paddedLength];

            // Fill entire array with 0xFF first
            for (int i = 0; i < paddedLength; i++)
            {
                padded[i] = PadByte;
            }

            Buffer.BlockCopy(data, 0, padded, 0, data.Length);

            return padded;
        }

        #endregion
    }
}
