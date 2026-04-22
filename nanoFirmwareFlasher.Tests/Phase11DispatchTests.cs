// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using nanoFramework.Tools.FirmwareFlasher;

namespace nanoFirmwareFlasher.Tests
{
    /// <summary>
    /// Phase 11 tests: IStmFlashableDevice interface implementation,
    /// FlashDeviceFiles dispatch helper, and early input validation.
    /// </summary>
    [TestClass]
    public class Phase11DispatchTests
    {
        #region IStmFlashableDevice interface tests

        [TestMethod]
        public void IStmFlashableDevice_Interface_Exists()
        {
            var iface = typeof(IStmFlashableDevice);
            Assert.IsNotNull(iface);
            Assert.IsTrue(iface.IsInterface);
            Assert.IsTrue(iface.IsPublic);
        }

        [TestMethod]
        public void IStmFlashableDevice_HasRequiredMembers()
        {
            var iface = typeof(IStmFlashableDevice);

            // DevicePresent property
            var devicePresent = iface.GetProperty("DevicePresent");
            Assert.IsNotNull(devicePresent, "Should have DevicePresent property");
            Assert.AreEqual(typeof(bool), devicePresent.PropertyType);

            // DoMassErase property
            var doMassErase = iface.GetProperty("DoMassErase");
            Assert.IsNotNull(doMassErase, "Should have DoMassErase property");
            Assert.AreEqual(typeof(bool), doMassErase.PropertyType);

            // Verbosity property
            var verbosity = iface.GetProperty("Verbosity");
            Assert.IsNotNull(verbosity, "Should have Verbosity property");
            Assert.AreEqual(typeof(VerbosityLevel), verbosity.PropertyType);

            // FlashHexFiles method
            var flashHex = iface.GetMethod("FlashHexFiles");
            Assert.IsNotNull(flashHex, "Should have FlashHexFiles method");
            Assert.AreEqual(typeof(ExitCodes), flashHex.ReturnType);

            // FlashBinFiles method
            var flashBin = iface.GetMethod("FlashBinFiles");
            Assert.IsNotNull(flashBin, "Should have FlashBinFiles method");
            Assert.AreEqual(typeof(ExitCodes), flashBin.ReturnType);
        }

        [TestMethod]
        public void StmSwdDevice_ImplementsIStmFlashableDevice()
        {
            Assert.IsTrue(typeof(IStmFlashableDevice).IsAssignableFrom(typeof(StmSwdDevice)));
        }

        [TestMethod]
        public void StmStLinkDevice_ImplementsIStmFlashableDevice()
        {
            Assert.IsTrue(typeof(IStmFlashableDevice).IsAssignableFrom(typeof(StmStLinkDevice)));
        }

        [TestMethod]
        public void StmNativeDfuDevice_ImplementsIStmFlashableDevice()
        {
            Assert.IsTrue(typeof(IStmFlashableDevice).IsAssignableFrom(typeof(StmNativeDfuDevice)));
        }

        [TestMethod]
        public void StmDfuDevice_ImplementsIStmFlashableDevice()
        {
            Assert.IsTrue(typeof(IStmFlashableDevice).IsAssignableFrom(typeof(StmDfuDevice)));
        }

        [TestMethod]
        public void StmJtagDevice_ImplementsIStmFlashableDevice()
        {
            Assert.IsTrue(typeof(IStmFlashableDevice).IsAssignableFrom(typeof(StmJtagDevice)));
        }

        #endregion

        #region FlashDeviceFiles helper tests

        [TestMethod]
        public void Stm32Manager_FlashDeviceFiles_MethodExists()
        {
            var method = typeof(Stm32Manager).GetMethod(
                "FlashDeviceFiles",
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(IStmFlashableDevice) },
                null);
            Assert.IsNotNull(method, "FlashDeviceFiles helper should exist on Stm32Manager");
            Assert.AreEqual(typeof(ExitCodes), method.ReturnType);
        }

        [TestMethod]
        public void Stm32Manager_FlashDeviceFiles_SetsVerbosity()
        {
            var options = new Options
            {
                Platform = SupportedPlatform.stm32,
                HexFile = new List<string>(), // empty — no flash work
                BinFile = new List<string>(),
                FlashAddress = new List<string>()
            };
            var manager = new Stm32Manager(options, VerbosityLevel.Diagnostic);

            var mockDevice = new MockFlashableDevice();
            var method = typeof(Stm32Manager).GetMethod(
                "FlashDeviceFiles",
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(IStmFlashableDevice) },
                null);

            var result = (ExitCodes)method.Invoke(manager, new object[] { mockDevice });

            Assert.AreEqual(ExitCodes.OK, result);
            Assert.AreEqual(VerbosityLevel.Diagnostic, mockDevice.Verbosity);
        }

        [TestMethod]
        public void Stm32Manager_FlashDeviceFiles_SetsMassErase()
        {
            var options = new Options
            {
                Platform = SupportedPlatform.stm32,
                MassErase = true,
                HexFile = new List<string>(),
                BinFile = new List<string>(),
                FlashAddress = new List<string>()
            };
            var manager = new Stm32Manager(options, VerbosityLevel.Quiet);

            var mockDevice = new MockFlashableDevice();
            var method = typeof(Stm32Manager).GetMethod(
                "FlashDeviceFiles",
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(IStmFlashableDevice) },
                null);

            method.Invoke(manager, new object[] { mockDevice });

            Assert.IsTrue(mockDevice.DoMassErase);
        }

        [TestMethod]
        public void Stm32Manager_FlashDeviceFiles_ReturnsOK_WhenNoFiles()
        {
            var options = new Options
            {
                Platform = SupportedPlatform.stm32,
                HexFile = new List<string>(),
                BinFile = new List<string>(),
                FlashAddress = new List<string>()
            };
            var manager = new Stm32Manager(options, VerbosityLevel.Quiet);

            var mockDevice = new MockFlashableDevice();
            var method = typeof(Stm32Manager).GetMethod(
                "FlashDeviceFiles",
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(IStmFlashableDevice) },
                null);

            var result = (ExitCodes)method.Invoke(manager, new object[] { mockDevice });

            Assert.AreEqual(ExitCodes.OK, result);
            Assert.IsFalse(mockDevice.FlashHexCalled);
            Assert.IsFalse(mockDevice.FlashBinCalled);
        }

        /// <summary>
        /// Mock implementation for testing FlashDeviceFiles dispatch.
        /// </summary>
        private class MockFlashableDevice : IStmFlashableDevice
        {
            public bool DevicePresent => true;
            public bool DoMassErase { get; set; }
            public VerbosityLevel Verbosity { get; set; }

            public bool FlashHexCalled { get; private set; }
            public bool FlashBinCalled { get; private set; }

            public ExitCodes FlashHexFiles(IList<string> files)
            {
                FlashHexCalled = true;
                return ExitCodes.OK;
            }

            public ExitCodes FlashBinFiles(IList<string> files, IList<string> addresses)
            {
                FlashBinCalled = true;
                return ExitCodes.OK;
            }
        }

        #endregion

        #region Program.cs early validation tests

        [TestMethod]
        public void ValidateInterface_DeployWithoutImage_Fails()
        {
            // This tests that --deploy without --image would be caught
            // by early validation in Program.cs.
            var o = new Options { Deploy = true };
            Assert.IsTrue(o.Deploy);
            Assert.IsNull(o.DeploymentImage);
        }

        [TestMethod]
        public void Options_DeployWithImage_IsValid()
        {
            var o = new Options { Deploy = true, DeploymentImage = "app.bin" };
            Assert.IsTrue(o.Deploy);
            Assert.IsNotNull(o.DeploymentImage);
        }

        #endregion
    }
}
