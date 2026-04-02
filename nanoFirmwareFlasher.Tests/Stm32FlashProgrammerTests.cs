//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using nanoFramework.Tools.FirmwareFlasher.Swd;

namespace nanoFirmwareFlasher.Tests
{
    /// <summary>
    /// Tests for the Stm32FlashProgrammer: device ID classification,
    /// flash register maps, family enumeration, and sector/page calculations.
    /// All tests use reflection to access internal members â€” no hardware required.
    /// </summary>
    [TestClass]
    public class Stm32FlashProgrammerTests
    {
        // Use reflection to call internal static ClassifyDevice(ushort devId)
        private static readonly MethodInfo ClassifyDeviceMethod =
            typeof(Stm32FlashProgrammer).GetMethod(
                "ClassifyDevice",
                BindingFlags.Static | BindingFlags.NonPublic);

        // Use reflection to call internal static GetFlashRegisters(Stm32Family family)
        private static readonly MethodInfo GetFlashRegistersMethod =
            typeof(Stm32FlashProgrammer).GetMethod(
                "GetFlashRegisters",
                BindingFlags.Static | BindingFlags.NonPublic);

        // Use reflection to call internal static GetSectorForAddress(uint offset)
        private static readonly MethodInfo GetSectorForAddressMethod =
            typeof(Stm32FlashProgrammer).GetMethod(
                "GetSectorForAddress",
                BindingFlags.Static | BindingFlags.NonPublic);

        // Helper to get the Stm32Family enum type
        private static readonly Type FamilyEnumType =
            typeof(Stm32FlashProgrammer).GetNestedType("Stm32Family", BindingFlags.NonPublic);

        private static object ClassifyDevice(ushort devId)
        {
            return ClassifyDeviceMethod.Invoke(null, new object[] { devId });
        }

        private static object GetFlashRegisters(object family)
        {
            return GetFlashRegistersMethod.Invoke(null, new object[] { family });
        }

        private static int GetSectorForAddress(uint offset)
        {
            return (int)GetSectorForAddressMethod.Invoke(null, new object[] { offset });
        }

        private static object GetFamilyValue(string name)
        {
            return Enum.Parse(FamilyEnumType, name);
        }

        private static object GetRegField(object regs, string fieldName)
        {
            return regs.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(regs);
        }

        private static uint GetUintRegField(object regs, string fieldName)
        {
            return (uint)GetRegField(regs, fieldName);
        }

        private static int GetIntRegField(object regs, string fieldName)
        {
            return (int)GetRegField(regs, fieldName);
        }

        #region Family Enum

        [TestMethod]
        public void Stm32Family_HasExpectedValues()
        {
            var values = Enum.GetNames(FamilyEnumType);
            Assert.IsTrue(values.Length >= 16, $"Expected at least 16 family values, got {values.Length}");

            // Verify core families exist
            string[] expected = { "Unknown", "F0", "F1", "F4", "F7", "H7", "L0", "L4", "L5",
                                  "G0", "G4", "WB", "WL", "U5", "C0", "H5" };

            foreach (string name in expected)
            {
                Assert.IsTrue(
                    Enum.IsDefined(FamilyEnumType, Enum.Parse(FamilyEnumType, name)),
                    $"Stm32Family should have value '{name}'");
            }
        }

        #endregion

        #region ClassifyDevice â€” F0 family

        [TestMethod]
        [DataRow((ushort)0x440, "F0")]
        [DataRow((ushort)0x442, "F0")]
        [DataRow((ushort)0x444, "F0")]
        [DataRow((ushort)0x445, "F0")]
        [DataRow((ushort)0x448, "F0")]
        public void ClassifyDevice_F0_Family(ushort devId, string expectedFamily)
        {
            var result = ClassifyDevice(devId);
            Assert.AreEqual(expectedFamily, result.ToString());
        }

        #endregion

        #region ClassifyDevice â€” F1 family

        [TestMethod]
        [DataRow((ushort)0x410, "F1")]
        [DataRow((ushort)0x412, "F1")]
        [DataRow((ushort)0x414, "F1")]
        [DataRow((ushort)0x418, "F1")]
        [DataRow((ushort)0x420, "F1")]
        [DataRow((ushort)0x428, "F1")]
        public void ClassifyDevice_F1_Family(ushort devId, string expectedFamily)
        {
            var result = ClassifyDevice(devId);
            Assert.AreEqual(expectedFamily, result.ToString());
        }

        #endregion

        #region ClassifyDevice â€” F4 family

        [TestMethod]
        [DataRow((ushort)0x411, "F4")]
        [DataRow((ushort)0x413, "F4")]
        [DataRow((ushort)0x419, "F4")]
        [DataRow((ushort)0x421, "F4")]
        [DataRow((ushort)0x423, "F4")]
        [DataRow((ushort)0x431, "F4")]
        [DataRow((ushort)0x433, "F4")]
        [DataRow((ushort)0x434, "F4")]
        [DataRow((ushort)0x441, "F4")]
        [DataRow((ushort)0x458, "F4")]
        [DataRow((ushort)0x463, "F4")]
        public void ClassifyDevice_F4_Family(ushort devId, string expectedFamily)
        {
            var result = ClassifyDevice(devId);
            Assert.AreEqual(expectedFamily, result.ToString());
        }

        #endregion

        #region ClassifyDevice â€” F7 family

        [TestMethod]
        [DataRow((ushort)0x449, "F7")]
        [DataRow((ushort)0x451, "F7")]
        [DataRow((ushort)0x452, "F7")]
        public void ClassifyDevice_F7_Family(ushort devId, string expectedFamily)
        {
            var result = ClassifyDevice(devId);
            Assert.AreEqual(expectedFamily, result.ToString());
        }

        #endregion

        #region ClassifyDevice â€” H7 family

        [TestMethod]
        [DataRow((ushort)0x450, "H7")]
        [DataRow((ushort)0x480, "H7")]
        [DataRow((ushort)0x483, "H7")]
        public void ClassifyDevice_H7_Family(ushort devId, string expectedFamily)
        {
            var result = ClassifyDevice(devId);
            Assert.AreEqual(expectedFamily, result.ToString());
        }

        #endregion

        #region ClassifyDevice â€” L0 family

        [TestMethod]
        [DataRow((ushort)0x417, "L0")]
        [DataRow((ushort)0x425, "L0")]
        [DataRow((ushort)0x447, "L0")]
        [DataRow((ushort)0x457, "L0")]
        public void ClassifyDevice_L0_Family(ushort devId, string expectedFamily)
        {
            var result = ClassifyDevice(devId);
            Assert.AreEqual(expectedFamily, result.ToString());
        }

        #endregion

        #region ClassifyDevice â€” L4 family

        [TestMethod]
        [DataRow((ushort)0x415, "L4")]
        [DataRow((ushort)0x435, "L4")]
        [DataRow((ushort)0x461, "L4")]
        [DataRow((ushort)0x462, "L4")]
        [DataRow((ushort)0x464, "L4")]
        [DataRow((ushort)0x470, "L4")]
        [DataRow((ushort)0x471, "L4")]
        public void ClassifyDevice_L4_Family(ushort devId, string expectedFamily)
        {
            var result = ClassifyDevice(devId);
            Assert.AreEqual(expectedFamily, result.ToString());
        }

        #endregion

        #region ClassifyDevice â€” G0 family

        [TestMethod]
        [DataRow((ushort)0x456, "G0")]
        [DataRow((ushort)0x460, "G0")]
        [DataRow((ushort)0x466, "G0")]
        [DataRow((ushort)0x467, "G0")]
        public void ClassifyDevice_G0_Family(ushort devId, string expectedFamily)
        {
            var result = ClassifyDevice(devId);
            Assert.AreEqual(expectedFamily, result.ToString());
        }

        #endregion

        #region ClassifyDevice â€” G4 family

        [TestMethod]
        [DataRow((ushort)0x468, "G4")]
        [DataRow((ushort)0x469, "G4")]
        [DataRow((ushort)0x479, "G4")]
        public void ClassifyDevice_G4_Family(ushort devId, string expectedFamily)
        {
            var result = ClassifyDevice(devId);
            Assert.AreEqual(expectedFamily, result.ToString());
        }

        #endregion

        #region ClassifyDevice â€” WB/WL families

        [TestMethod]
        [DataRow((ushort)0x495, "WB")]
        [DataRow((ushort)0x496, "WB")]
        public void ClassifyDevice_WB_Family(ushort devId, string expectedFamily)
        {
            var result = ClassifyDevice(devId);
            Assert.AreEqual(expectedFamily, result.ToString());
        }

        [TestMethod]
        public void ClassifyDevice_WL_Family()
        {
            Assert.AreEqual("WL", ClassifyDevice(0x497).ToString());
        }

        #endregion

        #region ClassifyDevice â€” L5 family

        [TestMethod]
        public void ClassifyDevice_L5_Family()
        {
            Assert.AreEqual("L5", ClassifyDevice(0x472).ToString());
        }

        #endregion

        #region ClassifyDevice â€” U5 family

        [TestMethod]
        [DataRow((ushort)0x455, "U5")]
        [DataRow((ushort)0x476, "U5")]
        [DataRow((ushort)0x481, "U5")]
        [DataRow((ushort)0x482, "U5")]
        public void ClassifyDevice_U5_Family(ushort devId, string expectedFamily)
        {
            var result = ClassifyDevice(devId);
            Assert.AreEqual(expectedFamily, result.ToString());
        }

        #endregion

        #region ClassifyDevice â€” C0 family

        [TestMethod]
        [DataRow((ushort)0x443, "C0")]
        [DataRow((ushort)0x453, "C0")]
        public void ClassifyDevice_C0_Family(ushort devId, string expectedFamily)
        {
            var result = ClassifyDevice(devId);
            Assert.AreEqual(expectedFamily, result.ToString());
        }

        #endregion

        #region ClassifyDevice â€” H5 family

        [TestMethod]
        [DataRow((ushort)0x474, "H5")]
        [DataRow((ushort)0x478, "H5")]
        [DataRow((ushort)0x484, "H5")]
        public void ClassifyDevice_H5_Family(ushort devId, string expectedFamily)
        {
            var result = ClassifyDevice(devId);
            Assert.AreEqual(expectedFamily, result.ToString());
        }

        #endregion

        #region ClassifyDevice â€” Unknown

        [TestMethod]
        [DataRow((ushort)0x000)]
        [DataRow((ushort)0xFFF)]
        [DataRow((ushort)0x999)]
        public void ClassifyDevice_Unknown_ForInvalidId(ushort devId)
        {
            Assert.AreEqual("Unknown", ClassifyDevice(devId).ToString());
        }

        #endregion

        #region ClassifyDevice â€” Completeness

        [TestMethod]
        public void ClassifyDevice_AllKnownIds_AreNotUnknown()
        {
            // Complete list of all known STM32 device IDs
            ushort[] knownIds =
            {
                // F0
                0x440, 0x442, 0x444, 0x445, 0x448,
                // F1
                0x410, 0x412, 0x414, 0x418, 0x420, 0x428,
                // F4
                0x411, 0x413, 0x419, 0x421, 0x423, 0x431, 0x433, 0x434, 0x441, 0x458, 0x463,
                // F7
                0x449, 0x451, 0x452,
                // H7
                0x450, 0x480, 0x483,
                // L0
                0x417, 0x425, 0x447, 0x457,
                // L4
                0x415, 0x435, 0x461, 0x462, 0x464, 0x470, 0x471,
                // G0
                0x456, 0x460, 0x466, 0x467,
                // G4
                0x468, 0x469, 0x479,
                // WB
                0x495, 0x496,
                // WL
                0x497,
                // L5
                0x472,
                // U5
                0x455, 0x476, 0x481, 0x482,
                // C0
                0x443, 0x453,
                // H5
                0x474, 0x478, 0x484,
            };

            int unknownCount = 0;

            foreach (ushort id in knownIds)
            {
                var result = ClassifyDevice(id).ToString();

                if (result == "Unknown")
                {
                    unknownCount++;
                }
            }

            Assert.AreEqual(0, unknownCount, $"{unknownCount} known device IDs classified as Unknown");
        }

        [TestMethod]
        public void ClassifyDevice_TotalKnownIds_Count()
        {
            // Verify the total number of supported device IDs
            int count = 0;

            for (ushort id = 0; id <= 0xFFF; id++)
            {
                if (ClassifyDevice(id).ToString() != "Unknown")
                {
                    count++;
                }
            }

            // 5 F0 + 6 F1 + 11 F4 + 3 F7 + 3 H7 + 4 L0 + 7 L4 + 4 G0 + 3 G4
            // + 2 WB + 1 WL + 1 L5 + 4 U5 + 2 C0 + 3 H5 = 59
            Assert.AreEqual(59, count, $"Expected 59 known device IDs, found {count}");
        }

        #endregion

        #region Flash Registers â€” F0/F1/L0 group

        [TestMethod]
        [DataRow("F0", 1024U)]
        [DataRow("F1", 1024U)]
        [DataRow("L0", 128U)]
        public void GetFlashRegisters_F0F1L0Group_CorrectValues(string familyName, uint expectedPageSize)
        {
            var family = GetFamilyValue(familyName);
            var regs = GetFlashRegisters(family);

            Assert.AreEqual(0x40022000U, GetUintRegField(regs, "FlashBase"));
            Assert.AreEqual(0x04U, GetUintRegField(regs, "KeyrOffset"));
            Assert.AreEqual(0x10U, GetUintRegField(regs, "CrOffset"));
            Assert.AreEqual(0x0CU, GetUintRegField(regs, "SrOffset"));
            Assert.AreEqual(1U << 0, GetUintRegField(regs, "PgBit"));
            Assert.AreEqual(1U << 1, GetUintRegField(regs, "SerBit"));
            Assert.AreEqual(1U << 2, GetUintRegField(regs, "MerBit"));
            Assert.AreEqual(1U << 6, GetUintRegField(regs, "StrtBit"));
            Assert.AreEqual(1U << 7, GetUintRegField(regs, "LockBit"));
            Assert.AreEqual(expectedPageSize, GetUintRegField(regs, "PageSize"));
        }

        #endregion

        #region Flash Registers â€” F4/F7 group

        [TestMethod]
        [DataRow("F4")]
        [DataRow("F7")]
        public void GetFlashRegisters_F4F7Group_CorrectValues(string familyName)
        {
            var family = GetFamilyValue(familyName);
            var regs = GetFlashRegisters(family);

            Assert.AreEqual(0x40023C00U, GetUintRegField(regs, "FlashBase"));
            Assert.AreEqual(0x04U, GetUintRegField(regs, "KeyrOffset"));
            Assert.AreEqual(0x10U, GetUintRegField(regs, "CrOffset"));
            Assert.AreEqual(0x0CU, GetUintRegField(regs, "SrOffset"));
            Assert.AreEqual(1U << 16, GetUintRegField(regs, "StrtBit"));
            Assert.AreEqual(1U << 31, GetUintRegField(regs, "LockBit"));
            Assert.AreEqual(3, GetIntRegField(regs, "SectorShift"));
            Assert.AreEqual(0U, GetUintRegField(regs, "PageSize")); // sector-based
        }

        #endregion

        #region Flash Registers â€” H7 group

        [TestMethod]
        public void GetFlashRegisters_H7_CorrectValues()
        {
            var regs = GetFlashRegisters(GetFamilyValue("H7"));

            Assert.AreEqual(0x52002000U, GetUintRegField(regs, "FlashBase"));
            Assert.AreEqual(0x04U, GetUintRegField(regs, "KeyrOffset"));
            Assert.AreEqual(0x0CU, GetUintRegField(regs, "CrOffset"));
            Assert.AreEqual(0x10U, GetUintRegField(regs, "SrOffset"));
            Assert.AreEqual(1U << 1, GetUintRegField(regs, "PgBit"));
            Assert.AreEqual(1U << 2, GetUintRegField(regs, "SerBit"));
            Assert.AreEqual(1U << 3, GetUintRegField(regs, "MerBit"));
            Assert.AreEqual(1U << 7, GetUintRegField(regs, "StrtBit"));
            Assert.AreEqual(1U << 0, GetUintRegField(regs, "LockBit"));
            Assert.AreEqual(8, GetIntRegField(regs, "SectorShift"));
            Assert.AreEqual(0U, GetUintRegField(regs, "PageSize")); // 128KB sectors
        }

        #endregion

        #region Flash Registers â€” L4/G0/G4/WB/WL/L5/U5/C0 group

        [TestMethod]
        [DataRow("L4", 4096U)]
        [DataRow("G0", 2048U)]
        [DataRow("G4", 4096U)]
        [DataRow("WB", 4096U)]
        [DataRow("WL", 4096U)]
        [DataRow("L5", 4096U)]
        [DataRow("U5", 4096U)]
        [DataRow("C0", 2048U)]
        public void GetFlashRegisters_L4Group_CorrectValues(string familyName, uint expectedPageSize)
        {
            var family = GetFamilyValue(familyName);
            var regs = GetFlashRegisters(family);

            Assert.AreEqual(0x40022000U, GetUintRegField(regs, "FlashBase"));
            Assert.AreEqual(0x08U, GetUintRegField(regs, "KeyrOffset"));
            Assert.AreEqual(0x14U, GetUintRegField(regs, "CrOffset"));
            Assert.AreEqual(0x10U, GetUintRegField(regs, "SrOffset"));
            Assert.AreEqual(1U << 0, GetUintRegField(regs, "PgBit"));
            Assert.AreEqual(1U << 1, GetUintRegField(regs, "SerBit"));
            Assert.AreEqual(1U << 2, GetUintRegField(regs, "MerBit"));
            Assert.AreEqual(1U << 16, GetUintRegField(regs, "StrtBit"));
            Assert.AreEqual(1U << 31, GetUintRegField(regs, "LockBit"));
            Assert.AreEqual(expectedPageSize, GetUintRegField(regs, "PageSize"));
        }

        #endregion

        #region Flash Registers â€” H5 group

        [TestMethod]
        public void GetFlashRegisters_H5_CorrectValues()
        {
            var regs = GetFlashRegisters(GetFamilyValue("H5"));

            Assert.AreEqual(0x40022000U, GetUintRegField(regs, "FlashBase"));
            Assert.AreEqual(0x04U, GetUintRegField(regs, "KeyrOffset"));
            Assert.AreEqual(0x10U, GetUintRegField(regs, "CrOffset"));
            Assert.AreEqual(0x20U, GetUintRegField(regs, "SrOffset"));
            Assert.AreEqual(1U << 1, GetUintRegField(regs, "PgBit"));
            Assert.AreEqual(1U << 2, GetUintRegField(regs, "SerBit"));
            Assert.AreEqual(1U << 3, GetUintRegField(regs, "MerBit"));
            Assert.AreEqual(1U << 7, GetUintRegField(regs, "StrtBit"));
            Assert.AreEqual(1U << 0, GetUintRegField(regs, "LockBit"));
            Assert.AreEqual(8, GetIntRegField(regs, "SectorShift"));
            Assert.AreEqual(0U, GetUintRegField(regs, "PageSize")); // 8KB sectors
        }

        #endregion

        #region Flash Registers â€” Unknown throws

        [TestMethod]
        [ExpectedException(typeof(TargetInvocationException))]
        public void GetFlashRegisters_Unknown_Throws()
        {
            GetFlashRegisters(GetFamilyValue("Unknown"));
        }

        #endregion

        #region GetSectorForAddress â€” F4/F7 variable sectors

        [TestMethod]
        public void GetSectorForAddress_Sector0_FirstByte()
        {
            Assert.AreEqual(0, GetSectorForAddress(0x0000));
        }

        [TestMethod]
        public void GetSectorForAddress_Sector0_LastByte()
        {
            Assert.AreEqual(0, GetSectorForAddress(0x3FFF));
        }

        [TestMethod]
        public void GetSectorForAddress_Sector1_FirstByte()
        {
            Assert.AreEqual(1, GetSectorForAddress(0x4000));
        }

        [TestMethod]
        public void GetSectorForAddress_Sector3_LastByte()
        {
            // Sectors 0-3 are 16KB each, so sector 3 ends at 0xFFFF
            Assert.AreEqual(3, GetSectorForAddress(0xFFFF));
        }

        [TestMethod]
        public void GetSectorForAddress_Sector4_FirstByte()
        {
            // Sector 4 is 64KB starting at offset 0x10000
            Assert.AreEqual(4, GetSectorForAddress(0x10000));
        }

        [TestMethod]
        public void GetSectorForAddress_Sector4_LastByte()
        {
            Assert.AreEqual(4, GetSectorForAddress(0x1FFFF));
        }

        [TestMethod]
        public void GetSectorForAddress_Sector5_FirstByte()
        {
            // Sectors 5+ are 128KB each starting at 0x20000
            Assert.AreEqual(5, GetSectorForAddress(0x20000));
        }

        [TestMethod]
        public void GetSectorForAddress_Sector6_FirstByte()
        {
            Assert.AreEqual(6, GetSectorForAddress(0x40000));
        }

        [TestMethod]
        public void GetSectorForAddress_Sector7_FirstByte()
        {
            Assert.AreEqual(7, GetSectorForAddress(0x60000));
        }

        #endregion

        #region Flash Registers â€” All families produce valid registers

        [TestMethod]
        public void GetFlashRegisters_AllNonUnknownFamilies_DoNotThrow()
        {
            string[] families = { "F0", "F1", "F4", "F7", "H7", "L0", "L4", "L5",
                                  "G0", "G4", "WB", "WL", "U5", "C0", "H5" };

            foreach (string familyName in families)
            {
                var regs = GetFlashRegisters(GetFamilyValue(familyName));

                // Basic sanity: FlashBase should be non-zero
                Assert.AreNotEqual(0U, GetUintRegField(regs, "FlashBase"),
                    $"{familyName} FlashBase should not be zero");

                // PgBit should be non-zero (every family needs programming enable)
                Assert.AreNotEqual(0U, GetUintRegField(regs, "PgBit"),
                    $"{familyName} PgBit should not be zero");

                // LockBit should be non-zero
                Assert.AreNotEqual(0U, GetUintRegField(regs, "LockBit"),
                    $"{familyName} LockBit should not be zero");
            }
        }

        #endregion

        #region DBGMCU constants

        [TestMethod]
        public void DbgmcuIdcode_Constants_Exist()
        {
            // Verify the DBGMCU IDCODE constants via reflection
            var m3m4 = typeof(Stm32FlashProgrammer).GetField(
                "DbgmcuIdcode_M3M4", BindingFlags.Static | BindingFlags.NonPublic);
            var m0 = typeof(Stm32FlashProgrammer).GetField(
                "DbgmcuIdcode_M0", BindingFlags.Static | BindingFlags.NonPublic);
            var m33 = typeof(Stm32FlashProgrammer).GetField(
                "DbgmcuIdcode_M33", BindingFlags.Static | BindingFlags.NonPublic);

            Assert.IsNotNull(m3m4, "DbgmcuIdcode_M3M4 constant should exist");
            Assert.IsNotNull(m0, "DbgmcuIdcode_M0 constant should exist");
            Assert.IsNotNull(m33, "DbgmcuIdcode_M33 constant should exist");

            Assert.AreEqual(0xE0042000U, (uint)m3m4.GetValue(null));
            Assert.AreEqual(0x40015800U, (uint)m0.GetValue(null));
            Assert.AreEqual(0x44024000U, (uint)m33.GetValue(null));
        }

        #endregion

        #region Flash Keys

        [TestMethod]
        public void FlashKeys_AreCorrect()
        {
            var key1 = typeof(Stm32FlashProgrammer).GetField(
                "FlashKey1", BindingFlags.Static | BindingFlags.NonPublic);
            var key2 = typeof(Stm32FlashProgrammer).GetField(
                "FlashKey2", BindingFlags.Static | BindingFlags.NonPublic);

            Assert.IsNotNull(key1);
            Assert.IsNotNull(key2);
            Assert.AreEqual(0x45670123U, (uint)key1.GetValue(null));
            Assert.AreEqual(0xCDEF89ABU, (uint)key2.GetValue(null));
        }

        #endregion
    }
}
