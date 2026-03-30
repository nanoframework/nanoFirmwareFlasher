// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using nanoFramework.Tools.FirmwareFlasher;

namespace nanoFirmwareFlasher.Tests
{
    [TestClass]
    public class Stm32UartBootloaderTests
    {
        [TestMethod]
        public void Bootloader_DefaultProperties()
        {
            // Verify default properties without connecting to hardware
            var bootloader = new Stm32UartBootloader();

            Assert.AreEqual(VerbosityLevel.Normal, bootloader.Verbosity);
            Assert.AreEqual((byte)0, bootloader.BootloaderVersion);
            Assert.IsFalse(bootloader.UsesExtendedErase);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void ReadMemory_ZeroLength_Throws()
        {
            var bootloader = new Stm32UartBootloader();

            // Should throw without needing a serial connection
            bootloader.ReadMemory(0x08000000, 0);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void ReadMemory_ExceedsMaxLength_Throws()
        {
            var bootloader = new Stm32UartBootloader();

            // Exceeds max 256 bytes
            bootloader.ReadMemory(0x08000000, 257);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void WriteMemory_EmptyData_Throws()
        {
            var bootloader = new Stm32UartBootloader();

            bootloader.WriteMemory(0x08000000, Array.Empty<byte>());
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void WriteMemory_ExceedsMaxBlockSize_Throws()
        {
            var bootloader = new Stm32UartBootloader();

            bootloader.WriteMemory(0x08000000, new byte[257]);
        }

        [TestMethod]
        public void Dispose_CanBeCalledMultipleTimes()
        {
            var bootloader = new Stm32UartBootloader();

            // Should not throw
            bootloader.Dispose();
            bootloader.Dispose();
        }

        [TestMethod]
        public void Stm32UartDevice_ExitCodes_Exist()
        {
            // Verify our new exit codes are part of the enum
            Assert.AreEqual(5020, (int)ExitCodes.E5020);
            Assert.AreEqual(5021, (int)ExitCodes.E5021);
        }

        [TestMethod]
        public void Interface_HasUartValue()
        {
            // Verify the Uart enum value exists
            Assert.AreEqual(3, (int)Interface.Uart);
        }

        [TestMethod]
        public void Stm32UartBootloaderException_Constructors()
        {
            // Test all constructors following the codebase pattern
            var ex1 = new Stm32UartBootloaderException();
            Assert.IsNotNull(ex1);

            var ex2 = new Stm32UartBootloaderException("test message");
            Assert.AreEqual("test message", ex2.Message);

            var inner = new InvalidOperationException("inner");
            var ex3 = new Stm32UartBootloaderException("outer", inner);
            Assert.AreEqual("outer", ex3.Message);
            Assert.AreSame(inner, ex3.InnerException);
        }
    }
}
