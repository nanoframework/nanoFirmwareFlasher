// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using nanoFramework.Tools.FirmwareFlasher;

namespace nanoFirmwareFlasher.Tests.Helpers
{
    internal static class TestDirectoryHelper
    {
        public const string LocationPathBase_RelativePath = "fw_cache";

        /// <summary>
        /// Get a test directory for the test. Also sets the <see cref="FirmwarePackage.LocationPathBase"/>.
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        public static string GetTestDirectory(TestContext context)
        {
            lock (typeof(TestDirectoryHelper))
            {
                s_lastIndex++;
                string path = Path.Combine(context.ResultsDirectory!, "nanoff", s_lastIndex.ToString());
                Debug.WriteLine($"Test directory: {path}");
                Directory.CreateDirectory(path);

                FirmwarePackage.LocationPathBase = Path.Combine(path, LocationPathBase_RelativePath);

                return path;
            }
        }
        private static int s_lastIndex;
    }
}
