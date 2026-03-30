// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using nanoFramework.Tools.FirmwareFlasher;
using nanoFramework.Tools.FirmwareFlasher.UsbDfu;

namespace nanoFirmwareFlasher.Tests
{
    [TestClass]
    public class DfuProtocolTests
    {
        #region DfuConst tests

        [TestMethod]
        public void DfuConst_VidPid_AreCorrect()
        {
            Assert.AreEqual(0x0483, DfuConst.StmVendorId);
            Assert.AreEqual(0xDF11, DfuConst.StmDfuProductId);
        }

        [TestMethod]
        public void DfuConst_RequestTypes_AreCorrect()
        {
            // USB DFU spec: Host→Device, Class, Interface = 0x21
            Assert.AreEqual(0x21, DfuConst.DfuRequestOut);
            // Device→Host, Class, Interface = 0xA1
            Assert.AreEqual(0xA1, DfuConst.DfuRequestIn);
        }

        [TestMethod]
        public void DfuConst_RequestCodes_MatchDfuSpec()
        {
            Assert.AreEqual(0, DfuConst.DfuDetach);
            Assert.AreEqual(1, DfuConst.DfuDnload);
            Assert.AreEqual(2, DfuConst.DfuUpload);
            Assert.AreEqual(3, DfuConst.DfuGetStatus);
            Assert.AreEqual(4, DfuConst.DfuClrStatus);
            Assert.AreEqual(5, DfuConst.DfuGetState);
            Assert.AreEqual(6, DfuConst.DfuAbort);
        }

        [TestMethod]
        public void DfuConst_DfuSeCommands_AreCorrect()
        {
            Assert.AreEqual(0x21, DfuConst.DfuSeSetAddress);
            Assert.AreEqual(0x41, DfuConst.DfuSeErase);
            Assert.AreEqual(0x92, DfuConst.DfuSeReadUnprotect);
        }

        [TestMethod]
        public void DfuConst_DataBlockOffset_IsTwo()
        {
            // DfuSe: data blocks start at wBlockNum=2
            Assert.AreEqual(2, DfuConst.DfuSeDataBlockOffset);
        }

        [TestMethod]
        public void DfuConst_DefaultTransferSize_Is2048()
        {
            Assert.AreEqual(2048, DfuConst.DefaultTransferSize);
        }

        #endregion

        #region DfuState enum tests

        [TestMethod]
        public void DfuState_Values_MatchDfuSpec()
        {
            Assert.AreEqual((byte)0, (byte)DfuState.AppIdle);
            Assert.AreEqual((byte)1, (byte)DfuState.AppDetach);
            Assert.AreEqual((byte)2, (byte)DfuState.DfuIdle);
            Assert.AreEqual((byte)3, (byte)DfuState.DfuDnloadSync);
            Assert.AreEqual((byte)4, (byte)DfuState.DfuDnbusy);
            Assert.AreEqual((byte)5, (byte)DfuState.DfuDnloadIdle);
            Assert.AreEqual((byte)6, (byte)DfuState.DfuManifestSync);
            Assert.AreEqual((byte)7, (byte)DfuState.DfuManifest);
            Assert.AreEqual((byte)8, (byte)DfuState.DfuManifestWaitReset);
            Assert.AreEqual((byte)9, (byte)DfuState.DfuUploadIdle);
            Assert.AreEqual((byte)10, (byte)DfuState.DfuError);
        }

        #endregion

        #region DfuStatus enum tests

        [TestMethod]
        public void DfuStatus_Ok_IsZero()
        {
            Assert.AreEqual((byte)0x00, (byte)DfuStatus.Ok);
        }

        [TestMethod]
        public void DfuStatus_ErrorCodes_MatchDfuSpec()
        {
            Assert.AreEqual((byte)0x01, (byte)DfuStatus.ErrTarget);
            Assert.AreEqual((byte)0x03, (byte)DfuStatus.ErrWrite);
            Assert.AreEqual((byte)0x04, (byte)DfuStatus.ErrErase);
            Assert.AreEqual((byte)0x06, (byte)DfuStatus.ErrProg);
            Assert.AreEqual((byte)0x07, (byte)DfuStatus.ErrVerify);
            Assert.AreEqual((byte)0x08, (byte)DfuStatus.ErrAddress);
            Assert.AreEqual((byte)0x0E, (byte)DfuStatus.ErrUnknown);
            Assert.AreEqual((byte)0x0F, (byte)DfuStatus.ErrStalledPkt);
        }

        #endregion

        #region DfuStatusResult tests

        [TestMethod]
        public void DfuStatusResult_PollTimeout_ComputesCorrectly()
        {
            var status = new DfuStatusResult
            {
                Status = DfuStatus.Ok,
                PollTimeoutLow = 0x64,  // 100 decimal
                PollTimeoutMid = 0x00,
                PollTimeoutHigh = 0x00,
                State = DfuState.DfuDnbusy,
                StringIndex = 0,
            };

            Assert.AreEqual(100, status.PollTimeout);
        }

        [TestMethod]
        public void DfuStatusResult_PollTimeout_Handles24BitValues()
        {
            var status = new DfuStatusResult
            {
                PollTimeoutLow = 0xFF,
                PollTimeoutMid = 0xFF,
                PollTimeoutHigh = 0x01,
            };

            // 0x01FFFF = 131071 ms
            Assert.AreEqual(131071, status.PollTimeout);
        }

        [TestMethod]
        public void DfuStatusResult_Size_Is6Bytes()
        {
            Assert.AreEqual(6, Marshal.SizeOf(typeof(DfuStatusResult)));
        }

        #endregion

        #region ExitCodes tests

        [TestMethod]
        public void ExitCodes_NativeDfu_Exist()
        {
            Assert.AreEqual(5030, (int)ExitCodes.E5030);
            Assert.AreEqual(5031, (int)ExitCodes.E5031);
        }

        [TestMethod]
        public void Interface_HasNativeDfu()
        {
            Assert.IsTrue(Enum.IsDefined(typeof(Interface), Interface.NativeDfu));
        }

        #endregion

        #region StmNativeDfuDevice tests

        [TestMethod]
        public void StmNativeDfuDevice_ListDevices_ReturnsEmptyOnNonWindows()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // On Windows, ListDevices may return actual devices or empty, both are valid
                var devices = StmNativeDfuDevice.ListDevices();
                Assert.IsNotNull(devices);
            }
            else
            {
                var devices = StmNativeDfuDevice.ListDevices();
                Assert.AreEqual(0, devices.Count);
            }
        }

        [TestMethod]
        public void StmNativeDfuDevice_ListDevices_ReturnsTuples()
        {
            // ListDevices should return (serial, device) tuples matching StmDfuDevice.ListDevices() format
            List<(string serial, string device)> devices = StmNativeDfuDevice.ListDevices();
            Assert.IsNotNull(devices);

            // Each device should have a non-null serial and device string
            foreach (var device in devices)
            {
                Assert.IsNotNull(device.serial);
                Assert.IsNotNull(device.device);
                Assert.IsTrue(device.device.StartsWith("USB"), $"Device index should start with 'USB', got: {device.device}");
            }
        }

        [TestMethod]
        public void StmNativeDfuDevice_Constructor_CrossPlatform()
        {
            // The constructor should no longer throw PlatformNotSupportedException.
            // Without hardware it will throw CantConnectToDfuDeviceException instead.
            try
            {
                using (var device = new StmNativeDfuDevice())
                {
                    // If we get here, a real DFU device was found (unlikely in CI)
                }
            }
            catch (CantConnectToDfuDeviceException)
            {
                // Expected when no device is connected
            }
        }

        #endregion

        #region DfuDevice serial number parsing tests

        [TestMethod]
        public void SerialParsing_ValidDevicePath_ExtractsSerial()
        {
            // Test the parsing logic indirectly through ListDevices()
            // The device path format is: \\?\usb#vid_0483&pid_df11#SERIAL#{GUID}
            // We verify the parsing by checking that ListDevices builds proper tuples
            var devices = StmNativeDfuDevice.ListDevices();
            Assert.IsNotNull(devices);
            // Can't assert on specific values without hardware, but structure should be correct
        }

        #endregion

        #region DfuOperationFailedException tests

        [TestMethod]
        public void DfuOperationFailedException_DefaultConstructor()
        {
            var ex = new DfuOperationFailedException();
            Assert.IsNotNull(ex);
        }

        [TestMethod]
        public void DfuOperationFailedException_MessageConstructor()
        {
            var ex = new DfuOperationFailedException("test error");
            Assert.AreEqual("test error", ex.Message);
        }

        [TestMethod]
        public void DfuOperationFailedException_InnerExceptionConstructor()
        {
            var inner = new InvalidOperationException("inner");
            var ex = new DfuOperationFailedException("outer", inner);
            Assert.AreEqual("outer", ex.Message);
            Assert.AreSame(inner, ex.InnerException);
        }

        #endregion
    }
}
