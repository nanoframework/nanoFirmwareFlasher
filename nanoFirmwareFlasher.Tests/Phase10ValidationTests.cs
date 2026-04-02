// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using nanoFramework.Tools.FirmwareFlasher;
using nanoFramework.Tools.FirmwareFlasher.Swd;

namespace nanoFirmwareFlasher.Tests
{
    /// <summary>
    /// Phase 10 tests: option validation, OutputWriter thread safety,
    /// UART VerifyMemoryBlock, SWD Verify, and the new E5022 exit code.
    /// </summary>
    [TestClass]
    public class Phase10ValidationTests
    {
        #region Options.ValidateInterfaceOptions tests

        [TestMethod]
        public void ValidateInterface_NoFlags_ReturnsNull()
        {
            var o = new Options();
            Assert.IsNull(Options.ValidateInterfaceOptions(o));
        }

        [TestMethod]
        public void ValidateInterface_SingleFlag_Jtag_ReturnsNull()
        {
            var o = new Options { JtagUpdate = true };
            Assert.IsNull(Options.ValidateInterfaceOptions(o));
        }

        [TestMethod]
        public void ValidateInterface_SingleFlag_Dfu_ReturnsNull()
        {
            var o = new Options { DfuUpdate = true };
            Assert.IsNull(Options.ValidateInterfaceOptions(o));
        }

        [TestMethod]
        public void ValidateInterface_SingleFlag_NativeDfu_ReturnsNull()
        {
            var o = new Options { NativeDfuUpdate = true };
            Assert.IsNull(Options.ValidateInterfaceOptions(o));
        }

        [TestMethod]
        public void ValidateInterface_SingleFlag_NativeSwd_ReturnsNull()
        {
            var o = new Options { NativeSwdUpdate = true };
            Assert.IsNull(Options.ValidateInterfaceOptions(o));
        }

        [TestMethod]
        public void ValidateInterface_SingleFlag_NativeStLink_ReturnsNull()
        {
            var o = new Options { NativeStLinkUpdate = true };
            Assert.IsNull(Options.ValidateInterfaceOptions(o));
        }

        [TestMethod]
        public void ValidateInterface_TwoFlags_JtagAndDfu_ReturnsError()
        {
            var o = new Options { JtagUpdate = true, DfuUpdate = true };
            string error = Options.ValidateInterfaceOptions(o);
            Assert.IsNotNull(error);
            StringAssert.Contains(error, "--jtag");
            StringAssert.Contains(error, "--dfu");
        }

        [TestMethod]
        public void ValidateInterface_AllSixFlags_ReturnsError()
        {
            var o = new Options
            {
                JtagUpdate = true,
                DfuUpdate = true,
                NativeDfuUpdate = true,
                NativeSwdUpdate = true,
                NativeStLinkUpdate = true
            };
            string error = Options.ValidateInterfaceOptions(o);
            Assert.IsNotNull(error);
            StringAssert.Contains(error, "--jtag");
            StringAssert.Contains(error, "--dfu");
            StringAssert.Contains(error, "--nativedfu");
            StringAssert.Contains(error, "--nativeswd");
            StringAssert.Contains(error, "--nativestlink");
        }

        [TestMethod]
        public void ValidateInterface_NativeStLinkAndNativeDfu_ReturnsError()
        {
            var o = new Options { NativeStLinkUpdate = true, NativeDfuUpdate = true };
            string error = Options.ValidateInterfaceOptions(o);
            Assert.IsNotNull(error);
            StringAssert.Contains(error, "--nativestlink");
            StringAssert.Contains(error, "--nativedfu");
        }

        #endregion

        #region --verify flag defaults

        [TestMethod]
        public void VerifyOption_DefaultIsFalse()
        {
            var o = new Options();
            Assert.IsFalse(o.Verify);
        }

        [TestMethod]
        public void VerifyOption_CanBeSetToTrue()
        {
            var o = new Options { Verify = true };
            Assert.IsTrue(o.Verify);
        }

        #endregion

        #region E5022 exit code

        [TestMethod]
        public void ExitCode_E5022_HasDisplayAttribute()
        {
            var field = typeof(ExitCodes).GetField(nameof(ExitCodes.E5022));
            var attr = field.GetCustomAttribute<DisplayAttribute>();
            Assert.IsNotNull(attr);
            Assert.IsFalse(string.IsNullOrEmpty(attr.Name));
        }

        [TestMethod]
        public void ExitCode_E5022_ValueIs5022()
        {
            Assert.AreEqual(5022, (int)ExitCodes.E5022);
        }

        #endregion

        #region SWD Verify tests

        [TestMethod]
        public void Stm32FlashProgrammer_VerifyMethod_Exists()
        {
            var method = typeof(Stm32FlashProgrammer).GetMethod(
                "Verify",
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(uint), typeof(byte[]), typeof(int), typeof(int) },
                null);
            Assert.IsNotNull(method, "Verify method should exist on Stm32FlashProgrammer");
            Assert.IsTrue(method.ReturnType == typeof(bool), "Verify should return bool");
        }

        [TestMethod]
        public void StmSwdDevice_VerifyProperty_Exists()
        {
            var prop = typeof(StmSwdDevice).GetProperty("Verify");
            Assert.IsNotNull(prop, "Verify property should exist on StmSwdDevice");
            Assert.AreEqual(typeof(bool), prop.PropertyType);
        }

        [TestMethod]
        public void StmStLinkDevice_VerifyProperty_Exists()
        {
            var prop = typeof(StmStLinkDevice).GetProperty("Verify");
            Assert.IsNotNull(prop, "Verify property should exist on StmStLinkDevice");
            Assert.AreEqual(typeof(bool), prop.PropertyType);
        }

        #endregion

        #region OutputWriter thread safety tests

        [TestMethod]
        public void OutputWriter_ConsoleLock_Exists()
        {
            // The private static s_consoleLock field must exist
            var field = typeof(OutputWriter).GetField(
                "s_consoleLock",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "s_consoleLock field should exist on OutputWriter");
            Assert.IsNotNull(field.GetValue(null), "s_consoleLock should not be null");
        }

        [TestMethod]
        public void OutputWriter_ConcurrentWrites_NoExceptions()
        {
            // Verify that concurrent OutputWriter calls don't throw when using test writer
            var exceptions = new List<Exception>();
            var barrier = new ManualResetEventSlim(false);

            var tasks = new Task[10];

            for (int i = 0; i < tasks.Length; i++)
            {
                int index = i;
                tasks[i] = Task.Run(() =>
                {
                    var writer = new TestOutputWriter();
                    OutputWriter.SetOutputWriter(writer);

                    barrier.Wait();

                    try
                    {
                        for (int j = 0; j < 100; j++)
                        {
                            OutputWriter.ForegroundColor = (ConsoleColor)(j % 16);
                            OutputWriter.Write($"Thread {index} message {j}");
                            OutputWriter.WriteLine($"Thread {index} line {j}");
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (exceptions)
                        {
                            exceptions.Add(ex);
                        }
                    }
                    finally
                    {
                        OutputWriter.SetOutputWriter(null);
                    }
                });
            }

            // Release all threads at once
            barrier.Set();
            Task.WaitAll(tasks);

            Assert.AreEqual(0, exceptions.Count, $"Concurrent writes threw exceptions: {(exceptions.Count > 0 ? exceptions[0].Message : "")}");
        }

        [TestMethod]
        public void OutputWriter_TestWriter_IsolatesOutput()
        {
            // Verify that SetOutputWriter properly isolates output per async context
            var writer1 = new TestOutputWriter();
            var writer2 = new TestOutputWriter();

            var task1 = Task.Run(() =>
            {
                OutputWriter.SetOutputWriter(writer1);
                OutputWriter.Write("hello from task 1");
                OutputWriter.SetOutputWriter(null);
            });

            var task2 = Task.Run(() =>
            {
                OutputWriter.SetOutputWriter(writer2);
                OutputWriter.Write("hello from task 2");
                OutputWriter.SetOutputWriter(null);
            });

            Task.WaitAll(task1, task2);

            Assert.IsTrue(writer1.Output.Contains("hello from task 1"));
            Assert.IsTrue(writer2.Output.Contains("hello from task 2"));
            Assert.IsFalse(writer1.Output.Contains("hello from task 2"));
            Assert.IsFalse(writer2.Output.Contains("hello from task 1"));
        }

        private class TestOutputWriter : OutputWriter.IOutputWriter
        {
            private readonly object _lock = new object();
            private string _output = "";

            public string Output
            {
                get { lock (_lock) { return _output; } }
            }

            public ConsoleColor ForegroundColor { get; set; }

            public void Write(string text)
            {
                lock (_lock)
                {
                    _output += text ?? "";
                }
            }
        }

        #endregion

        #region Stm32Operations.UpdateFirmwareAsync verify parameter

        [TestMethod]
        public void Stm32Operations_UpdateFirmwareAsync_HasVerifyParameter()
        {
            var method = typeof(Stm32Operations).GetMethod(
                "UpdateFirmwareAsync",
                BindingFlags.Static | BindingFlags.Public);
            Assert.IsNotNull(method);

            var parameters = method.GetParameters();
            bool foundVerify = false;

            foreach (var p in parameters)
            {
                if (p.Name == "verify" && p.ParameterType == typeof(bool))
                {
                    foundVerify = true;
                    Assert.IsTrue(p.HasDefaultValue, "verify parameter should have a default value");
                    Assert.AreEqual(false, p.DefaultValue, "verify default should be false");
                }
            }

            Assert.IsTrue(foundVerify, "UpdateFirmwareAsync should have a 'verify' parameter");
        }

        #endregion
    }
}
