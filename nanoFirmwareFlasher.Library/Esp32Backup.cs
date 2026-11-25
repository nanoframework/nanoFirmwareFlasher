// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using nanoFramework.Tools.FirmwareFlasher.Esp32Serial;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Backs up ESP32 flash regions and restores validated, byte-for-byte snapshots.
    /// </summary>
    public static class Esp32Backup
    {
        internal const string ManifestFileName = "manifest.json";

        /// <summary>
        /// Back up selected partitions, or the whole flash, to a new or empty directory.
        /// </summary>
        /// <param name="tool">Connected ESP32 tool.</param>
        /// <param name="device">Details of the connected device.</param>
        /// <param name="directory">Destination directory.</param>
        /// <param name="partitions">Partition labels or clr/deployment aliases; empty means all partitions.</param>
        /// <param name="wholeFlash">Back up the entire flash, including bootloader and partition table.</param>
        /// <param name="verbosity">Output verbosity.</param>
        /// <returns>The operation result.</returns>
        public static ExitCodes Backup(
            EspTool tool, Esp32DeviceInfo device, string directory, IEnumerable<string> partitions,
            bool wholeFlash, VerbosityLevel verbosity)
            => Backup(device, directory, partitions, wholeFlash, verbosity, tool.ReadPartitionTable, tool.ReadFlashRegion);

        /// <summary>
        /// Restore selected partitions from a backup. All selected files are checked before any write.
        /// </summary>
        /// <param name="tool">Connected ESP32 tool.</param>
        /// <param name="device">Details of the connected device.</param>
        /// <param name="directory">Directory containing the backup manifest and binaries.</param>
        /// <param name="partitions">Partition labels or clr/deployment aliases; empty means all saved partitions.</param>
        /// <param name="wholeFlash">Explicitly authorize restoring a whole-flash backup to the original device.</param>
        /// <param name="verbosity">Output verbosity.</param>
        /// <returns>The operation result.</returns>
        public static ExitCodes Restore(
            EspTool tool, Esp32DeviceInfo device, string directory, IEnumerable<string> partitions,
            bool wholeFlash, VerbosityLevel verbosity)
        {
            ExitCodes result = Restore(device, directory, partitions, wholeFlash, verbosity,
                tool.ReadPartitionTable, images => tool.WriteFlash(images, preserveImage: true));
            if (result == ExitCodes.OK && tool.CouldntResetTarget && verbosity >= VerbosityLevel.Normal)
            {
                OutputWriter.WriteLine("Please reset the ESP32 manually to run the restored firmware.");
            }

            return result;
        }

        internal static ExitCodes Backup(
            Esp32DeviceInfo device, string directory, IEnumerable<string> selectors, bool wholeFlash,
            VerbosityLevel verbosity, Func<byte[]> readTable, Action<string, int, int> readRegion)
        {
            List<string> selection = ValidateSelection(selectors, wholeFlash);
            List<Esp32Partition> selected = wholeFlash
                ? new List<Esp32Partition> { new Esp32Partition("flash", 0xFF, 0xFF, 0, device.FlashSize, 0) }
                : Select(Esp32PartitionTable.Parse(readTable(), device.FlashSize), selection);
            if (selected.Count == 0 || device.FlashSize <= 0)
            {
                throw new EspToolExecutionException("No ESP32 flash regions are available to back up.");
            }

            string path = Path.GetFullPath(directory);
            if (File.Exists(path) || (Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any()))
            {
                throw new EspToolExecutionException("The backup destination must be a new or empty directory. Existing backups are not overwritten.");
            }

            Directory.CreateDirectory(path);
            Esp32BackupManifest manifest = new Esp32BackupManifest
            {
                FormatVersion = 1,
                ChipType = device.ChipType,
                MacAddress = device.MacAddress,
                FlashSize = device.FlashSize,
                WholeFlash = wholeFlash,
                CreatedUtc = DateTimeOffset.UtcNow
            };

            foreach (Esp32Partition partition in selected)
            {
                string filename = $"region-{partition.Address:X8}.bin";
                string filePath = Path.Combine(path, filename);
                if (verbosity >= VerbosityLevel.Normal)
                {
                    OutputWriter.WriteLine($"Backing up {partition.Label} (0x{partition.Address:X}, {partition.Size} bytes)...");
                }

                readRegion(filePath, partition.Address, partition.Size);
                if (new FileInfo(filePath).Length != partition.Size)
                {
                    throw new EspToolExecutionException($"Incomplete backup of ESP32 partition '{partition.Label}'. No completion manifest was written.");
                }

                manifest.Partitions.Add(new Esp32BackupEntry
                {
                    Label = partition.Label,
                    Type = partition.Type,
                    Subtype = partition.Subtype,
                    Address = partition.Address,
                    Size = partition.Size,
                    Flags = partition.Flags,
                    FileName = filename,
                    Sha256 = ComputeHash(filePath)
                });
            }

            byte[] json = JsonSerializer.SerializeToUtf8Bytes(manifest, new JsonSerializerOptions { WriteIndented = true });
            using (FileStream stream = new FileStream(Path.Combine(path, ManifestFileName), FileMode.CreateNew, FileAccess.Write))
            {
                stream.Write(json, 0, json.Length);
            }

            if (verbosity >= VerbosityLevel.Normal)
            {
                OutputWriter.WriteLine($"Backup completed: {path}");
            }

            return ExitCodes.OK;
        }

        internal static ExitCodes Restore(
            Esp32DeviceInfo device, string directory, IEnumerable<string> selectors, bool wholeFlash,
            VerbosityLevel verbosity, Func<byte[]> readTable, Func<Dictionary<int, string>, ExitCodes> writeFlash)
        {
            List<string> selection = ValidateSelection(selectors, wholeFlash);
            string path = Path.GetFullPath(directory);
            Esp32BackupManifest manifest;
            try
            {
                manifest = JsonSerializer.Deserialize<Esp32BackupManifest>(File.ReadAllText(Path.Combine(path, ManifestFileName)));
            }
            catch (JsonException ex)
            {
                throw new EspToolExecutionException("Invalid ESP32 backup manifest.", ex);
            }

            ValidateManifest(manifest, device, wholeFlash);
            IReadOnlyList<Esp32Partition> installed = wholeFlash
                ? Array.Empty<Esp32Partition>()
                : Esp32PartitionTable.Parse(readTable(), device.FlashSize);
            List<Esp32Partition> saved = manifest.Partitions.Select(entry => entry.ToPartition()).ToList();
            List<Esp32Partition> selected = wholeFlash ? saved : Select(saved, selection);
            Dictionary<int, string> images = new Dictionary<int, string>();
            string stagingDirectory = Path.Combine(Path.GetTempPath(), "nanoff-restore-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stagingDirectory);
            try
            {
                foreach (Esp32Partition source in selected)
                {
                    Esp32BackupEntry entry = manifest.Partitions.Single(item => item.Address == source.Address);
                    if (!wholeFlash)
                    {
                        Esp32Partition destination = installed.SingleOrDefault(item =>
                            string.Equals(item.Label, source.Label, StringComparison.OrdinalIgnoreCase));
                        if (destination == null || destination.Address != source.Address || destination.Size != source.Size
                            || destination.Type != source.Type || destination.Subtype != source.Subtype
                            || destination.Flags != source.Flags || (source.Flags & 1) != 0)
                        {
                            throw new EspToolExecutionException($"The installed ESP32 layout is incompatible with backup partition '{source.Label}', or the partition is encrypted.");
                        }
                    }

                    string sourceFile = Path.Combine(path, entry.FileName);
                    if (new FileInfo(sourceFile).Length != entry.Size)
                    {
                        throw new EspToolExecutionException($"Backup partition '{entry.Label}' failed size validation. Nothing was written.");
                    }

                    string filePath = Path.Combine(stagingDirectory, entry.FileName);
                    File.Copy(sourceFile, filePath);
                    if (new FileInfo(filePath).Length != entry.Size
                        || !string.Equals(ComputeHash(filePath), entry.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new EspToolExecutionException($"Backup partition '{entry.Label}' failed size or SHA-256 validation. Nothing was written.");
                    }

                    images.Add(source.Address, filePath);
                }

                if (verbosity >= VerbosityLevel.Normal)
                {
                    OutputWriter.WriteLine($"Restoring {images.Count} validated ESP32 flash region(s)...");
                }

                return writeFlash(images);
            }
            finally
            {
                Directory.Delete(stagingDirectory, true);
            }
        }

        private static List<string> ValidateSelection(IEnumerable<string> selectors, bool wholeFlash)
        {
            List<string> selection = selectors?.ToList() ?? new List<string>();
            if (wholeFlash && selection.Count > 0)
            {
                throw new EspToolExecutionException("wholeflash cannot be combined with partition selection.");
            }

            if (selection.Any(string.IsNullOrWhiteSpace)
                || (selection.Count > 1 && selection.Any(item => string.Equals(item, "all", StringComparison.OrdinalIgnoreCase))))
            {
                throw new EspToolExecutionException("Select partition labels, or all by itself.");
            }

            return selection;
        }

        private static List<Esp32Partition> Select(IReadOnlyList<Esp32Partition> partitions, List<string> selectors)
        {
            if (selectors.Count == 0 || string.Equals(selectors[0], "all", StringComparison.OrdinalIgnoreCase))
            {
                return partitions.ToList();
            }

            List<Esp32Partition> result = new List<Esp32Partition>();
            foreach (string selector in selectors)
            {
                Esp32Partition partition = Esp32PartitionTable.Find(partitions, selector);
                if (result.Contains(partition))
                {
                    throw new EspToolExecutionException($"ESP32 partition '{partition.Label}' was selected more than once.");
                }

                result.Add(partition);
            }

            return result;
        }

        private static void ValidateManifest(Esp32BackupManifest manifest, Esp32DeviceInfo device, bool wholeFlash)
        {
            if (manifest == null || manifest.FormatVersion != 1 || manifest.Partitions == null || manifest.Partitions.Count == 0
                || manifest.FlashSize <= 0 || !string.Equals(manifest.ChipType, device.ChipType, StringComparison.OrdinalIgnoreCase)
                || manifest.WholeFlash != wholeFlash)
            {
                throw new EspToolExecutionException("The backup format, chip type or wholeflash selection is incompatible with this restore.");
            }

            if (wholeFlash && (manifest.FlashSize != device.FlashSize
                || string.IsNullOrWhiteSpace(manifest.MacAddress)
                || !string.Equals(manifest.MacAddress, device.MacAddress, StringComparison.OrdinalIgnoreCase)
                || manifest.Partitions.Count != 1 || manifest.Partitions[0] == null
                || manifest.Partitions[0].Address != 0 || manifest.Partitions[0].Size != manifest.FlashSize))
            {
                throw new EspToolExecutionException("A whole-flash backup can only be restored to the original ESP32 device with the same flash size.");
            }

            List<Esp32BackupEntry> checkedEntries = new List<Esp32BackupEntry>();
            foreach (Esp32BackupEntry entry in manifest.Partitions)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.Label) || entry.Address < 0 || entry.Size <= 0
                    || entry.Address % 0x1000 != 0 || entry.Size % 0x1000 != 0
                    || (long)entry.Address + entry.Size > manifest.FlashSize
                    || (long)entry.Address + entry.Size > device.FlashSize
                    || (!wholeFlash && entry.Address < Esp32PartitionTable.Address + 0x1000)
                    || string.IsNullOrWhiteSpace(entry.FileName) || entry.FileName == "." || entry.FileName == ".."
                    || entry.FileName.IndexOfAny(new[] { '/', '\\', ':' }) >= 0
                    || Path.GetFileName(entry.FileName) != entry.FileName
                    || string.IsNullOrEmpty(entry.Sha256))
                {
                    throw new EspToolExecutionException("Invalid ESP32 backup partition metadata or filename.");
                }

                if (checkedEntries.Any(other =>
                    string.Equals(other.Label, entry.Label, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(other.FileName, entry.FileName, StringComparison.OrdinalIgnoreCase)
                    || (entry.Address < (long)other.Address + other.Size && other.Address < (long)entry.Address + entry.Size)))
                {
                    throw new EspToolExecutionException("Duplicate or overlapping ESP32 backup entries.");
                }

                checkedEntries.Add(entry);
            }
        }

        private static string ComputeHash(string filePath)
        {
            using (SHA256 sha256 = SHA256.Create())
            using (FileStream stream = File.OpenRead(filePath))
            {
                return BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", "");
            }
        }
    }

    internal sealed class Esp32BackupManifest
    {
        public Esp32BackupManifest() { }
        public int FormatVersion { get; set; }
        public string ChipType { get; set; }
        public string MacAddress { get; set; }
        public int FlashSize { get; set; }
        public bool WholeFlash { get; set; }
        public DateTimeOffset CreatedUtc { get; set; }
        public List<Esp32BackupEntry> Partitions { get; set; } = new List<Esp32BackupEntry>();
    }

    internal sealed class Esp32BackupEntry
    {
        public Esp32BackupEntry() { }
        public string Label { get; set; }
        public byte Type { get; set; }
        public byte Subtype { get; set; }
        public int Address { get; set; }
        public int Size { get; set; }
        public uint Flags { get; set; }
        public string FileName { get; set; }
        public string Sha256 { get; set; }

        internal Esp32Partition ToPartition() => new Esp32Partition(Label, Type, Subtype, Address, Size, Flags);
    }
}
