// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
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
                s_runRoot ??= Path.Combine(context.ResultsDirectory!, RootFolderName, s_runId);
                string path = Path.Combine(s_runRoot, s_lastIndex.ToString());
                Debug.WriteLine($"Test directory: {path}");

                // always hand out an empty directory
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }

                Directory.CreateDirectory(path);

                FirmwarePackage.LocationPathBase = Path.Combine(path, LocationPathBase_RelativePath);

                return path;
            }
        }

        /// <summary>
        /// Removes the directories created by this test run, and stale ones left behind by earlier runs.
        /// </summary>
        public static void Cleanup()
        {
            lock (typeof(TestDirectoryHelper))
            {
                if (s_runRoot is null)
                {
                    return;
                }

                TryDelete(s_runRoot);

                string parent = Path.GetDirectoryName(s_runRoot)!;

                string[] dirs;

                try
                {
                    dirs = Directory.Exists(parent) ? Directory.GetDirectories(parent) : Array.Empty<string>();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Could not list '{parent}': {ex.Message}");
                    return;
                }

                foreach (string dir in dirs)
                {
                    try
                    {
                        // only touch old folders so concurrent test processes are not affected
                        if (Directory.GetLastWriteTimeUtc(dir) < DateTime.UtcNow.AddDays(-1))
                        {
                            TryDelete(dir);
                        }
                    }
                    catch (Exception ex)
                    {
                        // e.g. removed meanwhile by another test process
                        Debug.WriteLine($"Could not check '{dir}': {ex.Message}");
                    }
                }
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Could not delete '{path}': {ex.Message}");
            }
        }

        private const string RootFolderName = "nanoff";

        // unique per test process so separate runs never share (or find leftovers in) the same folders
        private static readonly string s_runId = $"{Environment.ProcessId}-{Guid.NewGuid():N}";
        private static string? s_runRoot;
        private static int s_lastIndex;
    }
}
