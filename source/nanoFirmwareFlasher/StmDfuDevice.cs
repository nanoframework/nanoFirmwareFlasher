//
// Copyright (c) 2019 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;

namespace nanoFramework.Tools.FirmwareFlasher
{

    // STSW-ST7009
    // DFU Development Kit package (Device Firmware Upgrade).


    internal class StmDfuDevice
    {
        /// <summary>
        /// GUID of interface class declared by ST DFU devices
        /// </summary>
        private static Guid s_dfuGuid = new Guid("3FE809AB-FB91-4CB5-A643-69670D52366E");

        private readonly string _deviceId;
        private readonly IntPtr _hDevice = IntPtr.Zero;
        private readonly StDfu.DfuFunctionalDescriptor _dfuDescriptor;

        private readonly uint _numberOfBlocks;
        private readonly uint _startAddress;

        private readonly List<StDfu.MappingSector> _sectorMap;

        private int targetIndex = 0;

        /// <summary>
        /// Maximum size of a block of data for writing, this is set depending on the bootloader version
        /// </summary>
        private UInt16 _maxWriteBlockSize = 1024;

        /// <summary>
        /// Property with option for performing mass erase on the connected device.
        /// If <see langword="false"/> only the flash sectors that will programmed are erased.
        /// </summary>
        public bool DoMassErase { get; set; } = false;

        /// <summary>
        /// This property is <see langword="true"/> if a DFU device is connected.
        /// </summary>
        public bool DevicePresent => !string.IsNullOrEmpty(_deviceId);

        /// <summary>
        /// ID of the connected DFU device.
        /// </summary>
        // the split bellow is to get only the ID part of the USB ID
        // that follows the pattern: USB\\VID_0483&PID_DF11\\3380386D3134
        public string DeviceId => _deviceId?.Split('\\', ' ')[2];

        public StDfu.UsbDeviceDescriptor _usbDescriptor { get; }

        /// <summary>
        /// Option to output progress messages.
        /// Default is <see langword="true"/>.
        /// </summary>
        public VerbosityLevel Verbosity { get; internal set; } = VerbosityLevel.Normal;

        /// <summary>
        /// Creates a new <see cref="StmDfuDevice"/>. If a DFU device ID is provided it will try to connect to that device.
        /// </summary>
        /// <param name="deviceId">ID of the device to connect to.</param>
        public StmDfuDevice(string deviceId = null)
        {
            uint returnValue;

            ManagementObjectCollection usbDevicesCollection;

            // build a managed object searcher to find USB devices with the ST DFU VID & PID along with the device description
            using (var searcher = new ManagementObjectSearcher(@"SELECT * FROM Win32_PnPEntity WHERE DeviceID Like ""USB\\VID_0483&PID_DF11%"" AND Description Like ""STM Device in DFU Mode"" "))
                usbDevicesCollection = searcher.Get();

            // are we to connect to a specific device?
            if (deviceId == null)
            {
                // no, grab the USB device ID of the 1st listed device from the respective property
                deviceId = usbDevicesCollection.OfType<ManagementObject>().Select(mo => mo.Properties["DeviceID"].Value as string).FirstOrDefault();
            }
            else
            {
                // yes, filter the connect devices collection with the requested device ID
                deviceId = usbDevicesCollection.OfType<ManagementObject>().Select(mo => mo.Properties["DeviceID"].Value as string).FirstOrDefault(d => d.Contains(deviceId));
            }

            // sanity check for no device found
            if (deviceId == null)
            {
                // couldn't find any DFU device
                return;
            }

            // store USB device ID
            _deviceId = deviceId;

            // ST DFU is expecting a device path in the WQL pattern:
            // "\\?\USB#VID_0483&PID_DF11#3380386D3134#{3FE809AB-FB91-4CB5-A643-69670D52366E}"
            // The GUID there is for the USB interface declared by DFU devices

            string devicePath = @"\\?\" + deviceId.Replace(@"\", "#") + @"#{" + s_dfuGuid.ToString() + "}";

            // open device
            returnValue = StDfu.STDFU_Open(devicePath, out _hDevice);
            if (returnValue != StDfu.STDFU_NOERROR)
            {
                throw new CantOpenDfuDeviceException(returnValue);
            }

            // read USB descriptor
            StDfu.UsbDeviceDescriptor usbDescriptor = new StDfu.UsbDeviceDescriptor();
            returnValue = StDfu.STDFU_GetDeviceDescriptor(ref _hDevice, ref usbDescriptor);

            if (returnValue == StDfu.STDFU_NOERROR)
            {
                _usbDescriptor = usbDescriptor;

                // check protocol version
                // update max write block size
                switch (_usbDescriptor.bcdDevice)
                {
                    case 0x011A:
                    case 0x0200:
                        _maxWriteBlockSize = 1024;
                        break;

                    case 0x02100:
                    case 0x2200:
                        _maxWriteBlockSize = 2048;
                        break;

                    default:
                        throw new BadDfuProtocolVersionException(usbDescriptor.bcdDevice.ToString("X4"));
                }
            }
            else
            {
                throw new CantReadUsbDescriptorException(returnValue);
            }

            // read DFU descriptor
            var dfuDescriptor = new StDfu.DfuFunctionalDescriptor();
            uint interfaceIndex = 0, alternates = 0;
            returnValue = StDfu.STDFU_GetDFUDescriptor(ref _hDevice, ref interfaceIndex, ref alternates, ref dfuDescriptor);

            if (returnValue == StDfu.STDFU_NOERROR)
            {
                _dfuDescriptor = dfuDescriptor;

                // Enable and disable programming choice, bitManifestationTolerant	
                if (_dfuDescriptor.wTransfertSize == 8)
                {
                    // Low Speed Devices
                    _numberOfBlocks = 8;
                }
                if (_dfuDescriptor.wTransfertSize == 128)
                {
                    // Full Speed Devices
                    _numberOfBlocks = 128;
                }
            }
            else
            {
                throw new CantReadDfuDescriptorException(returnValue);
            }

            // create sector mapping for device
            _sectorMap = StDfu.CreateMappingFromDevice(_hDevice, alternates, dfuDescriptor);
        }

        /// <summary>
        /// Flash the DFU supplied to the connected device.
        /// </summary>
        /// <param name="filePath"></param>
        public void FlashDfuFile(string filePath)
        {
            // check DFU file existence
            if (!File.Exists(filePath))
            {
                throw new DfuFileDoesNotExistException();
            }

            // load DFU file

            StDfu.DfuFile dfuFile = StDfu.LoadDfuFile(filePath, Verbosity);

            // erase flash
            if (DoMassErase)
            {
                if (Verbosity >= VerbosityLevel.Normal)
                {
                    Console.Write("Mass erase device...");
                }

                StDfu.MassErase(_hDevice, Verbosity >= VerbosityLevel.Normal);

                if (Verbosity >= VerbosityLevel.Normal)
                {
                    Console.WriteLine(" OK");
                }
            }
            else
            {
                //erase only the sections we will program
                if (Verbosity >= VerbosityLevel.Normal)
                {
                    Console.Write("Erasing sectors to program...");
                }

                StDfu.DfuTarget dfuTarErase = dfuFile.DfuTargets[0];

                for (int nIdxElem = 0; nIdxElem < dfuTarErase.DfuElements.Length; ++nIdxElem)
                {
                    StDfu.DfuElement dfuElem = dfuTarErase.DfuElements[nIdxElem];

                    StDfu.PartialErase(_hDevice, dfuElem.Address, (uint)dfuElem.Data.Length, _sectorMap, Verbosity >= VerbosityLevel.Detailed);
                }

                if (Verbosity >= VerbosityLevel.Normal)
                {
                    Console.WriteLine(" OK");
                }
            }

            // flash the device
            StDfu.DfuTarget dfuTarget = dfuFile.DfuTargets[targetIndex];

            if (Verbosity >= VerbosityLevel.Normal)
            {
                Console.Write("Flashing device...");
            }

            for (int elementIndex = 0; elementIndex < dfuTarget.DfuElements.Length; ++elementIndex)
            {
                StDfu.DfuElement dfuElement = dfuTarget.DfuElements[elementIndex];

                // Write the data in MaxWriteBlockSize blocks
                for (uint blockNumber = 0; blockNumber <= (uint)dfuElement.Data.Length / _maxWriteBlockSize; blockNumber++)
                {
                    // grab data for write and store it into a 2048 byte buffer
                    byte[] buffer = dfuElement.Data.Skip((int)(_maxWriteBlockSize * blockNumber)).Take(_maxWriteBlockSize).ToArray();

                    if (buffer.Length < _maxWriteBlockSize)
                    {
                        var i = buffer.Length;
                        Array.Resize(ref buffer, _maxWriteBlockSize);

                        // Pad with 0xFF so our CRC matches the ST Bootloader and STLink's CRC
                        for (; i < _maxWriteBlockSize; i++)
                        {
                            buffer[i] = 0xFF;
                        }
                    }

                    StDfu.WriteBlock(_hDevice, dfuElement.Address, buffer, blockNumber);
                }
            }

            if (Verbosity >= VerbosityLevel.Normal)
            {
                Console.WriteLine(" OK");
            }

            if (Verbosity >= VerbosityLevel.Normal)
            {
                Console.Write("Launching nanoBooter...");
            }

            // always reboot with nanoBooter
            StDfu.Detach(_hDevice, 0x08000000);

            if (Verbosity >= VerbosityLevel.Normal)
            {
                Console.WriteLine(" OK");
            }
        }

        /// <summary>
        /// Search connected DFU devices.
        /// </summary>
        /// <returns>A collection of connected DFU devices.</returns>
        public static List<string> ListDfuDevices()
        {
            ManagementObjectCollection usbDevicesCollection;

            // build a managed object searcher to find USB devices with the ST DFU VID & PID along with the device description
            using (var searcher = new ManagementObjectSearcher(@"SELECT * FROM Win32_PnPEntity WHERE DeviceID Like ""USB\\VID_0483&PID_DF11%"" AND Description Like ""STM Device in DFU Mode"" "))
                usbDevicesCollection = searcher.Get();

            // the split bellow is to get only the ID part of the USB ID
            // that follows the pattern: USB\\VID_0483&PID_DF11\\3380386D3134
            return usbDevicesCollection.OfType<ManagementObject>().Select(mo => (mo.Properties["DeviceID"].Value as string).Split('\\', ' ')[2]).ToList();
        }
    }
}
