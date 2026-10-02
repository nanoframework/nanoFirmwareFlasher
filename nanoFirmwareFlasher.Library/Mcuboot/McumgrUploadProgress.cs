// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace nanoFramework.Tools.FirmwareFlasher.Mcuboot
{
    /// <summary>
    /// Reports progress during an SMP image or deployment upload.
    /// </summary>
    public class McumgrUploadProgress
    {
        /// <summary>Bytes successfully sent so far.</summary>
        public int BytesSent { get; set; }

        /// <summary>Total size of the data being uploaded in bytes.</summary>
        public int TotalBytes { get; set; }

        /// <summary>Upload completion percentage (0–100).</summary>
        public int PercentComplete => TotalBytes > 0 ? (int)((long)BytesSent * 100 / TotalBytes) : 0;
    }
}
