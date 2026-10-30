// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using nanoFramework.Tools.FirmwareFlasher;
using nanoFramework.Tools.FirmwareFlasher.Esp32Serial;

namespace nanoFirmwareFlasher.Tests
{
    [TestClass]
    public class Esp32BackupTests
    {
        private string _root = null!;
        private string _directory = null!;
        private byte[] _table = null!;
        private readonly List<(int Address, int Size)> _reads = new();
        private readonly Dictionary<int, byte[]> _written = new();
        private int _tableReads;
        private int _writeCalls;

        [TestInitialize]
        public void Initialize()
        {
            _root = Path.Combine(Path.GetTempPath(), "nanoff-backup-test-" + Guid.NewGuid().ToString("N"));
            _directory = Path.Combine(_root, "backup");
            Directory.CreateDirectory(_root);
            _table = CreateTable();
            _reads.Clear();
            _written.Clear();
            _tableReads = 0;
            _writeCalls = 0;
        }

        [TestCleanup]
        public void Cleanup() => Directory.Delete(_root, true);

        [TestMethod]
        public void Backup_DefaultAll_SavesFullPartitionContentsAndManifest()
        {
            Assert.AreEqual(ExitCodes.OK, Backup());
            Esp32BackupManifest manifest = LoadManifest();

            Assert.AreEqual(1, manifest.FormatVersion);
            Assert.AreEqual(3, manifest.Partitions.Count);
            Assert.AreEqual(3, _reads.Count);
            Assert.IsFalse(manifest.WholeFlash);
            CollectionAssert.AreEquivalent(new[] { "factory", "deploy", "config" },
                manifest.Partitions.Select(entry => entry.Label).ToArray());
            Assert.IsTrue(_reads.All(region => region.Address >= 0x10000), "all must not include the bootloader or partition table.");
            foreach (Esp32BackupEntry entry in manifest.Partitions)
            {
                Assert.AreEqual(entry.Size, new FileInfo(Path.Combine(_directory, entry.FileName)).Length);
                Assert.AreEqual(64, entry.Sha256.Length);
            }
        }

        [TestMethod]
        [DataRow("config", 0x40000)]
        [DataRow("clr", 0x10000)]
        [DataRow("factory", 0x10000)]
        [DataRow("deploy", 0x30000)]
        [DataRow("deployment", 0x30000)]
        public void Backup_Selector_UsesActualPartitionRange(string selector, int address)
        {
            Assert.AreEqual(ExitCodes.OK, Backup(new[] { selector }));
            Assert.AreEqual(1, _reads.Count);
            Assert.AreEqual(address, _reads[0].Address);
        }

        [TestMethod]
        public void Backup_WholeFlash_DoesNotRequireAValidPartitionTable()
        {
            _table = new byte[32];
            Assert.AreEqual(ExitCodes.OK, Backup(wholeFlash: true));

            Assert.AreEqual(0, _tableReads);
            Assert.AreEqual((0, 0x100000), _reads.Single());
            Assert.IsTrue(LoadManifest().WholeFlash);
        }

        [TestMethod]
        public void Backup_ExistingDirectoryContents_AreNotOverwritten()
        {
            Directory.CreateDirectory(_directory);
            string existing = Path.Combine(_directory, "keep.txt");
            File.WriteAllText(existing, "original");

            Assert.ThrowsExactly<EspToolExecutionException>(() => Backup());
            Assert.AreEqual("original", File.ReadAllText(existing));
            Assert.AreEqual(0, _reads.Count);
        }

        [TestMethod]
        public void Backup_UnknownSelector_DoesNotCreateBackup()
        {
            Assert.ThrowsExactly<EspToolExecutionException>(() => Backup(new[] { "unknown" }));
            Assert.IsFalse(Directory.Exists(_directory));
        }

        [TestMethod]
        [DataRow("clr", "factory")]
        [DataRow("all", "config")]
        [DataRow("config", "config")]
        public void Backup_AmbiguousSelection_Throws(string first, string second)
        {
            Assert.ThrowsExactly<EspToolExecutionException>(() => Backup(new[] { first, second }));
            Assert.AreEqual(0, _reads.Count);
        }

        [TestMethod]
        public void Backup_PartitionsAndWholeFlash_Throws()
        {
            Assert.ThrowsExactly<EspToolExecutionException>(() => Backup(new[] { "config" }, true));
            Assert.AreEqual(0, _reads.Count);
        }

        [TestMethod]
        public void Backup_PartialRead_DoesNotWriteCompletionManifest()
        {
            Assert.ThrowsExactly<EspToolExecutionException>(() => Esp32Backup.Backup(
                Device(), _directory, new[] { "config" }, false, VerbosityLevel.Quiet, () => _table,
                (file, _, _) => File.WriteAllBytes(file, new byte[1])));
            Assert.IsFalse(File.Exists(Path.Combine(_directory, Esp32Backup.ManifestFileName)));
        }

        [TestMethod]
        public void Restore_All_RestoresEachPartitionByteForByteInOneWrite()
        {
            Backup();
            Assert.AreEqual(ExitCodes.OK, Restore());

            Assert.AreEqual(1, _writeCalls);
            Assert.AreEqual(3, _written.Count);
            foreach (Esp32BackupEntry entry in LoadManifest().Partitions)
            {
                CollectionAssert.AreEqual(File.ReadAllBytes(Path.Combine(_directory, entry.FileName)), _written[entry.Address]);
            }
        }

        [TestMethod]
        public void Restore_SelectedPartition_LeavesOtherPartitionsOutOfWrite()
        {
            Backup();
            Assert.AreEqual(ExitCodes.OK, Restore(new[] { "config" }));
            CollectionAssert.AreEqual(new[] { 0x40000 }, _written.Keys.ToArray());
        }

        [TestMethod]
        public void Restore_WholeFlash_RequiresExplicitWholeFlashFlag()
        {
            Backup(wholeFlash: true);

            Assert.ThrowsExactly<EspToolExecutionException>(() => Restore());
            Assert.AreEqual(0, _writeCalls);
            Assert.AreEqual(ExitCodes.OK, Restore(wholeFlash: true));
            Assert.AreEqual(0, _tableReads);
            Assert.AreEqual(0x100000, _written[0].Length);
        }

        [TestMethod]
        public void Restore_WholeFlash_RejectsDifferentDevice()
        {
            Backup(wholeFlash: true);

            Assert.ThrowsExactly<EspToolExecutionException>(() => Restore(
                wholeFlash: true, device: Device(mac: "11:22:33:44:55:66")));
            Assert.AreEqual(0, _writeCalls);
        }

        [TestMethod]
        public void Restore_WholeFlash_RejectsDifferentFlashSize()
        {
            Backup(wholeFlash: true);
            Assert.ThrowsExactly<EspToolExecutionException>(() => Restore(
                wholeFlash: true, device: Device(flashSize: 0x200000)));
            Assert.AreEqual(0, _writeCalls);
        }

        [TestMethod]
        public void Restore_RejectsDifferentChip()
        {
            Backup();
            Assert.ThrowsExactly<EspToolExecutionException>(() => Restore(device: Device(chip: "ESP32-S3")));
            Assert.AreEqual(0, _writeCalls);
        }

        [TestMethod]
        [DataRow(0x50000, 0x2000, 0)]
        [DataRow(0x40000, 0x3000, 0)]
        [DataRow(0x40000, 0x2000, 1)]
        public void Restore_IncompatibleInstalledLayout_DoesNotWrite(int address, int size, int flags)
        {
            Backup();
            _table = CreateTable(address, size, (uint)flags);

            Assert.ThrowsExactly<EspToolExecutionException>(() => Restore());
            Assert.AreEqual(0, _writeCalls);
        }

        [TestMethod]
        public void Restore_EncryptedPartitionWithMatchingLayout_IsRefused()
        {
            _table = CreateTable(configFlags: 1);
            Backup(new[] { "config" });

            Assert.ThrowsExactly<EspToolExecutionException>(() => Restore());
            Assert.AreEqual(0, _writeCalls);
        }

        [TestMethod]
        public void Restore_LastSelectedFileCorrupt_NoPartitionsAreWritten()
        {
            Backup();
            string file = Path.Combine(_directory, LoadManifest().Partitions.Last().FileName);
            byte[] data = File.ReadAllBytes(file);
            data[0] ^= 1;
            File.WriteAllBytes(file, data);

            Assert.ThrowsExactly<EspToolExecutionException>(() => Restore());
            Assert.AreEqual(0, _writeCalls);
        }

        [TestMethod]
        public void Restore_TruncatedFile_DoesNotWrite()
        {
            Backup();
            File.WriteAllBytes(Path.Combine(_directory, LoadManifest().Partitions[0].FileName), new byte[1]);

            Assert.ThrowsExactly<EspToolExecutionException>(() => Restore());
            Assert.AreEqual(0, _writeCalls);
        }

        [TestMethod]
        public void Restore_MissingFile_DoesNotWrite()
        {
            Backup();
            File.Delete(Path.Combine(_directory, LoadManifest().Partitions.Last().FileName));

            Assert.ThrowsExactly<FileNotFoundException>(() => Restore());
            Assert.AreEqual(0, _writeCalls);
        }

        [TestMethod]
        public void Restore_WritesValidatedStagingCopies_NotMutableSourceFiles()
        {
            Backup(new[] { "config" });
            string sourceFile = Path.Combine(_directory, LoadManifest().Partitions.Single().FileName);
            byte[] expected = File.ReadAllBytes(sourceFile);
            string? stagedFile = null;
            ExitCodes result = Esp32Backup.Restore(Device(), _directory, null!, false, VerbosityLevel.Quiet, () => _table,
                images =>
                {
                    stagedFile = images.Single().Value;
                    File.WriteAllBytes(sourceFile, new byte[1]);
                    CollectionAssert.AreEqual(expected, File.ReadAllBytes(stagedFile));
                    return ExitCodes.E4003;
                });

            Assert.AreEqual(ExitCodes.E4003, result);
            Assert.IsNotNull(stagedFile);
            Assert.IsFalse(File.Exists(stagedFile), "Staging files should be cleaned even when writing fails.");
        }

        [TestMethod]
        [DataRow("../outside.bin")]
        [DataRow("..\\outside.bin")]
        [DataRow("C:\\outside.bin")]
        [DataRow("/outside.bin")]
        public void Restore_ManifestCannotReferenceFilesOutsideBackup(string filename)
        {
            Backup();
            Esp32BackupManifest manifest = LoadManifest();
            manifest.Partitions[0].FileName = filename;
            SaveManifest(manifest);

            Assert.ThrowsExactly<EspToolExecutionException>(() => Restore());
            Assert.AreEqual(0, _writeCalls);
        }

        [TestMethod]
        public void Restore_UnsupportedManifestVersion_DoesNotWrite()
        {
            Backup();
            Esp32BackupManifest manifest = LoadManifest();
            manifest.FormatVersion = 2;
            SaveManifest(manifest);

            Assert.ThrowsExactly<EspToolExecutionException>(() => Restore());
            Assert.AreEqual(0, _writeCalls);
        }

        [TestMethod]
        public void Restore_DuplicateManifestEntry_DoesNotWrite()
        {
            Backup();
            Esp32BackupManifest manifest = LoadManifest();
            manifest.Partitions.Add(manifest.Partitions[0]);
            SaveManifest(manifest);

            Assert.ThrowsExactly<EspToolExecutionException>(() => Restore());
            Assert.AreEqual(0, _writeCalls);
        }

        [TestMethod]
        public void ConfigRestore_UsesInstalledSourceAndIncomingDestination()
        {
            var plan = Esp32PartitionTable.GetConfigRestorePlan(
                CreateTable(), CreateTable(configAddress: 0x50000), 0x100000, false);

            Assert.IsTrue(plan.HasValue);
            Assert.AreEqual(0x40000, plan.Value.Source.Address);
            Assert.AreEqual(0x50000, plan.Value.Destination.Address);
            Assert.AreEqual(0x2000, plan.Value.Source.Size);
        }

        [TestMethod]
        [DataRow(0x3000, 0)]
        [DataRow(0x2000, 1)]
        public void ConfigRestore_IncompatibleIncomingGeometry_Throws(int size, int flags)
        {
            Assert.ThrowsExactly<EspToolExecutionException>(() => Esp32PartitionTable.GetConfigRestorePlan(
                CreateTable(), CreateTable(configSize: size, configFlags: (uint)flags), 0x100000, false));
        }

        [TestMethod]
        public void ConfigRestore_EncryptedConfiguration_IsRefusedEvenWithMatchingFlags()
        {
            Assert.ThrowsExactly<EspToolExecutionException>(() => Esp32PartitionTable.GetConfigRestorePlan(
                CreateTable(configFlags: 1), CreateTable(configFlags: 1), 0x100000, false));
        }

        [TestMethod]
        public void ConfigRestore_ChangedFilesystemSubtype_IsRefused()
        {
            byte[] incoming = CreateTable();
            incoming[67] = 0x81;

            Assert.ThrowsExactly<EspToolExecutionException>(() => Esp32PartitionTable.GetConfigRestorePlan(
                CreateTable(), incoming, 0x100000, false));
        }

        [TestMethod]
        public void ConfigRestore_MissingIncomingConfig_Throws()
        {
            byte[] incoming = CreateTable();
            incoming[64] = 0xFF;
            incoming[65] = 0xFF;

            Assert.ThrowsExactly<EspToolExecutionException>(() => Esp32PartitionTable.GetConfigRestorePlan(
                CreateTable(), incoming, 0x100000, false));
        }

        [TestMethod]
        public void ConfigRestore_UnprovisionedDevice_DoesNotRequireAnOldConfig()
        {
            byte[] erased = Enumerable.Repeat((byte)0xFF, Esp32PartitionTable.Size).ToArray();

            Assert.IsNull(Esp32PartitionTable.GetConfigRestorePlan(erased, CreateTable(), 0x100000, false));
            Assert.ThrowsExactly<EspToolExecutionException>(() =>
                Esp32PartitionTable.GetConfigRestorePlan(erased, CreateTable(), 0x100000, true));
        }

        [TestMethod]
        public void ConfigRestore_InvalidInstalledTable_IsNotSilentlyIgnored()
        {
            Assert.ThrowsExactly<EspToolExecutionException>(() =>
                Esp32PartitionTable.GetConfigRestorePlan(new byte[32], CreateTable(), 0x100000, false));
        }

        private ExitCodes Backup(string[]? selectors = null, bool wholeFlash = false)
            => Esp32Backup.Backup(Device(), _directory, selectors!, wholeFlash, VerbosityLevel.Quiet,
                () =>
                {
                    _tableReads++;
                    return _table;
                },
                (file, address, size) =>
                {
                    _reads.Add((address, size));
                    File.WriteAllBytes(file, Enumerable.Repeat((byte)(address >> 12), size).ToArray());
                });

        private ExitCodes Restore(string[]? selectors = null, bool wholeFlash = false, Esp32DeviceInfo? device = null)
            => Esp32Backup.Restore(device ?? Device(), _directory, selectors!, wholeFlash, VerbosityLevel.Quiet,
                () =>
                {
                    _tableReads++;
                    return _table;
                },
                images =>
                {
                    _writeCalls++;
                    foreach (var image in images)
                    {
                        _written.Add(image.Key, File.ReadAllBytes(image.Value));
                    }
                    return ExitCodes.OK;
                });

        private Esp32BackupManifest LoadManifest()
            => JsonSerializer.Deserialize<Esp32BackupManifest>(File.ReadAllText(Path.Combine(_directory, Esp32Backup.ManifestFileName)))!;

        private void SaveManifest(Esp32BackupManifest manifest)
            => File.WriteAllText(Path.Combine(_directory, Esp32Backup.ManifestFileName), JsonSerializer.Serialize(manifest));

        private static Esp32DeviceInfo Device(string chip = "ESP32", string mac = "AA:BB:CC:DD:EE:FF", int flashSize = 0x100000)
            => new Esp32DeviceInfo(chip, chip, "", "40MHz", mac, 0, 0, flashSize, PSRamAvailability.No, 0);

        private static byte[] CreateTable(int configAddress = 0x40000, int configSize = 0x2000, uint configFlags = 0)
        {
            byte[] table = Enumerable.Repeat((byte)0xFF, Esp32PartitionTable.Size).ToArray();
            WriteEntry(table, 0, "factory", 0, 0, 0x10000, 0x10000);
            WriteEntry(table, 32, "deploy", 1, 0x84, 0x30000, 0x1000);
            WriteEntry(table, 64, "config", 1, 0x83, configAddress, configSize, configFlags);
            return table;
        }

        private static void WriteEntry(byte[] table, int offset, string label, byte type, byte subtype, int address, int size, uint flags = 0)
        {
            Array.Clear(table, offset, 32);
            table[offset] = 0xAA;
            table[offset + 1] = 0x50;
            table[offset + 2] = type;
            table[offset + 3] = subtype;
            Esp32CommandPacket.WriteUInt32LE(table, offset + 4, (uint)address);
            Esp32CommandPacket.WriteUInt32LE(table, offset + 8, (uint)size);
            Encoding.ASCII.GetBytes(label).CopyTo(table, offset + 12);
            Esp32CommandPacket.WriteUInt32LE(table, offset + 28, flags);
        }
    }
}
