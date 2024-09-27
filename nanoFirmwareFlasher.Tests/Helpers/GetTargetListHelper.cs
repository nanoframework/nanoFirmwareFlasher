// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using nanoFramework.Tools.FirmwareFlasher;

namespace nanoFirmwareFlasher.Tests.Helpers
{
    internal static class GetTargetListHelper
    {
        internal static List<CloudSmithPackageDetail> GetTargetList(bool? community, bool preview, SupportedPlatform? platform, bool latestVersionOnly = true)
        {
            var allPackages = new List<CloudSmithPackageDetail>();

            if (community != true)
            {
                allPackages.AddRange(FirmwarePackage.GetTargetList(false, preview, platform, VerbosityLevel.Quiet));
            }
            if (community != false && !preview)
            {
                allPackages.AddRange(FirmwarePackage.GetTargetList(true, preview, platform, VerbosityLevel.Quiet));
            }
            if (allPackages.Count == 0)
            {
                Assert.Inconclusive("No packages available???");
            }

            if (latestVersionOnly)
            {
                var latestVersion = new Dictionary<string, (CloudSmithPackageDetail package, Version version)>();
                foreach (var package in allPackages)
                {
                    var thisVersion = new Version(package.Version);
                    if (!latestVersion.TryGetValue(package.Name, out (CloudSmithPackageDetail package, Version version) version)
                        || thisVersion > version.version)
                    {
                        latestVersion[package.Name] = (package, thisVersion);
                    }
                }
                return (from p in latestVersion.Values
                        select p.package).ToList();
            }
            else
            {
                return allPackages;
            }
        }
    }
}
