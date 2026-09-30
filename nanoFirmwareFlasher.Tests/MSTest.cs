// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using nanoFramework.Tools.FirmwareFlasher;

[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]

namespace nanoFirmwareFlasher.Tests
{
    [TestClass]
    public static class TestAssemblySetup
    {
        [AssemblyInitialize]
        public static void AssemblyInitialize(TestContext context)
        {
            // tests must never send telemetry, even when a real connection string is present in appsettings.json
            Environment.SetEnvironmentVariable(NanoTelemetry.OptOutEnvironmentVariable, "1");
        }
    }
}
