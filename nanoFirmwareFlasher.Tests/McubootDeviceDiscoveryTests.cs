// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using nanoFramework.Tools.FirmwareFlasher.Mcuboot;

namespace nanoFirmwareFlasher.Tests
{
    [TestClass]
    public class McubootDeviceDiscoveryTests
    {
        [TestMethod]
        public async Task ProbeSerialPorts_NullList_ReturnsEmpty()
        {
            List<McubootDiscoveredDevice> devices = await McubootDeviceDiscovery.ProbeSerialPortsAsync(null);

            Assert.IsNotNull(devices);
            Assert.AreEqual(0, devices.Count);
        }

        [TestMethod]
        public async Task ProbeSerialPorts_EmptyList_ReturnsEmpty()
        {
            List<McubootDiscoveredDevice> devices = await McubootDeviceDiscovery.ProbeSerialPortsAsync(Array.Empty<string>());

            Assert.AreEqual(0, devices.Count);
        }

        [TestMethod]
        public async Task ProbeSerialPorts_NonexistentPort_IsSkippedWithoutThrowing()
        {
            List<McubootDiscoveredDevice> devices = await McubootDeviceDiscovery.ProbeSerialPortsAsync(
                new[] { "COM_DOES_NOT_EXIST", "", null },
                probeTimeoutMs: 100);

            Assert.AreEqual(0, devices.Count, "a port that can't be opened must not be reported as a device");
        }

        [TestMethod]
        public void McumgrClient_TimeoutMs_RejectsNonPositive()
        {
            using var client = new McumgrClient("COM_DOES_NOT_EXIST", timeoutMs: 500);

            Assert.AreEqual(500, client.TimeoutMs);

            client.TimeoutMs = 2_000;
            Assert.AreEqual(2_000, client.TimeoutMs);

            Assert.Throws<ArgumentOutOfRangeException>(() => client.TimeoutMs = 0);
        }
    }
}
