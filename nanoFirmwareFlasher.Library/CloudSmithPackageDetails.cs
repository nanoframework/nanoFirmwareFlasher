// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Class with details of a CloudSmith package.
    /// </summary>
    public class CloudSmithPackageDetail
    {
        /// <summary>
        /// Package name.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Package version.
        /// </summary>
        public string Version { get; set; }

        /// <summary>
        /// Platform code
        /// </summary>
        public string Platform { get; set; }
    }
}
