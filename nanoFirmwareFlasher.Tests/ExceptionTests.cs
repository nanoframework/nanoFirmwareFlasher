// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using nanoFramework.Tools.FirmwareFlasher;

namespace nanoFirmwareFlasher.Tests
{
    /// <summary>
    /// Tests for all exception classes that don't already have dedicated tests.
    /// Verifies constructor overloads, message propagation, and inner exception chaining.
    /// </summary>
    [TestClass]
    public class ExceptionTests
    {
        #region CantConnectToDfuDeviceException

        [TestMethod]
        public void CantConnectToDfuDeviceException_DefaultConstructor()
        {
            var ex = new CantConnectToDfuDeviceException();
            Assert.IsNotNull(ex);
            Assert.IsInstanceOfType(ex, typeof(Exception));
        }

        [TestMethod]
        public void CantConnectToDfuDeviceException_MessageConstructor()
        {
            var ex = new CantConnectToDfuDeviceException("DFU not found");
            Assert.AreEqual("DFU not found", ex.Message);
        }

        [TestMethod]
        public void CantConnectToDfuDeviceException_InnerExceptionConstructor()
        {
            var inner = new TimeoutException("timeout");
            var ex = new CantConnectToDfuDeviceException("DFU error", inner);
            Assert.AreEqual("DFU error", ex.Message);
            Assert.AreSame(inner, ex.InnerException);
        }

        #endregion

        #region CantConnectToJLinkDeviceException

        [TestMethod]
        public void CantConnectToJLinkDeviceException_DefaultConstructor()
        {
            var ex = new CantConnectToJLinkDeviceException();
            Assert.IsNotNull(ex);
        }

        [TestMethod]
        public void CantConnectToJLinkDeviceException_MessageConstructor()
        {
            var ex = new CantConnectToJLinkDeviceException("J-Link not responding");
            Assert.AreEqual("J-Link not responding", ex.Message);
        }

        [TestMethod]
        public void CantConnectToJLinkDeviceException_InnerExceptionConstructor()
        {
            var inner = new InvalidOperationException("USB error");
            var ex = new CantConnectToJLinkDeviceException("J-Link error", inner);
            Assert.AreEqual("J-Link error", ex.Message);
            Assert.AreSame(inner, ex.InnerException);
        }

        #endregion

        #region CantConnectToJtagDeviceException

        [TestMethod]
        public void CantConnectToJtagDeviceException_DefaultConstructor()
        {
            var ex = new CantConnectToJtagDeviceException();
            Assert.IsNotNull(ex);
        }

        [TestMethod]
        public void CantConnectToJtagDeviceException_MessageConstructor()
        {
            var ex = new CantConnectToJtagDeviceException("JTAG timeout");
            Assert.AreEqual("JTAG timeout", ex.Message);
        }

        [TestMethod]
        public void CantConnectToJtagDeviceException_InnerExceptionConstructor()
        {
            var inner = new TimeoutException("SWD error");
            var ex = new CantConnectToJtagDeviceException("JTAG failed", inner);
            Assert.AreEqual("JTAG failed", ex.Message);
            Assert.AreSame(inner, ex.InnerException);
        }

        #endregion

        #region CantConnectToNanoDeviceException

        [TestMethod]
        public void CantConnectToNanoDeviceException_DefaultConstructor()
        {
            var ex = new CantConnectToNanoDeviceException();
            Assert.IsNotNull(ex);
        }

        [TestMethod]
        public void CantConnectToNanoDeviceException_MessageConstructor()
        {
            var ex = new CantConnectToNanoDeviceException("device offline");
            Assert.AreEqual("device offline", ex.Message);
        }

        [TestMethod]
        public void CantConnectToNanoDeviceException_InnerExceptionConstructor()
        {
            var inner = new Exception("serial port busy");
            var ex = new CantConnectToNanoDeviceException("nano error", inner);
            Assert.AreEqual("nano error", ex.Message);
            Assert.AreSame(inner, ex.InnerException);
        }

        #endregion

        #region DfuFileDoesNotExistException

        [TestMethod]
        public void DfuFileDoesNotExistException_DefaultConstructor()
        {
            var ex = new DfuFileDoesNotExistException();
            Assert.IsNotNull(ex);
        }

        [TestMethod]
        public void DfuFileDoesNotExistException_MessageConstructor()
        {
            var ex = new DfuFileDoesNotExistException("file.dfu missing");
            Assert.AreEqual("file.dfu missing", ex.Message);
        }

        [TestMethod]
        public void DfuFileDoesNotExistException_InnerExceptionConstructor()
        {
            var inner = new System.IO.FileNotFoundException("not found");
            var ex = new DfuFileDoesNotExistException("DFU file error", inner);
            Assert.AreEqual("DFU file error", ex.Message);
            Assert.AreSame(inner, ex.InnerException);
        }

        #endregion

        #region EraseEsp32FlashException

        [TestMethod]
        public void EraseEsp32FlashException_MessageConstructor()
        {
            var ex = new EraseEsp32FlashException("erase failed");
            Assert.AreEqual("erase failed", ex.Message);
        }

        [TestMethod]
        public void EraseEsp32FlashException_InnerExceptionConstructor()
        {
            var inner = new TimeoutException("timeout");
            var ex = new EraseEsp32FlashException("erase error", inner);
            Assert.AreEqual("erase error", ex.Message);
            Assert.AreSame(inner, ex.InnerException);
        }

        #endregion

        #region EspToolExecutionException

        [TestMethod]
        public void EspToolExecutionException_DefaultConstructor()
        {
            var ex = new EspToolExecutionException();
            Assert.IsNotNull(ex);
        }

        [TestMethod]
        public void EspToolExecutionException_MessageConstructor()
        {
            var ex = new EspToolExecutionException("esptool crashed");
            Assert.AreEqual("esptool crashed", ex.Message);
        }

        [TestMethod]
        public void EspToolExecutionException_InnerExceptionConstructor()
        {
            var inner = new InvalidOperationException("process error");
            var ex = new EspToolExecutionException("esptool error", inner);
            Assert.AreEqual("esptool error", ex.Message);
            Assert.AreSame(inner, ex.InnerException);
        }

        #endregion

        #region NanoDeviceOperationFailedException

        [TestMethod]
        public void NanoDeviceOperationFailedException_DefaultConstructor()
        {
            var ex = new NanoDeviceOperationFailedException();
            Assert.IsNotNull(ex);
        }

        [TestMethod]
        public void NanoDeviceOperationFailedException_MessageConstructor()
        {
            var ex = new NanoDeviceOperationFailedException("deploy failed");
            Assert.AreEqual("deploy failed", ex.Message);
        }

        [TestMethod]
        public void NanoDeviceOperationFailedException_InnerExceptionConstructor()
        {
            var inner = new Exception("inner");
            var ex = new NanoDeviceOperationFailedException("outer", inner);
            Assert.AreEqual("outer", ex.Message);
            Assert.AreSame(inner, ex.InnerException);
        }

        #endregion

        #region NoOperationPerformedException

        [TestMethod]
        public void NoOperationPerformedException_DefaultConstructor()
        {
            var ex = new NoOperationPerformedException();
            Assert.IsNotNull(ex);
        }

        [TestMethod]
        public void NoOperationPerformedException_MessageConstructor()
        {
            var ex = new NoOperationPerformedException("nothing happened");
            Assert.AreEqual("nothing happened", ex.Message);
        }

        [TestMethod]
        public void NoOperationPerformedException_InnerExceptionConstructor()
        {
            var inner = new ArgumentException("bad args");
            var ex = new NoOperationPerformedException("no-op", inner);
            Assert.AreEqual("no-op", ex.Message);
            Assert.AreSame(inner, ex.InnerException);
        }

        #endregion

        #region ReadEsp32FlashException

        [TestMethod]
        public void ReadEsp32FlashException_MessageConstructor()
        {
            var ex = new ReadEsp32FlashException("read failed");
            Assert.AreEqual("read failed", ex.Message);
        }

        [TestMethod]
        public void ReadEsp32FlashException_InnerExceptionConstructor()
        {
            var inner = new TimeoutException("read timeout");
            var ex = new ReadEsp32FlashException("read error", inner);
            Assert.AreEqual("read error", ex.Message);
            Assert.AreSame(inner, ex.InnerException);
        }

        #endregion

        #region SilinkExecutionException

        [TestMethod]
        public void SilinkExecutionException_DefaultConstructor()
        {
            var ex = new SilinkExecutionException();
            Assert.IsNotNull(ex);
        }

        [TestMethod]
        public void SilinkExecutionException_MessageConstructor()
        {
            var ex = new SilinkExecutionException("silink error");
            Assert.AreEqual("silink error", ex.Message);
        }

        [TestMethod]
        public void SilinkExecutionException_InnerExceptionConstructor()
        {
            var inner = new Exception("process died");
            var ex = new SilinkExecutionException("silink failed", inner);
            Assert.AreEqual("silink failed", ex.Message);
            Assert.AreSame(inner, ex.InnerException);
        }

        #endregion

        #region StLinkCliExecutionException

        [TestMethod]
        public void StLinkCliExecutionException_DefaultConstructor()
        {
            var ex = new StLinkCliExecutionException();
            Assert.IsNotNull(ex);
        }

        [TestMethod]
        public void StLinkCliExecutionException_MessageConstructor()
        {
            var ex = new StLinkCliExecutionException("STM32 CLI error");
            Assert.AreEqual("STM32 CLI error", ex.Message);
        }

        [TestMethod]
        public void StLinkCliExecutionException_InnerExceptionConstructor()
        {
            var inner = new InvalidOperationException("exe not found");
            var ex = new StLinkCliExecutionException("CLI error", inner);
            Assert.AreEqual("CLI error", ex.Message);
            Assert.AreSame(inner, ex.InnerException);
        }

        #endregion

        #region UniflashCliExecutionException

        [TestMethod]
        public void UniflashCliExecutionException_DefaultConstructor()
        {
            var ex = new UniflashCliExecutionException();
            Assert.IsNotNull(ex);
        }

        [TestMethod]
        public void UniflashCliExecutionException_MessageConstructor()
        {
            var ex = new UniflashCliExecutionException("Uniflash error");
            Assert.AreEqual("Uniflash error", ex.Message);
        }

        [TestMethod]
        public void UniflashCliExecutionException_InnerExceptionConstructor()
        {
            var inner = new Exception("DSLite crash");
            var ex = new UniflashCliExecutionException("Uniflash failed", inner);
            Assert.AreEqual("Uniflash failed", ex.Message);
            Assert.AreSame(inner, ex.InnerException);
        }

        #endregion

        #region WriteEsp32FlashException

        [TestMethod]
        public void WriteEsp32FlashException_MessageConstructor()
        {
            var ex = new WriteEsp32FlashException("write failed");
            Assert.AreEqual("write failed", ex.Message);
        }

        [TestMethod]
        public void WriteEsp32FlashException_InnerExceptionConstructor()
        {
            var inner = new TimeoutException("write timeout");
            var ex = new WriteEsp32FlashException("write error", inner);
            Assert.AreEqual("write error", ex.Message);
            Assert.AreSame(inner, ex.InnerException);
        }

        #endregion

        #region All exceptions are proper Exception subclasses

        [TestMethod]
        public void AllExceptions_AreExceptionSubclasses()
        {
            // Verify every exception type inherits from Exception
            Type[] exceptionTypes =
            {
                typeof(CantConnectToDfuDeviceException),
                typeof(CantConnectToJLinkDeviceException),
                typeof(CantConnectToJtagDeviceException),
                typeof(CantConnectToNanoDeviceException),
                typeof(DfuFileDoesNotExistException),
                typeof(DfuOperationFailedException),
                typeof(EraseEsp32FlashException),
                typeof(EspToolExecutionException),
                typeof(NanoDeviceOperationFailedException),
                typeof(NoOperationPerformedException),
                typeof(ReadEsp32FlashException),
                typeof(SilinkExecutionException),
                typeof(StLinkCliExecutionException),
                typeof(Stm32UartBootloaderException),
                typeof(UniflashCliExecutionException),
                typeof(WriteEsp32FlashException),
            };

            foreach (Type t in exceptionTypes)
            {
                Assert.IsTrue(typeof(Exception).IsAssignableFrom(t),
                    $"{t.Name} should inherit from Exception");
            }
        }

        #endregion
    }
}
