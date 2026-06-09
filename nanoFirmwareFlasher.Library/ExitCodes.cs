// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel.DataAnnotations;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Exit Codes.
    /// </summary>
    public enum ExitCodes
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
        [Display(Name = "No DFU device found. Make sure it's connected and has booted in DFU mode. Try --nativedfu for tool-free DFU or --uart for serial bootloader.")]
        E1000 = 1000,

        /// <summary>
        /// DFU file doesn't exist
        /// </summary>
        [Display(Name = "Couldn't find DFU file. Check the path.")]
        E1002 = 1002,

        /// <summary>
        /// Error flashing DFU device
        /// </summary>
        [Display(Name = "Error flashing DFU device.")]
        E1003 = 1003,

        /// <summary>
        /// Firmware package doesn't have DFU package
        /// </summary>
        [Display(Name = "Firmware package doesn't have a DFU package.")]
        E1004 = 1004,

        /// <summary>
        /// Can't connect to specified DFU device.
        /// </summary>
        [Display(Name = "Can't connect to specified DFU device. Make sure it's connected and that the ID is correct.")]
        E1005 = 1005,

        /// <summary>
        /// Failed to start execution on the connected device.
        /// </summary>
        [Display(Name = "Failed to start execution on the connected device.")]
        E1006 = 1006,

        ///////////////////////
        // nanoDevice Errors //
        ///////////////////////

        /// <summary>
        /// Error connecting to nano device.
        /// </summary>
        [Display(Name = "Error connecting to nano device.")]
        E2000 = 2000,

        /// <summary>
        /// Error connecting to nano device.
        /// </summary>
        [Display(Name = "Error occurred with listing nano devices.")]
        E2001 = 2001,

        /// <summary>
        /// Error executing operation with nano device.
        /// </summary>
        [Display(Name = "Error executing operation with nano device.")]
        E2002 = 2002,

        /// <summary>
        /// Error executing operation with nano device.
        /// </summary>
        [Display(Name = "Error executing file deployment on nano device.")]
        E2003 = 2003,

        //////////////////////////////
        // Raspberry Pi Pico Errors //
        //////////////////////////////

        /// <summary>
        /// Generic Pico flashing error.
        /// </summary>
        [Display(Name = "Error flashing Raspberry Pi Pico device.")]
        E3000 = 3000,

        /// <summary>
        /// UF2 drive not found (device not in BOOTSEL mode).
        /// </summary>
        [Display(Name = "Pico UF2 drive not found. Make sure the device is connected in BOOTSEL mode (hold BOOTSEL button while connecting USB).")]
        E3001 = 3001,

        /// <summary>
        /// Failed to copy UF2 file to device.
        /// </summary>
        [Display(Name = "Failed to copy firmware file to Pico UF2 drive.")]
        E3002 = 3002,

        /// <summary>
        /// Invalid or corrupt UF2 file.
        /// </summary>
        [Display(Name = "Invalid or corrupt UF2 file.")]
        E3003 = 3003,

        /// <summary>
        /// Device not recognized as RP2040/RP2350.
        /// </summary>
        [Display(Name = "Connected device not recognized as RP2040 or RP2350.")]
        E3004 = 3004,

        /// <summary>
        /// Timeout waiting for UF2 drive to appear.
        /// </summary>
        [Display(Name = "Timeout waiting for Pico UF2 drive to appear. Make sure the device is in BOOTSEL mode.")]
        E3005 = 3005,

        /// <summary>
        /// Multiple Pico devices in BOOTSEL mode — cannot determine deploy target.
        /// </summary>
        [Display(Name = "Multiple Pico devices found in BOOTSEL mode. Disconnect extra devices so only the target remains.")]
        E3006 = 3006,

        ////////////////////////
        // ESP32 tools Errors //
        ////////////////////////

        /// <summary>
        /// Error executing ESP32 serial command
        /// </summary>
        [Display(Name = "Error executing ESP32 serial command.")]
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

        //////////////////////////
        // ST Programmer Errors //
        //////////////////////////

        /// <summary>
        /// Error executing STM32 Programmer CLI command.
        /// </summary>
        [Display(Name = "Error executing STM32 Programmer CLI command.")]
        E5000 = 5000,

        /// <summary>
        /// No JTAG device found
        /// </summary>
        [Display(Name = "No JTAG device found. Make sure it's connected. Try --nativestlink or --nativeswd for tool-free flashing.")]
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
        /// Invalid address specified. Hexadecimal (0x0000F000) format required.
        /// </summary>
        [Display(Name = "Invalid address specified. Hexadecimal (0x0000F000) format required.")]
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

        /// <summary>
        /// Flash verification failed — read-back data does not match written data.
        /// </summary>
        [Display(Name = "Flash verification failed — read-back data does not match the written data.")]
        E5022 = 5022,

        //////////////////////////////
        // Native DFU Errors //
        //////////////////////////////

        /// <summary>
        /// Native USB DFU not supported on this platform.
        /// </summary>
        [Display(Name = "Native USB DFU is only supported on Windows. Use --dfu for CLI-based DFU or --uart for serial bootloader.")]
        E5030 = 5030,

        /// <summary>
        /// Native USB DFU protocol error.
        /// </summary>
        [Display(Name = "DFU protocol error during native USB transfer.")]
        E5031 = 5031,

        //////////////////////////////
        // Native SWD (CMSIS-DAP)   //
        //////////////////////////////

        /// <summary>
        /// Native SWD not supported on this platform.
        /// </summary>
        [Display(Name = "Native SWD via CMSIS-DAP is only supported on Windows.")]
        E5040 = 5040,

        /// <summary>
        /// Native SWD protocol error.
        /// </summary>
        [Display(Name = "SWD protocol error during native CMSIS-DAP transfer.")]
        E5041 = 5041,

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

        /// <summary>
        /// Timeout of the connection
        /// </summary>
        [Display(Name = "Couldn't access serial device. Another (nanoFramework) application has exclusive access to the device.")]
        E6002 = 6002,

        ///////////////
        // TI Errors //
        ///////////////

        /// <summary>
        /// Couldn't open serial device
        /// </summary>
        [Display(Name = "Unsupported device.")]
        E7000 = 7000,

        ///////////////////
        // J-Link Errors //
        ///////////////////

        /// <summary>
        /// Error executing J-Link CLI command.
        /// </summary>
        [Display(Name = "Error executing J-Link CLI command.")]
        E8000 = 8000,

        /// <summary>
        /// No J-Link device found
        /// </summary>
        [Display(Name = "No J-Link device found. Make sure it's connected.")]
        E8001 = 8001,

        /// <summary>
        /// Error executing silink command.
        /// </summary>
        [Display(Name = "Error executing silink CLI command.")]
        E8002 = 8002,

        /// <summary>
        /// Path of BIN file contains spaces or diacritic characters.
        /// </summary>
        [Display(Name = "Path of BIN file contains spaces or diacritic characters.")]
        E8003 = 8003,

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

        /// <summary>
        /// Couldn't find any device connected
        /// </summary>
        [Display(Name = "Couldn't find any device connected. For STM32, try: --nativestlink, --nativeswd, --nativedfu, or --uart.")]
        E9010 = 9010,

        /// <summary>
        /// Couldn't find CLR image file. Check the path.
        /// </summary>
        [Display(Name = "Couldn't find CLR image file. Check the path.")]
        E9011 = 9011,

        /// <summary>
        /// CLR image file has wrong format. It has to be a binary file.
        /// </summary>
        [Display(Name = "CLR image file has wrong format. It has to be a binary file.")]
        E9012 = 9012,

        /// <summary>
        /// Unsupported platform.
        /// </summary>
        [Display(Name = "Unsupported platform. Valid options are: esp32, stm32, cc13x2")]
        E9013 = 9013,

        /// <summary>
        /// Error clearing cache location.
        /// </summary>
        [Display(Name = "Error occurred when clearing the firmware cache location.")]
        E9014 = 9014,

        /// <summary>
        /// Can't find the target in the firmware archive.
        /// </summary>
        [Display(Name = "Can't find the target in the firmware archive.")]
        E9015 = 9015,
    }
}
