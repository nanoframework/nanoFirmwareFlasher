//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System.ComponentModel.DataAnnotations;

namespace nanoFramework.Tools.FirmwareFlasher
{
    public enum ExitCodes : int
    {
        /// <summary>
        /// Execution terminated without any error
        /// </summary>
        [Display(Name = "")]
        OK = 0,

        ////////////////
        // DFU Errors //
        ////////////////

        /// <summary>
        /// No DFU device found
        /// </summary>
        [Display(Name = "No DFU device found. Make sure it's connected and has booted in DFU mode")]
        E1000 = 1000,

        /// <summary>
        /// DFU file doesn't exist
        /// </summary>
        [Display(Name = "Couldn't find DFU file. Check the path.")]
        E1002 = 1002,

        /// <summary>
        /// Error flashing DFU dvice
        /// </summary>
        [Display(Name = "Error flashing DFU dvice.")]
        E1003 = 1003,

        ////////////////////////
        // ESP32 tools Errors //
        ////////////////////////

        /// <summary>
        /// Error executing esptool command
        /// </summary>
        [Display(Name = "Error executing esptool command.")]
        E4000 = 4000,

        /// <summary>
        /// Unsupported flash size for ESP32 target.
        /// </summary>
        [Display(Name = "Unsupported flash size for ESP32 target.")]
        E4001 = 4001,

        /// <summary>
        /// Failed to erase ESP32 flash.
        /// </summary>
        [Display(Name = "Failed to erase ESP32 flash.")]
        E4002 = 4002,

        /// <summary>
        /// Failed to write new firmware to ESP32.
        /// </summary>
        [Display(Name = "Failed to write new firmware to ESP32.")]
        E4003 = 4003,

        /// <summary>
        /// Failed to read from ESP32 flash.
        /// </summary>
        [Display(Name = "Failed to read from ESP32 flash.")]
        E4004 = 4004,

        /// <summary>
        /// Can't open COM port.
        /// </summary>
        [Display(Name = "Failed to open specified COM port.")]
        E4005 = 4005,

        ////////////////////
        // ST Link Errors //
        ////////////////////

        /// <summary>
        /// Error executing ST Link CLI command.
        /// </summary>
        [Display(Name = "Error executing ST Link CLI command.")]
        E5000 = 5000,

        /// <summary>
        /// No JTAG device found
        /// </summary>
        [Display(Name = "No JTAG device found. Make sure it's connected")]
        E5001 = 5001,

        /// <summary>
        /// Can't connect to specified JTAG device.
        /// </summary>
        [Display(Name = "Can't connect to specified JTAG device. Make sure it's connected and that the ID is correct.")]
        E5002 = 5002,

        /// <summary>
        /// HEX file doesn't exist
        /// </summary>
        [Display(Name = "Couldn't find HEX file. Check the path.")]
        E5003 = 5003,

        /// <summary>
        /// BIN file doesn't exist
        /// </summary>
        [Display(Name = "Couldn't find BIN file. Check the path.")]
        E5004 = 5004,

        /// <summary>
        /// Failed to perform mass erase on device
        /// </summary>
        [Display(Name = "Failed to perform mass erase on device.")]
        E5005 = 5005,

        /// <summary>
        /// Failed to write new firmware to device.
        /// </summary>
        [Display(Name = "Failed to write new firmware to device.")]
        E5006 = 5006,

        /// <summary>
        /// Can't program BIN file without specifying an address
        /// </summary>
        [Display(Name = "Can't program BIN file without specifying an address.")]
        E5007 = 5007,

        /// <summary>
        /// Can't program BIN file without specifying an address
        /// </summary>
        [Display(Name = "Can't program BIN file without specifying an address.")]
        E5008 = 5008,

        /// <summary>
        ///Address count doesn't match BIN files count.
        /// </summary>
        [Display(Name = "Address count doesn't match BIN files count. An address needs to be specified for each BIN file.")]
        E5009 = 5009,

        /// <summary>
        /// Failed to reset MCU.
        /// </summary>
        [Display(Name = "Failed to reset MCU on connected device.")]
        E5010 = 5010,

        ////////////////
        // COM Errors //
        ////////////////

        /// <summary>
        /// Couldn't open serial device
        /// </summary>
        [Display(Name = "Couldn't open serial device. Make sure the COM port exists, that the device is connected and that it's not being used by another application.")]
        E6000 = 6000,

        /// <summary>
        /// Need to specify a COM port.
        /// </summary>
        [Display(Name = "Need to specify a COM port.")]
        E6001 = 6001,

        ///////////////
        // TI Errors //
        ///////////////

        /// <summary>
        /// Couldn't open serial device
        /// </summary>
        [Display(Name = "Unsupported device.")]
        E7000 = 7000,

        ////////////////////////////////
        // Application general Errors //
        ////////////////////////////////

        /// <summary>
        /// Error parsing arguments.
        /// </summary>
        [Display(Name = "Invalid or missing arguments.")]
        E9000 = 9000,

        /// <summary>
        /// Can't access or create backup directory.
        /// </summary>
        [Display(Name = "Can't access or create backup directory.")]
        E9002 = 9002,

        /// <summary>
        /// Error when deleting existing backup file.
        /// </summary>
        [Display(Name = "Can't delete existing backup file.")]
        E9003 = 9003,

        /// <summary>
        /// Backup file specified without backup path.
        /// </summary>
        [Display(Name = "Backup file specified without backup path. Specify backup path with --backuppath.")]
        E9004 = 9004,

        /// <summary>
        /// Can't find the target in Cloudsmith repository.
        /// </summary>
        [Display(Name = "Can't find the target in Cloudsmith repository.")]
        E9005 = 9005,

        /// <summary>
        /// Can't create temporary directory to download firmware.
        /// </summary>
        [Display(Name = "Can't create temporary directory to download firmware.")]
        E9006 = 9006,

        /// <summary>
        /// Error downloading firmware file.
        /// </summary>
        [Display(Name = "Error downloading firmware file.")]
        E9007 = 9007,

        /// <summary>
        /// Couldn't find application file. Check the path.
        /// </summary>
        [Display(Name = "Couldn't find application file. Check the path.")]
        E9008 = 9008,

        /// <summary>
        /// Can't program deployment BIN file without specifying a valid address
        /// </summary>
        [Display(Name = "Can't program deployment BIN file without specifying a valid deployment address.")]
        E9009 = 9009,

    }
}
