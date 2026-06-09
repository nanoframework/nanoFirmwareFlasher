//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using nanoFramework.Tools.FirmwareFlasher;
using nanoFramework.Tools.FirmwareFlasher.Swd;

namespace nanoFirmwareFlasher.Tests
{
    /// <summary>
    /// Tests for the Phase 3 CMSIS-DAP / SWD protocol stack.
    /// All tests are unit tests that verify protocol constants, data structures,
    /// and logic without requiring real hardware.
    /// </summary>
    [TestClass]
    public class SwdProtocolTests
    {
        #region CMSIS-DAP command ID constants

        [TestMethod]
        public void CmsisDap_CommandIds_AreCorrect()
        {
            Assert.AreEqual(0x00, CmsisDap.DapInfo);
            Assert.AreEqual(0x01, CmsisDap.DapHostStatus);
            Assert.AreEqual(0x02, CmsisDap.DapConnect);
            Assert.AreEqual(0x03, CmsisDap.DapDisconnect);
            Assert.AreEqual(0x04, CmsisDap.DapTransferConfigure);
            Assert.AreEqual(0x05, CmsisDap.DapTransfer);
            Assert.AreEqual(0x06, CmsisDap.DapTransferBlock);
            Assert.AreEqual(0x07, CmsisDap.DapTransferAbort);
            Assert.AreEqual(0x08, CmsisDap.DapWriteAbort);
            Assert.AreEqual(0x09, CmsisDap.DapDelay);
            Assert.AreEqual(0x0A, CmsisDap.DapResetTarget);
            Assert.AreEqual(0x10, CmsisDap.DapSwjPins);
            Assert.AreEqual(0x11, CmsisDap.DapSwjClock);
            Assert.AreEqual(0x12, CmsisDap.DapSwjSequence);
            Assert.AreEqual(0x13, CmsisDap.DapSwdConfigure);
            Assert.AreEqual(0x1D, CmsisDap.DapSwdSequence);
        }

        [TestMethod]
        public void CmsisDap_ResponseStatus_AreCorrect()
        {
            Assert.AreEqual(0x00, CmsisDap.DapOk);
            Assert.AreEqual(0xFF, CmsisDap.DapError);
        }

        [TestMethod]
        public void CmsisDap_PortSwd_IsOne()
        {
            Assert.AreEqual(1, CmsisDap.DapPortSwd);
        }

        [TestMethod]
        public void CmsisDap_InfoIds_AreCorrect()
        {
            Assert.AreEqual(0x01, CmsisDap.DapInfoVendorId);
            Assert.AreEqual(0x02, CmsisDap.DapInfoProductId);
            Assert.AreEqual(0x03, CmsisDap.DapInfoSerialNumber);
            Assert.AreEqual(0x04, CmsisDap.DapInfoFirmwareVersion);
            Assert.AreEqual(0x05, CmsisDap.DapInfoDeviceVendor);
            Assert.AreEqual(0x06, CmsisDap.DapInfoDeviceName);
            Assert.AreEqual(0xF0, CmsisDap.DapInfoCapabilities);
            Assert.AreEqual(0xFE, CmsisDap.DapInfoPacketCount);
            Assert.AreEqual(0xFF, CmsisDap.DapInfoPacketSize);
        }

        #endregion

        #region TransferRequest helpers

        [TestMethod]
        public void TransferRequest_DpRead_SetsCorrectBits()
        {
            // DP read at address 0x00: APnDP=0, RnW=1 → bits = 0x02
            var req = TransferRequest.DpRead(0x00);
            Assert.AreEqual(0x02, req.Request);
            Assert.AreEqual(0u, req.Data);
        }

        [TestMethod]
        public void TransferRequest_DpRead_WithAddress4_SetsAddressBits()
        {
            // DP read at address 0x04: APnDP=0, RnW=1, A[3:2]=01 → 0x02 | (0x04 & 0x0C) = 0x06
            var req = TransferRequest.DpRead(0x04);
            Assert.AreEqual(0x06, req.Request);
        }

        [TestMethod]
        public void TransferRequest_DpRead_WithAddress8_SetsAddressBits()
        {
            // DP read at address 0x08: APnDP=0, RnW=1, A[3:2]=10 → 0x02 | 0x08 = 0x0A
            var req = TransferRequest.DpRead(0x08);
            Assert.AreEqual(0x0A, req.Request);
        }

        [TestMethod]
        public void TransferRequest_DpRead_WithAddressC_SetsAddressBits()
        {
            // DP read at address 0x0C: APnDP=0, RnW=1, A[3:2]=11 → 0x02 | 0x0C = 0x0E
            var req = TransferRequest.DpRead(0x0C);
            Assert.AreEqual(0x0E, req.Request);
        }

        [TestMethod]
        public void TransferRequest_DpWrite_SetsCorrectBits()
        {
            // DP write at address 0x00: APnDP=0, RnW=0 → bits = 0x00
            var req = TransferRequest.DpWrite(0x00, 0x12345678);
            Assert.AreEqual(0x00, req.Request);
            Assert.AreEqual(0x12345678u, req.Data);
        }

        [TestMethod]
        public void TransferRequest_DpWrite_WithAddress8_SetsAddressBits()
        {
            // DP write at address 0x08: APnDP=0, RnW=0, A[3:2]=10 → 0x08
            var req = TransferRequest.DpWrite(0x08, 0xDEADBEEF);
            Assert.AreEqual(0x08, req.Request);
            Assert.AreEqual(0xDEADBEEFu, req.Data);
        }

        [TestMethod]
        public void TransferRequest_ApRead_SetsCorrectBits()
        {
            // AP read at address 0x00: APnDP=1, RnW=1 → bits = 0x03
            var req = TransferRequest.ApRead(0x00);
            Assert.AreEqual(0x03, req.Request);
            Assert.AreEqual(0u, req.Data);
        }

        [TestMethod]
        public void TransferRequest_ApRead_WithAddress4_SetsAddressBits()
        {
            // AP read at address 0x04: APnDP=1, RnW=1, A[3:2]=01 → 0x03 | 0x04 = 0x07
            var req = TransferRequest.ApRead(0x04);
            Assert.AreEqual(0x07, req.Request);
        }

        [TestMethod]
        public void TransferRequest_ApWrite_SetsCorrectBits()
        {
            // AP write at address 0x00: APnDP=1, RnW=0 → bits = 0x01
            var req = TransferRequest.ApWrite(0x00, 0xCAFEBABE);
            Assert.AreEqual(0x01, req.Request);
            Assert.AreEqual(0xCAFEBABEu, req.Data);
        }

        [TestMethod]
        public void TransferRequest_ApWrite_WithAddressC_SetsAddressBits()
        {
            // AP write at address 0x0C: APnDP=1, RnW=0, A[3:2]=11 → 0x01 | 0x0C = 0x0D
            var req = TransferRequest.ApWrite(0x0C, 0);
            Assert.AreEqual(0x0D, req.Request);
        }

        #endregion

        #region SWD Protocol DP register addresses

        [TestMethod]
        public void SwdProtocol_DpRegisters_HaveCorrectAddresses()
        {
            Assert.AreEqual(0x00, SwdProtocol.DpIdcode);
            Assert.AreEqual(0x00, SwdProtocol.DpAbort);
            Assert.AreEqual(0x04, SwdProtocol.DpCtrlStat);
            Assert.AreEqual(0x08, SwdProtocol.DpSelect);
            Assert.AreEqual(0x0C, SwdProtocol.DpRdbuff);
        }

        [TestMethod]
        public void SwdProtocol_CtrlStatBits_AreCorrect()
        {
            Assert.AreEqual(1U << 30, SwdProtocol.CtrlStatCsyspwrupreq);
            Assert.AreEqual(1U << 28, SwdProtocol.CtrlStatCdbgpwrupreq);
            Assert.AreEqual(1U << 31, SwdProtocol.CtrlStatCsyspwrupack);
            Assert.AreEqual(1U << 29, SwdProtocol.CtrlStatCdbgpwrupack);
            Assert.AreEqual(1U << 5, SwdProtocol.CtrlStatStickyerr);
            Assert.AreEqual(1U << 4, SwdProtocol.CtrlStatStickycmp);
            Assert.AreEqual(1U << 1, SwdProtocol.CtrlStatStickyorun);
        }

        [TestMethod]
        public void SwdProtocol_AbortBits_AreCorrect()
        {
            Assert.AreEqual(1U << 4, SwdProtocol.AbortOrunerrclr);
            Assert.AreEqual(1U << 3, SwdProtocol.AbortWderrclr);
            Assert.AreEqual(1U << 2, SwdProtocol.AbortStkerrclr);
            Assert.AreEqual(1U << 1, SwdProtocol.AbortStkcmpclr);
            Assert.AreEqual(1U << 0, SwdProtocol.AbortDapabort);
        }

        #endregion

        #region AP register constants

        [TestMethod]
        public void SwdProtocol_ApRegisters_HaveCorrectOffsets()
        {
            Assert.AreEqual(0x00, SwdProtocol.ApCsw);
            Assert.AreEqual(0x04, SwdProtocol.ApTar);
            Assert.AreEqual(0x0C, SwdProtocol.ApDrw);
            Assert.AreEqual(0xFC, SwdProtocol.ApIdr);
        }

        [TestMethod]
        public void SwdProtocol_CswConstants_AreCorrect()
        {
            Assert.AreEqual(0x02u, SwdProtocol.CswSize32);
            Assert.AreEqual(0x10u, SwdProtocol.CswAddrinc_Single);
            Assert.AreEqual(0x00u, SwdProtocol.CswAddrinc_Off);
            Assert.AreEqual(1U << 31, SwdProtocol.CswDbgSwEnable);
        }

        #endregion

        #region SwdProtocolException

        [TestMethod]
        public void SwdProtocolException_DefaultConstructor_Works()
        {
            var ex = new SwdProtocolException();
            Assert.IsNotNull(ex);
        }

        [TestMethod]
        public void SwdProtocolException_MessageConstructor_SetsMessage()
        {
            var ex = new SwdProtocolException("test error");
            Assert.AreEqual("test error", ex.Message);
        }

        [TestMethod]
        public void SwdProtocolException_InnerExceptionConstructor_Works()
        {
            var inner = new InvalidOperationException("inner");
            var ex = new SwdProtocolException("outer", inner);
            Assert.AreEqual("outer", ex.Message);
            Assert.AreSame(inner, ex.InnerException);
        }

        [TestMethod]
        public void SwdProtocolException_IsSerializable()
        {
            var ex = new SwdProtocolException("serializable");
            Assert.IsTrue(ex is System.Runtime.Serialization.ISerializable);
        }

        #endregion

        #region Exit codes

        [TestMethod]
        public void ExitCodes_NativeSwdValues_AreCorrect()
        {
            Assert.AreEqual(5040, (int)ExitCodes.E5040);
            Assert.AreEqual(5041, (int)ExitCodes.E5041);
        }

        #endregion

        #region Interface enum

        [TestMethod]
        public void Interface_NativeSwd_Exists()
        {
            Assert.IsTrue(Enum.IsDefined(typeof(Interface), Interface.NativeSwd));
        }

        [TestMethod]
        public void Interface_AllValues_AreDistinct()
        {
            var values = Enum.GetValues(typeof(Interface)).Cast<int>().ToArray();
            Assert.AreEqual(values.Length, values.Distinct().Count());
        }

        #endregion

        #region Stm32FlashProgrammer family detection

        [TestMethod]
        public void Stm32Family_Unknown_IsDefault()
        {
            Assert.AreEqual(0, (int)Stm32FlashProgrammer.Stm32Family.Unknown);
        }

        [TestMethod]
        public void Stm32Family_AllValues_AreDistinct()
        {
            var values = Enum.GetValues(typeof(Stm32FlashProgrammer.Stm32Family)).Cast<int>().ToArray();
            Assert.AreEqual(values.Length, values.Distinct().Count());
        }

        [TestMethod]
        public void Stm32Family_ContainsExpectedFamilies()
        {
            Assert.IsTrue(Enum.IsDefined(typeof(Stm32FlashProgrammer.Stm32Family), "F0"));
            Assert.IsTrue(Enum.IsDefined(typeof(Stm32FlashProgrammer.Stm32Family), "F1"));
            Assert.IsTrue(Enum.IsDefined(typeof(Stm32FlashProgrammer.Stm32Family), "F4"));
            Assert.IsTrue(Enum.IsDefined(typeof(Stm32FlashProgrammer.Stm32Family), "F7"));
            Assert.IsTrue(Enum.IsDefined(typeof(Stm32FlashProgrammer.Stm32Family), "H7"));
            Assert.IsTrue(Enum.IsDefined(typeof(Stm32FlashProgrammer.Stm32Family), "L0"));
            Assert.IsTrue(Enum.IsDefined(typeof(Stm32FlashProgrammer.Stm32Family), "L4"));
            Assert.IsTrue(Enum.IsDefined(typeof(Stm32FlashProgrammer.Stm32Family), "G0"));
            Assert.IsTrue(Enum.IsDefined(typeof(Stm32FlashProgrammer.Stm32Family), "G4"));
            Assert.IsTrue(Enum.IsDefined(typeof(Stm32FlashProgrammer.Stm32Family), "WB"));
            Assert.IsTrue(Enum.IsDefined(typeof(Stm32FlashProgrammer.Stm32Family), "U5"));
        }

        #endregion

        #region CmsisDap enumeration

        [TestMethod]
        public void CmsisDap_Enumerate_ReturnsEmptyWhenNoDevices()
        {
            // Enumerate should always succeed (empty list if no devices)
            var result = CmsisDap.Enumerate();
            Assert.IsNotNull(result);
            // On CI or machines without probes, list should be empty or contain real devices
        }

        [TestMethod]
        public void StmSwdDevice_ListDevices_ReturnsListOnWindows()
        {
            // Should not throw, just return possibly empty list
            var result = StmSwdDevice.ListDevices();
            Assert.IsNotNull(result);
        }

        #endregion

        #region CmsisDap IDisposable

        [TestMethod]
        public void CmsisDap_Dispose_DoesNotThrow()
        {
            // Disposing without opening should be safe
            var dap = new CmsisDap();
            dap.Dispose();
            // Double dispose should also be safe
            dap.Dispose();
        }

        #endregion

        #region TransferRequest bitfield patterns

        [TestMethod]
        public void TransferRequest_ReadRequests_HaveBit1Set()
        {
            // All read requests should have bit 1 (RnW) set
            var dpRead = TransferRequest.DpRead(0x00);
            Assert.IsTrue((dpRead.Request & 0x02) != 0, "DP Read should have RnW bit set");

            var apRead = TransferRequest.ApRead(0x00);
            Assert.IsTrue((apRead.Request & 0x02) != 0, "AP Read should have RnW bit set");
        }

        [TestMethod]
        public void TransferRequest_WriteRequests_HaveBit1Clear()
        {
            // All write requests should have bit 1 (RnW) clear
            var dpWrite = TransferRequest.DpWrite(0x00, 0);
            Assert.IsTrue((dpWrite.Request & 0x02) == 0, "DP Write should have RnW bit clear");

            var apWrite = TransferRequest.ApWrite(0x00, 0);
            Assert.IsTrue((apWrite.Request & 0x02) == 0, "AP Write should have RnW bit clear");
        }

        [TestMethod]
        public void TransferRequest_ApRequests_HaveBit0Set()
        {
            // AP requests should have bit 0 (APnDP) set
            var apRead = TransferRequest.ApRead(0x00);
            Assert.IsTrue((apRead.Request & 0x01) != 0, "AP Read should have APnDP bit set");

            var apWrite = TransferRequest.ApWrite(0x00, 0);
            Assert.IsTrue((apWrite.Request & 0x01) != 0, "AP Write should have APnDP bit set");
        }

        [TestMethod]
        public void TransferRequest_DpRequests_HaveBit0Clear()
        {
            // DP requests should have bit 0 (APnDP) clear
            var dpRead = TransferRequest.DpRead(0x00);
            Assert.IsTrue((dpRead.Request & 0x01) == 0, "DP Read should have APnDP bit clear");

            var dpWrite = TransferRequest.DpWrite(0x00, 0);
            Assert.IsTrue((dpWrite.Request & 0x01) == 0, "DP Write should have APnDP bit clear");
        }

        [TestMethod]
        public void TransferRequest_AllAddresses_MapCorrectly()
        {
            // Verify all four valid DP addresses (0x00, 0x04, 0x08, 0x0C) produce correct A[3:2] bits
            byte[] addresses = { 0x00, 0x04, 0x08, 0x0C };
            byte[] expectedAddrBits = { 0x00, 0x04, 0x08, 0x0C };

            for (int i = 0; i < addresses.Length; i++)
            {
                var req = TransferRequest.DpRead(addresses[i]);
                byte addrBits = (byte)(req.Request & 0x0C);
                Assert.AreEqual(expectedAddrBits[i], addrBits,
                    $"Address 0x{addresses[i]:X2} should produce A[3:2] bits 0x{expectedAddrBits[i]:X2}, got 0x{addrBits:X2}");
            }
        }

        #endregion
    }
}
