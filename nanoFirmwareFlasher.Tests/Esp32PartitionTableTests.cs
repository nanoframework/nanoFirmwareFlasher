// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using nanoFramework.Tools.FirmwareFlasher;
using nanoFramework.Tools.FirmwareFlasher.Esp32Serial;

namespace nanoFirmwareFlasher.Tests
{
    [TestClass]
    public class Esp32PartitionTableTests
    {
        [TestMethod]
        [DataRow(0x1B0000)]
        [DataRow(0x1E0000)]
        [DataRow(0x250000)]
        public void GetDeploymentAddress_UsesPartitionOffset(int address)
        {
            byte[] table = CreateTable(address);

            Assert.AreEqual(address, Esp32PartitionTable.GetDeploymentAddress(table, 0x1000));
        }

        [TestMethod]
        public void GetDeploymentAddress_FindsPartitionByTypeAndSubtype()
        {
            byte[] table = CreateTable(0x1E0000);
            Array.Clear(table, 32 + 12, 16);
            Encoding.ASCII.GetBytes("managed").CopyTo(table, 32 + 12);

            Assert.AreEqual(0x1E0000, Esp32PartitionTable.GetDeploymentAddress(table, 0x1000));
        }

        [TestMethod]
        public void GetDeploymentAddress_LegacyTableWithoutChecksum_IsSupported()
        {
            byte[] table = CreateTable(0x1B0000);
            Array.Resize(ref table, 64);

            Assert.AreEqual(0x1B0000, Esp32PartitionTable.GetDeploymentAddress(table, 0x1000));
        }

        [TestMethod]
        public void GetDeploymentAddress_ValidChecksum_IsSupported()
        {
            byte[] table = CreateTable(0x1E0000);
            AddChecksum(table);

            Assert.AreEqual(0x1E0000, Esp32PartitionTable.GetDeploymentAddress(table, 0x1000));
        }

        [TestMethod]
        public void GetDeploymentAddress_CorruptChecksum_Throws()
        {
            byte[] table = CreateTable(0x1E0000);
            AddChecksum(table);
            table[44] ^= 1;

            Assert.ThrowsExactly<EspToolExecutionException>(() =>
                Esp32PartitionTable.GetDeploymentAddress(table, 0x1000));
        }

        [TestMethod]
        [DataRow(0, 0x10000)]
        [DataRow(0x1E0001, 0x10000)]
        [DataRow(0x1E0000, 0)]
        [DataRow(0x1E0000, 0x10001)]
        [DataRow(0x7FFFF000, 0x10000)]
        public void GetDeploymentAddress_InvalidPartitionRange_Throws(int address, int size)
        {
            byte[] table = CreateTable(address, size);

            Assert.ThrowsExactly<EspToolExecutionException>(() =>
                Esp32PartitionTable.GetDeploymentAddress(table, 0x1000));
        }

        [TestMethod]
        [DataRow(0, 0x84)]
        [DataRow(1, 0x82)]
        public void GetDeploymentAddress_WrongTypeOrSubtype_Throws(int type, int subtype)
        {
            byte[] table = CreateTable(0x1E0000);
            table[34] = (byte)type;
            table[35] = (byte)subtype;

            Assert.ThrowsExactly<EspToolExecutionException>(() =>
                Esp32PartitionTable.GetDeploymentAddress(table, 0x1000));
        }

        [TestMethod]
        public void GetDeploymentAddress_ErasedTable_Throws()
        {
            byte[] table = CreateTable(0x1E0000);
            table[0] = 0xFF;
            table[1] = 0xFF;

            Assert.ThrowsExactly<EspToolExecutionException>(() =>
                Esp32PartitionTable.GetDeploymentAddress(table, 0x1000));
        }

        [TestMethod]
        public void GetDeploymentAddress_InvalidEntryAfterDeployment_Throws()
        {
            byte[] table = CreateTable(0x1E0000);
            table[64] = 0;

            Assert.ThrowsExactly<EspToolExecutionException>(() =>
                Esp32PartitionTable.GetDeploymentAddress(table, 0x1000));
        }

        [TestMethod]
        public void GetDeploymentAddress_DuplicateDeploymentPartitions_Throws()
        {
            byte[] table = CreateTable(0x1E0000);
            Buffer.BlockCopy(table, 32, table, 64, 32);

            Assert.ThrowsExactly<EspToolExecutionException>(() =>
                Esp32PartitionTable.GetDeploymentAddress(table, 0x1000));
        }

        [TestMethod]
        [DataRow(0)]
        [DataRow(31)]
        [DataRow(65)]
        [DataRow(0x1000)]
        public void GetDeploymentAddress_InvalidTableLength_Throws(int length)
        {
            Assert.ThrowsExactly<EspToolExecutionException>(() =>
                Esp32PartitionTable.GetDeploymentAddress(new byte[length], 0x1000));
        }

        [TestMethod]
        public void GetDeploymentAddress_ImageExactlyFits_IsAccepted()
        {
            Assert.AreEqual(0x1E0000,
                Esp32PartitionTable.GetDeploymentAddress(CreateTable(0x1E0000), 0x10000));
        }

        [TestMethod]
        public void GetDeploymentAddress_ImageOneByteTooLarge_Throws()
        {
            Assert.ThrowsExactly<EspToolExecutionException>(() =>
                Esp32PartitionTable.GetDeploymentAddress(CreateTable(0x1E0000), 0x10001));
        }

        [TestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("0x0")]
        public void ResolveDeploymentAddress_ReadsTableWhenNoOverride(string? explicitAddress)
        {
            int reads = 0;
            ExitCodes result = Esp32Operations.GetDeploymentAddress(
                explicitAddress!,
                0x1000,
                () =>
                {
                    reads++;
                    return CreateTable(0x250000);
                },
                out int address);

            Assert.AreEqual(ExitCodes.OK, result);
            Assert.AreEqual(0x250000, address);
            Assert.AreEqual(1, reads);
        }

        [TestMethod]
        [DataRow("0x1B0000")]
        [DataRow("0X1b0000")]
        public void ResolveDeploymentAddress_ExplicitOverride_DoesNotReadTable(string explicitAddress)
        {
            ExitCodes result = Esp32Operations.GetDeploymentAddress(
                explicitAddress,
                0x10001,
                () => throw new AssertFailedException("Explicit address must bypass partition table reads."),
                out int address);

            Assert.AreEqual(ExitCodes.OK, result);
            Assert.AreEqual(0x1B0000, address);
        }

        [TestMethod]
        [DataRow("0")]
        [DataRow("0x")]
        [DataRow("0xINVALID")]
        [DataRow("0xFFFFFFFF")]
        public void ResolveDeploymentAddress_InvalidOverride_ReturnsAddressError(string explicitAddress)
        {
            ExitCodes result = Esp32Operations.GetDeploymentAddress(
                explicitAddress,
                0x1000,
                () => throw new AssertFailedException("Invalid address must not read flash."),
                out _);

            Assert.AreEqual(ExitCodes.E9009, result);
        }

        [TestMethod]
        public void ResolveDeploymentAddress_ReadFailure_DoesNotFallback()
        {
            Assert.ThrowsExactly<ReadEsp32FlashException>(() =>
                Esp32Operations.GetDeploymentAddress(
                    null!,
                    0x1000,
                    () => throw new ReadEsp32FlashException("Read failed."),
                    out _));
        }

        private static byte[] CreateTable(int address, int size = 0x10000)
        {
            byte[] table = new byte[Esp32PartitionTable.Size];
            for (int i = 0; i < table.Length; i++)
            {
                table[i] = 0xFF;
            }

            WriteEntry(table, 0, 0, 0, 0x10000, 0x1A0000, "factory");
            WriteEntry(table, 32, 1, 0x84, address, size, "deploy");
            return table;
        }

        private static void WriteEntry(byte[] table, int position, byte type, byte subtype, int address, int size, string name)
        {
            Array.Clear(table, position, 32);
            table[position] = 0xAA;
            table[position + 1] = 0x50;
            table[position + 2] = type;
            table[position + 3] = subtype;
            Esp32CommandPacket.WriteUInt32LE(table, position + 4, (uint)address);
            Esp32CommandPacket.WriteUInt32LE(table, position + 8, (uint)size);
            Encoding.ASCII.GetBytes(name).CopyTo(table, position + 12);
        }

        private static void AddChecksum(byte[] table)
        {
            table[64] = 0xEB;
            table[65] = 0xEB;
            using (MD5 md5 = MD5.Create())
            {
                md5.ComputeHash(table, 0, 64).CopyTo(table, 80);
            }
        }
    }
}
