//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System.Runtime.InteropServices;

namespace nanoFramework.Tools.FirmwareFlasher.UsbDfu
{
    /// <summary>
    /// USB DFU 1.1 protocol constants and DfuSe (ST extensions) constants.
    /// </summary>
    internal static class DfuConst
    {
        // STM32 DFU device USB identifiers
        internal const ushort StmVendorId = 0x0483;
        internal const ushort StmDfuProductId = 0xDF11;

        // DFU class request types (USB control transfer bmRequestType)
        internal const byte DfuRequestOut = 0x21; // Host→Device, Class, Interface
        internal const byte DfuRequestIn = 0xA1;  // Device→Host, Class, Interface

        // DFU 1.1 request codes (bRequest)
        internal const byte DfuDetach = 0;
        internal const byte DfuDnload = 1;
        internal const byte DfuUpload = 2;
        internal const byte DfuGetStatus = 3;
        internal const byte DfuClrStatus = 4;
        internal const byte DfuGetState = 5;
        internal const byte DfuAbort = 6;

        // DfuSe special command prefixes (sent via DFU_DNLOAD with wBlockNum=0)
        internal const byte DfuSeSetAddress = 0x21;
        internal const byte DfuSeErase = 0x41;
        internal const byte DfuSeReadUnprotect = 0x92;

        // DfuSe: data blocks start at wBlockNum=2
        internal const ushort DfuSeDataBlockOffset = 2;

        // Default transfer size for STM32 DFU (bytes per block)
        internal const int DefaultTransferSize = 2048;

        // Timeouts (milliseconds)
        internal const int ControlTransferTimeout = 5000;
        internal const int EraseTimeout = 30000;
        internal const int StatusPollInterval = 50;
        internal const int MaxStatusPolls = 600; // 30 seconds at 50ms intervals
    }

    /// <summary>
    /// DFU device states as defined in USB DFU 1.1 specification.
    /// </summary>
    internal enum DfuState : byte
    {
        AppIdle = 0,
        AppDetach = 1,
        DfuIdle = 2,
        DfuDnloadSync = 3,
        DfuDnbusy = 4,
        DfuDnloadIdle = 5,
        DfuManifestSync = 6,
        DfuManifest = 7,
        DfuManifestWaitReset = 8,
        DfuUploadIdle = 9,
        DfuError = 10,
    }

    /// <summary>
    /// DFU status codes as defined in USB DFU 1.1 specification.
    /// </summary>
    internal enum DfuStatus : byte
    {
        Ok = 0x00,
        ErrTarget = 0x01,
        ErrFile = 0x02,
        ErrWrite = 0x03,
        ErrErase = 0x04,
        ErrCheckErased = 0x05,
        ErrProg = 0x06,
        ErrVerify = 0x07,
        ErrAddress = 0x08,
        ErrNotDone = 0x09,
        ErrFirmware = 0x0A,
        ErrVendor = 0x0B,
        ErrUsbr = 0x0C,
        ErrPor = 0x0D,
        ErrUnknown = 0x0E,
        ErrStalledPkt = 0x0F,
    }

    /// <summary>
    /// Parsed DFU_GETSTATUS response (6 bytes).
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct DfuStatusResult
    {
        /// <summary>Status code indicating the result of the last operation.</summary>
        internal DfuStatus Status;

        /// <summary>Poll timeout byte 0 (LSB) — minimum time in ms to wait before next GetStatus.</summary>
        internal byte PollTimeoutLow;

        /// <summary>Poll timeout byte 1.</summary>
        internal byte PollTimeoutMid;

        /// <summary>Poll timeout byte 2 (MSB).</summary>
        internal byte PollTimeoutHigh;

        /// <summary>Current DFU state.</summary>
        internal DfuState State;

        /// <summary>Index of status description in string table.</summary>
        internal byte StringIndex;

        /// <summary>Gets the poll timeout in milliseconds.</summary>
        internal int PollTimeout => PollTimeoutLow | (PollTimeoutMid << 8) | (PollTimeoutHigh << 16);
    }
}
