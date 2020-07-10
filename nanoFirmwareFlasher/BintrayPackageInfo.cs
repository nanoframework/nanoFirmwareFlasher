//
// Copyright (c) 2019 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using Newtonsoft.Json;
using System;

namespace nanoFramework.Tools.FirmwareFlasher
{
    [Serializable]
    internal class BintrayPackageInfo
    {
        [JsonProperty("latest_version")]
        public string LatestVersion { get; set; }
    }
}
