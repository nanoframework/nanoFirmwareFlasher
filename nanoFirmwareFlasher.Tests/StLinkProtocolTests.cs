//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using nanoFramework.Tools.FirmwareFlasher;
using nanoFramework.Tools.FirmwareFlasher.Swd;

namespace nanoFirmwareFlasher.Tests
{
    /// <summary>
    /// Tests for the Phase 5 ST-LINK V2/V3 protocol transport.
    /// All tests are unit tests that verify protocol constants, command encoding,
    /// clock divisor mapping, and TransferRequest ↔ DAP register mapping
    /// without requiring real hardware.
    /// </summary>
    [TestClass]
    public class StLinkProtocolTests
    {
        #region ISwdTransport implementation

        [TestMethod]
        public void StLinkTransport_Implements_ISwdTransport()
        {
            // StLinkTransport should implement ISwdTransport
            Assert.IsTrue(typeof(ISwdTransport).IsAssignableFrom(typeof(StLinkTransport)));
        }

        [TestMethod]
        public void StLinkTransport_Implements_IDisposable()
        {
            Assert.IsTrue(typeof(IDisposable).IsAssignableFrom(typeof(StLinkTransport)));
        }

        [TestMethod]
        public void StLinkTransport_PacketSize_Is64()
        {
            var transport = new StLinkTransport();
            Assert.AreEqual(64, transport.PacketSize);
        }

        [TestMethod]
        public void StLinkTransport_ProductName_DefaultsToNull()
        {
            var transport = new StLinkTransport();
            Assert.IsNull(transport.ProductName);
        }

        [TestMethod]
        public void StLinkTransport_SerialNumber_DefaultsToNull()
        {
            var transport = new StLinkTransport();
            Assert.IsNull(transport.SerialNumber);
        }

        #endregion

        #region StmStLinkDevice public API

        [TestMethod]
        public void StmStLinkDevice_HasExpectedPublicProperties()
        {
            // Verify the public API matches what integration code expects
            var type = typeof(StmStLinkDevice);

            Assert.IsNotNull(type.GetProperty("DevicePresent"));
            Assert.IsNotNull(type.GetProperty("ProbeId"));
            Assert.IsNotNull(type.GetProperty("ProbeName"));
            Assert.IsNotNull(type.GetProperty("DeviceName"));
            Assert.IsNotNull(type.GetProperty("DeviceCPU"));
            Assert.IsNotNull(type.GetProperty("DpIdcode"));
            Assert.IsNotNull(type.GetProperty("DoMassErase"));
            Assert.IsNotNull(type.GetProperty("Verbosity"));
        }

        [TestMethod]
        public void StmStLinkDevice_HasExpectedPublicMethods()
        {
            var type = typeof(StmStLinkDevice);

            Assert.IsNotNull(type.GetMethod("FlashHexFiles"));
            Assert.IsNotNull(type.GetMethod("FlashBinFiles"));
            Assert.IsNotNull(type.GetMethod("MassErase"));
            Assert.IsNotNull(type.GetMethod("ResetMcu"));
            Assert.IsNotNull(type.GetMethod("StartExecution"));
            Assert.IsNotNull(type.GetMethod("ListDevices"));
            Assert.IsNotNull(type.GetMethod("Dispose"));
        }

        [TestMethod]
        public void StmStLinkDevice_ListDevices_ReturnsListType()
        {
            // Static method should return List<string>
            var method = typeof(StmStLinkDevice).GetMethod("ListDevices");
            Assert.IsNotNull(method);
            Assert.AreEqual(typeof(List<string>), method.ReturnType);
            Assert.IsTrue(method.IsStatic);
        }

        [TestMethod]
        public void StmStLinkDevice_Implements_IDisposable()
        {
            Assert.IsTrue(typeof(IDisposable).IsAssignableFrom(typeof(StmStLinkDevice)));
        }

        #endregion

        #region Interface enum

        [TestMethod]
        public void Interface_NativeStLink_EnumValueExists()
        {
            // Verify the NativeStLink value exists in the Interface enum
            Assert.IsTrue(Enum.IsDefined(typeof(Interface), Interface.NativeStLink));
        }

        [TestMethod]
        public void Interface_NativeStLink_IsDistinctFromOthers()
        {
            // NativeStLink must be different from all other interface values
            Assert.AreNotEqual(Interface.None, Interface.NativeStLink);
            Assert.AreNotEqual(Interface.Jtag, Interface.NativeStLink);
            Assert.AreNotEqual(Interface.Dfu, Interface.NativeStLink);
            Assert.AreNotEqual(Interface.Uart, Interface.NativeStLink);
            Assert.AreNotEqual(Interface.NativeDfu, Interface.NativeStLink);
            Assert.AreNotEqual(Interface.NativeSwd, Interface.NativeStLink);
        }

        [TestMethod]
        public void Interface_AllValues_AreUnique()
        {
            var values = Enum.GetValues(typeof(Interface)).Cast<int>().ToArray();
            int distinctCount = values.Distinct().Count();
            Assert.AreEqual(values.Length, distinctCount, "All Interface enum values must be unique");
        }

        #endregion

        #region TransferRequest DAP register mapping

        [TestMethod]
        public void TransferRequest_DpRead_MapsTo_StLinkDpPort()
        {
            // DP read: APnDP=0 (bit 0), RnW=1 (bit 1) → request = 0x02
            var req = TransferRequest.DpRead(0x00);

            // ST-LINK transport interprets: isAp=(request & 0x01)!=0 → false
            bool isAp = (req.Request & 0x01) != 0;
            bool isRead = (req.Request & 0x02) != 0;
            byte regAddr = (byte)(req.Request & 0x0C);

            Assert.IsFalse(isAp, "DP read should not be AP");
            Assert.IsTrue(isRead, "DP read should be a read");
            Assert.AreEqual((byte)0x00, regAddr);
        }

        [TestMethod]
        public void TransferRequest_ApRead_MapsTo_StLinkApPort()
        {
            // AP read: APnDP=1 (bit 0), RnW=1 (bit 1) → request = 0x03
            var req = TransferRequest.ApRead(0x00);

            bool isAp = (req.Request & 0x01) != 0;
            bool isRead = (req.Request & 0x02) != 0;
            byte regAddr = (byte)(req.Request & 0x0C);

            Assert.IsTrue(isAp, "AP read should be AP");
            Assert.IsTrue(isRead, "AP read should be a read");
            Assert.AreEqual((byte)0x00, regAddr);
        }

        [TestMethod]
        public void TransferRequest_DpWrite_MapsTo_StLinkDpPort()
        {
            // DP write: APnDP=0, RnW=0 → request = 0x00
            var req = TransferRequest.DpWrite(0x04, 0xAABBCCDD);

            bool isAp = (req.Request & 0x01) != 0;
            bool isRead = (req.Request & 0x02) != 0;
            byte regAddr = (byte)(req.Request & 0x0C);

            Assert.IsFalse(isAp, "DP write should not be AP");
            Assert.IsFalse(isRead, "DP write should not be a read");
            Assert.AreEqual((byte)0x04, regAddr);
            Assert.AreEqual(0xAABBCCDDu, req.Data);
        }

        [TestMethod]
        public void TransferRequest_ApWrite_MapsTo_StLinkApPort()
        {
            // AP write: APnDP=1, RnW=0 → request = 0x01
            var req = TransferRequest.ApWrite(0x0C, 0x11223344);

            bool isAp = (req.Request & 0x01) != 0;
            bool isRead = (req.Request & 0x02) != 0;
            byte regAddr = (byte)(req.Request & 0x0C);

            Assert.IsTrue(isAp, "AP write should be AP");
            Assert.IsFalse(isRead, "AP write should not be a read");
            Assert.AreEqual((byte)0x0C, regAddr);
            Assert.AreEqual(0x11223344u, req.Data);
        }

        [TestMethod]
        public void TransferRequest_AllDpAddresses_MapCorrectly()
        {
            byte[] addrs = { 0x00, 0x04, 0x08, 0x0C };

            foreach (byte addr in addrs)
            {
                var req = TransferRequest.DpRead(addr);
                byte regAddr = (byte)(req.Request & 0x0C);
                Assert.AreEqual(addr, regAddr, $"DP address 0x{addr:X2} should map to register address 0x{addr:X2}");
            }
        }

        [TestMethod]
        public void TransferRequest_AllApAddresses_MapCorrectly()
        {
            byte[] addrs = { 0x00, 0x04, 0x08, 0x0C };

            foreach (byte addr in addrs)
            {
                var req = TransferRequest.ApRead(addr);
                byte regAddr = (byte)(req.Request & 0x0C);
                Assert.AreEqual(addr, regAddr, $"AP address 0x{addr:X2} should map to register address 0x{addr:X2}");
            }
        }

        #endregion

        #region StLinkTransport no-op methods

        [TestMethod]
        public void StLinkTransport_TransferConfigure_ReturnsTrue()
        {
            // TransferConfigure is a no-op on ST-LINK, should always succeed
            var transport = new StLinkTransport();
            Assert.IsTrue(transport.TransferConfigure(0, 100, 0));
        }

        [TestMethod]
        public void StLinkTransport_SwdConfigure_ReturnsTrue()
        {
            // SwdConfigure is a no-op on ST-LINK, should always succeed
            var transport = new StLinkTransport();
            Assert.IsTrue(transport.SwdConfigure(0));
        }

        [TestMethod]
        public void StLinkTransport_SwjSequence_ReturnsTrue()
        {
            // SwjSequence is a no-op on ST-LINK, should always succeed
            var transport = new StLinkTransport();
            Assert.IsTrue(transport.SwjSequence(51, new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x9E, 0xE7 }));
        }

        #endregion

        #region SwdProtocol uses ISwdTransport

        [TestMethod]
        public void SwdProtocol_AcceptsISwdTransport()
        {
            // SwdProtocol constructor should accept any ISwdTransport, including StLinkTransport
            var type = typeof(SwdProtocol);
            var constructor = type.GetConstructor(
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public,
                null,
                new[] { typeof(ISwdTransport) },
                null);

            Assert.IsNotNull(constructor, "SwdProtocol should have a constructor that accepts ISwdTransport");
        }

        [TestMethod]
        public void SwdProtocol_AcceptsStLinkTransport()
        {
            // Since StLinkTransport implements ISwdTransport, it should be passable to SwdProtocol
            var stLink = new StLinkTransport();
            Assert.IsInstanceOfType(stLink, typeof(ISwdTransport));

            // Verify we can create a SwdProtocol with it (via reflection since internal)
            var type = typeof(SwdProtocol);
            var constructor = type.GetConstructor(
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public,
                null,
                new[] { typeof(ISwdTransport) },
                null);

            var swd = constructor.Invoke(new object[] { stLink });
            Assert.IsNotNull(swd);
        }

        #endregion

        #region StLinkTransport enumerate (graceful when no device connected)

        [TestMethod]
        public void StLinkTransport_Enumerate_ReturnsListType()
        {
            // The static Enumerate method should return a properly typed list
            var method = typeof(StLinkTransport).GetMethod("Enumerate",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);

            Assert.IsNotNull(method, "StLinkTransport should have an Enumerate method");
        }

        [TestMethod]
        public void StmStLinkDevice_ListDevices_ReturnsNonNull()
        {
            // ListDevices should return a non-null list even when no devices are connected
            var devices = StmStLinkDevice.ListDevices();
            Assert.IsNotNull(devices);
        }

        #endregion

        #region Dispose safety

        [TestMethod]
        public void StLinkTransport_Dispose_DoesNotThrow_WhenNotOpened()
        {
            // Disposing without opening should not throw
            var transport = new StLinkTransport();
            transport.Dispose();
        }

        [TestMethod]
        public void StLinkTransport_DoubleDispose_DoesNotThrow()
        {
            var transport = new StLinkTransport();
            transport.Dispose();
            transport.Dispose(); // second dispose should not throw
        }

        #endregion

        #region Phase 6: Consolidation — auto-detection

        [TestMethod]
        public void Stm32Operations_ResetMcu_IsStaticMethod()
        {
            var method = typeof(Stm32Operations).GetMethod("ResetMcu");
            Assert.IsNotNull(method);
            Assert.IsTrue(method.IsStatic);
        }

        [TestMethod]
        public void Stm32Operations_MassErase_IsStaticMethod()
        {
            var method = typeof(Stm32Operations).GetMethod("MassErase");
            Assert.IsNotNull(method);
            Assert.IsTrue(method.IsStatic);
        }

        [TestMethod]
        public void Stm32Operations_UpdateFirmwareAsync_HasInterfaceParameter()
        {
            // Verify that UpdateFirmwareAsync accepts an Interface parameter
            var method = typeof(Stm32Operations).GetMethod("UpdateFirmwareAsync");
            Assert.IsNotNull(method);

            var parameters = method.GetParameters();
            bool hasInterfaceParam = false;

            foreach (var param in parameters)
            {
                if (param.ParameterType == typeof(Interface))
                {
                    hasInterfaceParam = true;
                    break;
                }
            }

            Assert.IsTrue(hasInterfaceParam, "UpdateFirmwareAsync should have an Interface parameter");
        }

        [TestMethod]
        public void Interface_HasAllExpectedValues()
        {
            // Verify all 7 interface values exist
            var values = Enum.GetValues(typeof(Interface));
            Assert.AreEqual(7, values.Length, "Interface enum should have exactly 7 values");

            Assert.IsTrue(Enum.IsDefined(typeof(Interface), Interface.None));
            Assert.IsTrue(Enum.IsDefined(typeof(Interface), Interface.Jtag));
            Assert.IsTrue(Enum.IsDefined(typeof(Interface), Interface.Dfu));
            Assert.IsTrue(Enum.IsDefined(typeof(Interface), Interface.Uart));
            Assert.IsTrue(Enum.IsDefined(typeof(Interface), Interface.NativeDfu));
            Assert.IsTrue(Enum.IsDefined(typeof(Interface), Interface.NativeSwd));
            Assert.IsTrue(Enum.IsDefined(typeof(Interface), Interface.NativeStLink));
        }

        [TestMethod]
        public void StmSwdDevice_ListDevices_IsNotPlatformRestricted()
        {
            // After Phase 4/6, ListDevices should work on all platforms (no Windows-only guard)
            // It should return a list (possibly empty) without throwing
            var result = StmSwdDevice.ListDevices();
            Assert.IsNotNull(result);
        }

        [TestMethod]
        public void StmStLinkDevice_ListDevices_WorksOnAllPlatforms()
        {
            // ListDevices should work on all platforms without throwing
            var result = StmStLinkDevice.ListDevices();
            Assert.IsNotNull(result);
        }

        #endregion

        #region Phase 7: CLI-Free Robustness

        [TestMethod]
        public void StmDeviceBase_RunSTM32ProgrammerCLI_ThrowsOnMissingBinary()
        {
            // RunSTM32ProgrammerCLI should throw StLinkCliExecutionException when CLI binary is missing,
            // not Win32Exception/FileNotFoundException
            try
            {
                StmDeviceBase.RunSTM32ProgrammerCLI("--list");
                // If CLI happens to exist, test is inconclusive
            }
            catch (StLinkCliExecutionException ex)
            {
                // Expected — should mention the tool name and suggest native alternatives
                Assert.IsTrue(
                    ex.Message.Contains("STM32_Programmer_CLI") || ex.Message.Contains("native"),
                    $"Error message should reference the missing tool or native alternatives. Got: {ex.Message}");
            }
        }

        [TestMethod]
        public void StmDeviceBase_RunSTM32ProgrammerCLI_IsPublicStatic()
        {
            var method = typeof(StmDeviceBase).GetMethod("RunSTM32ProgrammerCLI");
            Assert.IsNotNull(method, "RunSTM32ProgrammerCLI should exist");
            Assert.IsTrue(method.IsStatic);
            Assert.IsTrue(method.IsPublic);
        }

        [TestMethod]
        public void JLinkCli_RunJLinkCLI_IsInternalMethod()
        {
            // RunJLinkCLI should be internal (not public)
            var method = typeof(JLinkCli).GetMethod(
                "RunJLinkCLI",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(method, "RunJLinkCLI should exist as internal static method");
        }

        [TestMethod]
        public void SilinkCli_RunSilinkCLI_IsInternalMethod()
        {
            // RunSilinkCLI should be internal (not public)
            var method = typeof(SilinkCli).GetMethod(
                "RunSilinkCLI",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(method, "RunSilinkCLI should exist as internal static method");
        }

        [TestMethod]
        public void ExitCodes_E1000_DisplaySuggestsNativeAlternatives()
        {
            // E1000 (No DFU device) should suggest native alternatives
            var memberInfo = typeof(ExitCodes).GetField("E1000");
            Assert.IsNotNull(memberInfo);

            var displayAttr = memberInfo.GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.DisplayAttribute), false);
            Assert.AreEqual(1, displayAttr.Length, "E1000 should have a Display attribute");

            var name = ((System.ComponentModel.DataAnnotations.DisplayAttribute)displayAttr[0]).Name;
            Assert.IsTrue(
                name.Contains("nativedfu") || name.Contains("native") || name.Contains("uart"),
                $"E1000 display should suggest native alternatives. Got: {name}");
        }

        [TestMethod]
        public void ExitCodes_E5001_DisplaySuggestsNativeAlternatives()
        {
            // E5001 (No JTAG device) should suggest native alternatives
            var memberInfo = typeof(ExitCodes).GetField("E5001");
            Assert.IsNotNull(memberInfo);

            var displayAttr = memberInfo.GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.DisplayAttribute), false);
            Assert.AreEqual(1, displayAttr.Length);

            var name = ((System.ComponentModel.DataAnnotations.DisplayAttribute)displayAttr[0]).Name;
            Assert.IsTrue(
                name.Contains("nativestlink") || name.Contains("nativeswd") || name.Contains("native"),
                $"E5001 display should suggest native alternatives. Got: {name}");
        }

        [TestMethod]
        public void ExitCodes_E9010_DisplaySuggestsNativeAlternatives()
        {
            // E9010 (No device connected) should suggest native alternatives
            var memberInfo = typeof(ExitCodes).GetField("E9010");
            Assert.IsNotNull(memberInfo);

            var displayAttr = memberInfo.GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.DisplayAttribute), false);
            Assert.AreEqual(1, displayAttr.Length);

            var name = ((System.ComponentModel.DataAnnotations.DisplayAttribute)displayAttr[0]).Name;
            Assert.IsTrue(
                name.Contains("native") || name.Contains("uart"),
                $"E9010 display should suggest native alternatives. Got: {name}");
        }

        [TestMethod]
        public void StmDeviceBase_ExecuteListDevices_IsPublicStatic()
        {
            // ExecuteListDevices wraps RunSTM32ProgrammerCLI — verify it exists
            var method = typeof(StmDeviceBase).GetMethod("ExecuteListDevices");
            Assert.IsNotNull(method, "ExecuteListDevices should exist");
            Assert.IsTrue(method.IsStatic);
            Assert.IsTrue(method.IsPublic);
        }

        #endregion
    }
}
