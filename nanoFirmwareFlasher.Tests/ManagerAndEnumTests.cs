// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using nanoFramework.Tools.FirmwareFlasher;

namespace nanoFirmwareFlasher.Tests
{
    /// <summary>
    /// Tests for manager constructor validation, enum definitions,
    /// and ExitCodes Display attributes — no hardware required.
    /// </summary>
    [TestClass]
    public class ManagerAndEnumTests
    {
        #region TIManager constructor validation

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void TIManager_NullOptions_ThrowsArgumentNull()
        {
            new TIManager(null, VerbosityLevel.Normal);
        }

        [TestMethod]
        [ExpectedException(typeof(NotSupportedException))]
        public void TIManager_WrongPlatform_ThrowsNotSupported()
        {
            var options = new Options { Platform = SupportedPlatform.esp32 };
            new TIManager(options, VerbosityLevel.Normal);
        }

        [TestMethod]
        public void TIManager_CorrectPlatform_CreatesSuccessfully()
        {
            var options = new Options { Platform = SupportedPlatform.ti_simplelink };
            var manager = new TIManager(options, VerbosityLevel.Normal);
            Assert.IsNotNull(manager);
        }

        #endregion

        #region Esp32Manager constructor validation

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Esp32Manager_NullOptions_ThrowsArgumentNull()
        {
            new Esp32Manager(null, VerbosityLevel.Normal);
        }

        [TestMethod]
        [ExpectedException(typeof(NotSupportedException))]
        public void Esp32Manager_WrongPlatform_ThrowsNotSupported()
        {
            var options = new Options { Platform = SupportedPlatform.stm32 };
            new Esp32Manager(options, VerbosityLevel.Normal);
        }

        [TestMethod]
        public void Esp32Manager_CorrectPlatform_CreatesSuccessfully()
        {
            var options = new Options { Platform = SupportedPlatform.esp32 };
            var manager = new Esp32Manager(options, VerbosityLevel.Normal);
            Assert.IsNotNull(manager);
        }

        #endregion

        #region Stm32Manager constructor validation

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Stm32Manager_NullOptions_ThrowsArgumentNull()
        {
            new Stm32Manager(null, VerbosityLevel.Normal);
        }

        [TestMethod]
        [ExpectedException(typeof(NotSupportedException))]
        public void Stm32Manager_WrongPlatform_ThrowsNotSupported()
        {
            var options = new Options { Platform = SupportedPlatform.esp32 };
            new Stm32Manager(options, VerbosityLevel.Normal);
        }

        [TestMethod]
        public void Stm32Manager_CorrectPlatform_CreatesSuccessfully()
        {
            var options = new Options { Platform = SupportedPlatform.stm32 };
            var manager = new Stm32Manager(options, VerbosityLevel.Normal);
            Assert.IsNotNull(manager);
        }

        #endregion

        #region SupportedPlatform enum

        [TestMethod]
        public void SupportedPlatform_HasExpectedValues()
        {
            Assert.IsTrue(Enum.IsDefined(typeof(SupportedPlatform), "esp32"));
            Assert.IsTrue(Enum.IsDefined(typeof(SupportedPlatform), "stm32"));
            Assert.IsTrue(Enum.IsDefined(typeof(SupportedPlatform), "ti_simplelink"));
            Assert.IsTrue(Enum.IsDefined(typeof(SupportedPlatform), "efm32"));
        }

        [TestMethod]
        public void SupportedPlatform_esp32_IsZero()
        {
            Assert.AreEqual(0, (int)SupportedPlatform.esp32);
        }

        [TestMethod]
        public void SupportedPlatform_stm32_IsOne()
        {
            Assert.AreEqual(1, (int)SupportedPlatform.stm32);
        }

        #endregion

        #region VerbosityLevel enum

        [TestMethod]
        public void VerbosityLevel_HasExpectedValues()
        {
            Assert.AreEqual(0, (int)VerbosityLevel.Quiet);
            Assert.AreEqual(1, (int)VerbosityLevel.Minimal);
            Assert.AreEqual(2, (int)VerbosityLevel.Normal);
            Assert.AreEqual(3, (int)VerbosityLevel.Detailed);
            Assert.AreEqual(4, (int)VerbosityLevel.Diagnostic);
        }

        [TestMethod]
        public void VerbosityLevel_Count()
        {
            Assert.AreEqual(5, Enum.GetValues(typeof(VerbosityLevel)).Length);
        }

        #endregion

        #region PartitionTableSize enum

        [TestMethod]
        public void PartitionTableSize_HasExpectedValues()
        {
            Assert.AreEqual(2, (int)PartitionTableSize._2);
            Assert.AreEqual(4, (int)PartitionTableSize._4);
            Assert.AreEqual(8, (int)PartitionTableSize._8);
            Assert.AreEqual(16, (int)PartitionTableSize._16);
        }

        #endregion

        #region ExitCodes — Display attributes

        [TestMethod]
        public void ExitCodes_OK_HasDisplayAttribute()
        {
            var field = typeof(ExitCodes).GetField(nameof(ExitCodes.OK));
            var display = field.GetCustomAttribute<DisplayAttribute>();
            Assert.IsNotNull(display, "ExitCodes.OK should have a Display attribute");
        }

        [TestMethod]
        public void ExitCodes_AllValues_HaveDisplayAttribute()
        {
            var values = Enum.GetValues(typeof(ExitCodes));
            int missingCount = 0;

            foreach (ExitCodes code in values)
            {
                var field = typeof(ExitCodes).GetField(code.ToString());
                var display = field?.GetCustomAttribute<DisplayAttribute>();

                if (display == null)
                {
                    missingCount++;
                }
            }

            Assert.AreEqual(0, missingCount, $"{missingCount} ExitCodes values are missing Display attributes");
        }

        [TestMethod]
        public void ExitCodes_E1000_SuggestsNativeAlternatives()
        {
            var field = typeof(ExitCodes).GetField(nameof(ExitCodes.E1000));
            var display = field.GetCustomAttribute<DisplayAttribute>();
            Assert.IsNotNull(display);

            string name = display.Name;
            Assert.IsTrue(
                name.Contains("nativedfu") || name.Contains("uart"),
                "E1000 should suggest native alternatives");
        }

        [TestMethod]
        public void ExitCodes_E5001_SuggestsNativeAlternatives()
        {
            var field = typeof(ExitCodes).GetField(nameof(ExitCodes.E5001));
            var display = field.GetCustomAttribute<DisplayAttribute>();
            Assert.IsNotNull(display);

            string name = display.Name;
            Assert.IsTrue(
                name.Contains("nativestlink") || name.Contains("nativeswd"),
                "E5001 should suggest native alternatives");
        }

        #endregion

        #region IManager interface

        [TestMethod]
        public void IManager_ImplementedByAllManagers()
        {
            Assert.IsTrue(typeof(IManager).IsAssignableFrom(typeof(Esp32Manager)));
            Assert.IsTrue(typeof(IManager).IsAssignableFrom(typeof(Stm32Manager)));
            Assert.IsTrue(typeof(IManager).IsAssignableFrom(typeof(TIManager)));
        }

        #endregion
    }
}
