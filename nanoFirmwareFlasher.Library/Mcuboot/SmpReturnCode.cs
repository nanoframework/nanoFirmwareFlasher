// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace nanoFramework.Tools.FirmwareFlasher.Mcuboot
{
    /// <summary>SMP protocol return codes carried in the <c>rc</c> CBOR field of response payloads.</summary>
    internal enum SmpReturnCode
    {
        /// <summary>Success.</summary>
        Ok = 0,
        /// <summary>Unknown error.</summary>
        Unknown = 1,
        /// <summary>Insufficient memory.</summary>
        NoMemory = 2,
        /// <summary>Invalid value or argument.</summary>
        InvalidValue = 3,
        /// <summary>Operation timed out on the device.</summary>
        Timeout = 4,
        /// <summary>No such entry or resource.</summary>
        NoEntry = 5,
        /// <summary>Current state disallows the command.</summary>
        BadState = 6,
        /// <summary>Response payload too large for the transport.</summary>
        TooLarge = 7,
        /// <summary>Command or feature not supported by the device.</summary>
        NotSupported = 8,
        /// <summary>Corrupted data detected.</summary>
        Corrupted = 9,
        /// <summary>Device is busy and cannot service the request.</summary>
        Busy = 10,
        /// <summary>Access denied.</summary>
        AccessDenied = 11,
        /// <summary>Uploaded firmware version is too old for the current image.</summary>
        UnsupportedTooOld = 12,
        /// <summary>Uploaded firmware version is too new for the current image.</summary>
        UnsupportedTooNew = 13,
    }
}
