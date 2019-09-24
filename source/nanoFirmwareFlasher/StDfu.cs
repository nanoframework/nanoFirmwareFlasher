//
// Copyright (c) 2019 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace nanoFramework.Tools.FirmwareFlasher
{
    internal class StDfu
    {
        #region DFU file structures

        // generic method to marshal a section of a byte array into a struct
        static void StructFromBytes<T>(ref T pstruct, byte[] abyBuff, int nOffset)
        {
            int nLen = Marshal.SizeOf(pstruct);
            IntPtr pbyMarsh = Marshal.AllocHGlobal(nLen);
            Marshal.Copy(abyBuff, nOffset, pbyMarsh, nLen);
            pstruct = (T)Marshal.PtrToStructure(pbyMarsh, pstruct.GetType());
            Marshal.FreeHGlobal(pbyMarsh);
        }

        // DFU file header
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        unsafe struct DfuHeader
        {
            // "DfuSe"
            public fixed byte Signature[5];
            public byte Version;
            // size of the sections from the header to the tail
            public uint DfuImageSize;
            // how many targets in the file
            public byte TargetsCount;
        }

        // DFU target header
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        unsafe struct DfuImageTarget
        {
            // "Target"
            public fixed byte Signature[6];            
            public byte AlternateSetting;
            // flag that the target has a name
            public uint IsNamed;
            // target name, which is optional
            public fixed byte TargetName[255];
            // size of the elements that follow the header
            public uint TargetSize;
            // how many elements in this target
            public uint NumElements;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        unsafe struct DfuImageElement
        {
            // start address
            public uint Address;
            // size of the data block
            public uint Size;
            // data follow the Size field
        }

        //this is always at the end of the file, so you can seek there and work backwards
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        unsafe struct DfuTail
        { 
            public ushort Version;
            public ushort Pid;
            public ushort Vid;
            // DFU version, seems to be fixed with 0x011A
            public ushort DfuVersion;
            // signature with DFU ASCII backwards:  'U'(55) 'F'(46) 'D'(44)
            public fixed byte Signature[3];
            // tail length (16)
            public byte Length;
            // file CRC excluding the CRC
            public uint Crc;
        }

        #endregion


        #region DFU descriptors

        public class DfuFile
        {
            public ushort Vid = 0;
            public ushort Pid = 0;
            public ushort Version = 0;
            public DfuTarget[] DfuTargets = null;
        }

        public class DfuTarget
        {
            public string Name = null;
            public DfuElement[] DfuElements = null;
        }

        public class DfuElement
        {
            public uint Address = 0xffffffff;
            public byte[] Data = null;
        }

        #endregion

        public const uint STDFU_ERROR_OFFSET = 0x12340000;

        public const uint STDFU_NOERROR = STDFU_ERROR_OFFSET + 0x0;
        public const uint STDFU_MEMORY = STDFU_ERROR_OFFSET + 0x1;
        public const uint STDFU_BADPARAMETER = STDFU_ERROR_OFFSET + 0x2;

        public const uint STDFU_NOTIMPLEMENTED = STDFU_ERROR_OFFSET + 0x3;
        public const uint STDFU_ENUMFINISHED = STDFU_ERROR_OFFSET + 0x4;
        public const uint STDFU_OPENDRIVERERROR = STDFU_ERROR_OFFSET + 0x5;

        public const uint STDFU_ERRORDESCRIPTORBUILDING = STDFU_ERROR_OFFSET + 0x6;
        public const uint STDFU_PIPECREATIONERROR = STDFU_ERROR_OFFSET + 0x7;
        public const uint STDFU_PIPERESETERROR = STDFU_ERROR_OFFSET + 0x8;
        public const uint STDFU_PIPEABORTERROR = STDFU_ERROR_OFFSET + 0x9;
        public const uint STDFU_STRINGDESCRIPTORERROR = STDFU_ERROR_OFFSET + 0xA;

        public const uint STDFU_DRIVERISCLOSED = STDFU_ERROR_OFFSET + 0xB;
        public const uint STDFU_VENDOR_RQ_PB = STDFU_ERROR_OFFSET + 0xC;
        public const uint STDFU_ERRORWHILEREADING = STDFU_ERROR_OFFSET + 0xD;
        public const uint STDFU_ERRORBEFOREREADING = STDFU_ERROR_OFFSET + 0xE;
        public const uint STDFU_ERRORWHILEWRITING = STDFU_ERROR_OFFSET + 0xF;
        public const uint STDFU_ERRORBEFOREWRITING = STDFU_ERROR_OFFSET + 0x10;
        public const uint STDFU_DEVICERESETERROR = STDFU_ERROR_OFFSET + 0x11;
        public const uint STDFU_CANTUSEUNPLUGEVENT = STDFU_ERROR_OFFSET + 0x12;
        public const uint STDFU_INCORRECTBUFFERSIZE = STDFU_ERROR_OFFSET + 0x13;
        public const uint STDFU_DESCRIPTORNOTFOUND = STDFU_ERROR_OFFSET + 0x14;
        public const uint STDFU_PIPESARECLOSED = STDFU_ERROR_OFFSET + 0x15;
        public const uint STDFU_PIPESAREOPEN = STDFU_ERROR_OFFSET + 0x16;

        public const uint STDFU_TIMEOUTWAITINGFORRESET = STDFU_ERROR_OFFSET + 0x17;

        public const uint STDFU_RQ_GET_DEVICE_DESCRIPTOR = 0x02000000;
        public const uint STDFU_RQ_GET_DFU_DESCRIPTOR = 0x03000000;
        public const uint STDFU_RQ_GET_STRING_DESCRIPTOR = 0x04000000;
        public const uint STDFU_RQ_GET_NB_OF_CONFIGURATIONS = 0x05000000;
        public const uint STDFU_RQ_GET_CONFIGURATION_DESCRIPTOR = 0x06000000;
        public const uint STDFU_RQ_GET_NB_OF_INTERFACES = 0x07000000;
        public const uint STDFU_RQ_GET_NB_OF_ALTERNATES = 0x08000000;
        public const uint STDFU_RQ_GET_INTERFACE_DESCRIPTOR = 0x09000000;
        public const uint STDFU_RQ_OPEN = 0x0A000000;
        public const uint STDFU_RQ_CLOSE = 0x0B000000;
        public const uint STDFU_RQ_DETACH = 0x0C000000;
        public const uint STDFU_RQ_DOWNLOAD = 0x0D000000;
        public const uint STDFU_RQ_UPLOAD = 0x0E000000;
        public const uint STDFU_RQ_GET_STATUS = 0x0F000000;
        public const uint STDFU_RQ_CLR_STATUS = 0x10000000;
        public const uint STDFU_RQ_GET_STATE = 0x11000000;
        public const uint STDFU_RQ_ABORT = 0x12000000;
        public const uint STDFU_RQ_SELECT_ALTERNATE = 0x13000000;
        public const uint STDFU_RQ_AWAITINGPNPUNPLUGEVENT = 0x14000000;
        public const uint STDFU_RQ_AWAITINGPNPPLUGEVENT = 0x15000000;
        public const uint STDFU_RQ_IDENTIFYINGDEVICE = 0x16000000;

        private const char SEPARATOR_ADDRESS = '/';
        private const char SEPARATOR_ADDRESS_ALIASED = '-';
        private const char SEPARATOR_BLOCKS = ',';
        private const char SEPARATOR_NBSECTORS_SECTORSIZE = '*';

        // DFU States
        public const uint STATE_IDLE                    = 0x00;
        public const uint STATE_DETACH					= 0x01;
        public const uint STATE_DFU_IDLE				= 0x02;
        public const uint STATE_DFU_DOWNLOAD_SYNC		= 0x03;
        public const uint STATE_DFU_DOWNLOAD_BUSY		= 0x04;
        public const uint STATE_DFU_DOWNLOAD_IDLE		= 0x05;
        public const uint STATE_DFU_MANIFEST_SYNC		= 0x06;
        public const uint STATE_DFU_MANIFEST			= 0x07;
        public const uint STATE_DFU_MANIFEST_WAIT_RESET = 0x08;
        public const uint STATE_DFU_UPLOAD_IDLE			= 0x09;
        public const uint STATE_DFU_ERROR				= 0x0A;

        public const uint STATE_DFU_UPLOAD_SYNC			= 0x91;
        public const uint STATE_DFU_UPLOAD_BUSY			= 0x92;


        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct UsbInterfaceDescriptor
        {
            public byte bLength;
            public byte bDescriptorType;
            public byte bInterfaceNumber;
            public byte bAlternateSetting;
            public byte bNumEndpoints;
            public byte bInterfaceClass;
            public byte bInterfaceSubClass;
            public byte bInterfaceProtocol;
            public byte iInterface;
        };

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct DfuFunctionalDescriptor
        {
            public byte bLength;
            public byte bDescriptorType;
            public byte bmAttributes;
            public ushort wDetachTimeOut;
            public ushort wTransfertSize;
            public ushort bcdDFUVersion;
        };

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct DfuStatus
        {
            public byte bStatus;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
            public byte[] bwPollTimeout;
            public byte bState;
            public byte iString;
        };

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct UsbDeviceDescriptor
        {
            public byte bLength;
            public byte bDescriptorType;
            public ushort bcdUSB;
            public byte bDeviceClass;
            public byte bDeviceSubClass;
            public byte bDeviceProtocol;
            public byte bMaxPacketSize0;
            public ushort idVendor;
            public ushort idProduct;
            public ushort bcdDevice;
            public byte iManufacturer;
            public byte iProduct;
            public byte iSerialNumber;
            public byte bNumConfigurations;
        };

        // from MAPPINGSECTOR
        public class MappingSector
        {
            public enum SectorType
            {
                InternalFLASH,
                OptionBytes,
                OTP,
                DeviceFeature,
                Other
            };

            public uint StartAddress;
            public uint SectorIndex;
            public uint SectorSize;
            public SectorType Type;
            public string Name;

            public MappingSector(string name, SectorType sectorType, uint startAddress, uint size, uint sectorIndex)
            {
                Name = name;
                Type = sectorType;
                StartAddress = startAddress;
                SectorSize = size;
                SectorIndex = sectorIndex;
            }
        }

        #region imports from STDFU.dll

        // export from dumpbin
        // 1    0 000014A0 STDFU_Abort
        // 2    1 00001030 STDFU_Close
        // 3    2 000013F0 STDFU_Clrstatus
        // 4    3 00001210 STDFU_Detach
        // 5    4 00001290 STDFU_Dnload
        // 6    5 000010B0 STDFU_GetConfigurationDescriptor
        // 7    6 00001130 STDFU_GetDFUDescriptor
        // 8    7 00001050 STDFU_GetDeviceDescriptor
        // 9    8 00001110 STDFU_GetInterfaceDescriptor
        //10    9 000010F0 STDFU_GetNbOfAlternates
        //11    A 00001090 STDFU_GetNbOfConfigurations
        //12    B 000010D0 STDFU_GetNbOfInterfaces
        //13    C 00001070 STDFU_GetStringDescriptor
        //14    D 00001440 STDFU_Getstate
        //15    E 00001360 STDFU_Getstatus
        //16    F 00001010 STDFU_Open
        //17   10 000011F0 STDFU_SelectCurrentConfiguration
        //18   11 000012F0 STDFU_Upload


        [DllImport("STDFU.dll", EntryPoint = "STDFU_GetInterfaceDescriptor", CharSet = CharSet.Auto)]
        internal static extern uint STDFU_GetInterfaceDescriptor(
            ref IntPtr handle, 
            uint nConfigIdx, 
            uint nInterfaceIdx, 
            uint nAltSetIdx, 
            ref UsbInterfaceDescriptor pDesc);


        [DllImport("STDFU.dll", EntryPoint = "STDFU_SelectCurrentConfiguration", CharSet = CharSet.Ansi)]
        private static extern uint STDFU_SelectCurrentConfiguration(
            ref IntPtr hDevice, 
            uint ConfigIndex, 
            uint InterfaceIndex, 
            uint AlternateSetIndex);


        [DllImport("STDFU.dll", EntryPoint = "STDFU_GetDFUDescriptor", CharSet = CharSet.Auto)]
        internal static extern uint STDFU_GetDFUDescriptor(
            ref IntPtr handle, 
            ref uint DFUInterfaceNum, 
            ref uint NBOfAlternates, 
            ref DfuFunctionalDescriptor dfuDescriptor);

        [DllImport("STDFU.dll", EntryPoint = "STDFU_GetDeviceDescriptor", CharSet = CharSet.Auto)]
        internal static extern uint STDFU_GetDeviceDescriptor(
            ref IntPtr handle, 
            ref UsbDeviceDescriptor descriptor);

        [DllImport("STDFU.dll", EntryPoint = "STDFU_GetStringDescriptor", CharSet = CharSet.Auto)]
        internal static extern uint STDFU_GetStringDescriptor(
            ref IntPtr handle, 
            uint index, 
            IntPtr stringBuffer, 
            uint stringLength);

        [DllImport("STDFU.dll", EntryPoint = "STDFU_Dnload", CharSet = CharSet.Ansi)]
        internal static extern uint STDFU_Dnload(
            ref IntPtr hDevice, 
            [MarshalAs(UnmanagedType.LPArray)]byte[] pBuffer,
            uint nBytes,
            ushort nBlocks);

        [DllImport("STDFU.dll", EntryPoint = "STDFU_Getstatus", CharSet = CharSet.Ansi)]
        internal static extern uint STDFU_GetStatus(
            ref IntPtr hDevice, 
            ref DfuStatus dfuStatus);

        [DllImport("STDFU.dll", EntryPoint = "STDFU_Clrstatus", CharSet = CharSet.Ansi)]
        internal static extern uint STDFU_ClrStatus(ref IntPtr hDevice);

        [DllImport("STDFU.dll", EntryPoint = "STDFU_Open", CharSet = CharSet.Ansi)]
        internal static extern uint STDFU_Open(
            [MarshalAs(UnmanagedType.LPStr)]string szDevicePath, 
            out IntPtr hDevice);


        #endregion

        internal static List<MappingSector> CreateMappingFromDevice(
            IntPtr hDevice, 
            uint alternates, 
            DfuFunctionalDescriptor dfuDescriptor)
        {
            List<MappingSector> sectorMap = new List<MappingSector>((int)alternates);

            uint returnValue;
            uint interfaceIndex = 0;
            IntPtr stringBuffer = Marshal.AllocHGlobal(512);

            UsbInterfaceDescriptor usbInterfaceDescriptor = new UsbInterfaceDescriptor();

            // Loop through Internal FLASH, Option bytes, OTP and Device Feature
            for (uint i = 0; i < alternates; i++)
            {
                uint sectorIndex = 0;

                returnValue = STDFU_GetInterfaceDescriptor(ref hDevice, 0, interfaceIndex, i, ref usbInterfaceDescriptor);

                if (returnValue != STDFU_NOERROR)
                {
                    throw new ErrorGettingInterfaceDescriptorException();
                }

                // sanity check
                if (usbInterfaceDescriptor.iInterface == 0)
                {
                    throw new ErrorGettingInterfaceDescriptorException();
                }

                returnValue = STDFU_GetStringDescriptor(ref hDevice, usbInterfaceDescriptor.iInterface, stringBuffer, 512);
                if (returnValue != STDFU_NOERROR)
                {
                    throw new ErrorGettingStringDescriptorException();
                }

                if (returnValue == STDFU_NOERROR)
                {
                    ushort numberOfSectors = 0;
                    MappingSector.SectorType type;

                    var rawSectorDescription = Marshal.PtrToStringAnsi(stringBuffer);

                    // sanity check
                    if (rawSectorDescription[0] != '@')
                    {
                        throw new WrongOrInvalidStringDescriptorException();
                    }

                    var sectorName = rawSectorDescription.Substring(1, rawSectorDescription.IndexOf('/') - 1);
                    sectorName = sectorName.TrimEnd(' ');

                    if (sectorName.Equals("Internal Flash"))
                    {
                        type = MappingSector.SectorType.InternalFLASH;
                    }
                    else if (sectorName.Equals("Option Bytes"))
                    {
                        type = MappingSector.SectorType.OptionBytes;
                    }
                    else if (sectorName.Equals("OTP Memory"))
                    {
                        type = MappingSector.SectorType.OTP;
                    }
                    else if (sectorName.Equals("Device Feature"))
                    {
                        type = MappingSector.SectorType.DeviceFeature;
                    }
                    else
                    {
                        type = MappingSector.SectorType.Other;
                    }

                    var startAddress = uint.Parse(rawSectorDescription.Substring(rawSectorDescription.IndexOf(SEPARATOR_ADDRESS) + 3, 8), System.Globalization.NumberStyles.HexNumber);

                    var sectorDescription = rawSectorDescription;

                    while (sectorDescription.IndexOf(SEPARATOR_NBSECTORS_SECTORSIZE) >= 0)
                    {
                        var sectorN = sectorDescription.Substring(sectorDescription.IndexOf(SEPARATOR_NBSECTORS_SECTORSIZE) - 3, 3);
                        if (char.IsDigit(sectorN[0]))
                        {
                            numberOfSectors = ushort.Parse(sectorN);
                        }
                        else
                        {
                            numberOfSectors = ushort.Parse(sectorN.Substring(1));
                        }

                        var sectorSize = ushort.Parse(sectorDescription.Substring(sectorDescription.IndexOf(SEPARATOR_NBSECTORS_SECTORSIZE) + 1, 3));
                        if (sectorDescription[sectorDescription.IndexOf(SEPARATOR_NBSECTORS_SECTORSIZE) + 4] == 'K')
                        {
                            sectorSize *= 1024;
                        }
                        else if (sectorDescription[sectorDescription.IndexOf(SEPARATOR_NBSECTORS_SECTORSIZE) + 4] == 'M')
                        {
                            sectorSize *= 1024;
                        }

                        for (sectorIndex = 0; sectorIndex < numberOfSectors; sectorIndex++)
                        {
                            sectorMap.Add(new MappingSector(sectorName, type, startAddress, sectorSize, sectorIndex));
                            startAddress += sectorSize;
                        }

                        sectorDescription = sectorDescription.Substring(sectorDescription.IndexOf(SEPARATOR_NBSECTORS_SECTORSIZE) + 1);
                    }
                }
                else
                {
                    break;
                }
            }

            return sectorMap;
        }

        public bool ParseDfuFile(
            string filepath, 
            out ushort vid, 
            out ushort pid, 
            out ushort version,
            bool outputMessages = false)
        {
            byte[] fileData;
            bool retval = true;

            try
            {
                // read content from DFU file
                fileData = System.IO.File.ReadAllBytes(filepath);

                // check prefix
                if (Encoding.UTF8.GetString(fileData, 0, 5) != "DfuSe")
                {
                    throw new DfuFileException("File signature error");
                }

                // check version
                if (fileData[5] != 1)
                {
                    throw new DfuFileException("DFU file version must be 1");
                }

                // check suffix
                if ((Encoding.UTF8.GetString(fileData, fileData.Length - 8, 3) != "UFD")
                    || (fileData[fileData.Length - 5] != 16)
                    || (fileData[fileData.Length - 10] != 0x1A)
                    || (fileData[fileData.Length - 9] != 0x01))
                {
                    throw new DfuFileException("File suffix error");
                }

                // check the CRC
                var crc = BitConverter.ToUInt32(fileData, fileData.Length - 4);
                if (crc != CalculateCRC(fileData))
                {
                    throw new DfuFileException("File CRC error");
                }

                // get VID, PID and version number from file
                vid = BitConverter.ToUInt16(fileData, fileData.Length - 12);
                pid = BitConverter.ToUInt16(fileData, fileData.Length - 14);
                version = BitConverter.ToUInt16(fileData, fileData.Length - 16);
            }
            catch
            {
                vid = 0;
                pid = 0;
                version = 0;
                retval = false;
            }

            return retval;
        }

        internal static unsafe DfuFile LoadDfuFile(
            string filePath, 
            VerbosityLevel verbosity)
        {
            byte[] fileData;
            DfuFile dfuFile = new DfuFile();

            if (verbosity >= VerbosityLevel.Normal)
            {
                Console.Write("Loading DFU file...");
            }

            try
            {
                /////////////////
                // read the file 
                fileData = System.IO.File.ReadAllBytes(filePath);

                // parse DFU header
                DfuHeader dfuHeader = new DfuHeader();
                StructFromBytes(ref dfuHeader, fileData, 0);

                if (verbosity >= VerbosityLevel.Normal)
                {
                    Console.WriteLine(" OK");
                }

                ////////////////////
                // validate DFU file

                if (verbosity >= VerbosityLevel.Normal)
                {
                    Console.Write("Validating DFU file...");
                }

                // sanity check on the prefix
                if (dfuHeader.Signature[0] == (byte)'D' &&
                        dfuHeader.Signature[1] == (byte)'f' &&
                        dfuHeader.Signature[2] == (byte)'u' &&
                        dfuHeader.Signature[3] == (byte)'S' &&
                        dfuHeader.Signature[4] == (byte)'e'
                        )
                {
                    // we are good
                }
                else
                {
                    throw new DfuFileException("Bad file header");
                }

                // check DFU file format version
                if (dfuHeader.Version != 1)
                {
                    throw new DfuFileException("DFU file version must be 1");
                }

                // check if there are any targets in the file
                if (dfuHeader.TargetsCount > 0)
                {
                    // we are good
                }
                else
                {
                    throw new DfuFileException("File has no targets");
                }

                // DFU tail at the end of the file
                DfuTail dfuTail = new DfuTail();
                StructFromBytes(ref dfuTail, fileData, fileData.Length - Marshal.SizeOf(dfuTail));

                // check signature
                if (dfuTail.Signature[0] == (byte)'U' &&
                        dfuTail.Signature[1] == (byte)'F' &&
                        dfuTail.Signature[2] == (byte)'D')
                {
                    // we are good
                }
                else
                {
                    throw new DfuFileException("Wrong file signature");
                }

                // check declared length and DFU version
                if (16 != dfuTail.Length ||
                        0x011a != dfuTail.DfuVersion)
                {
                    throw new DfuFileException("Wrong tail size or version");
                }

                // check CRC
                if (dfuTail.Crc != CalculateCRC(fileData))
                {
                    throw new DfuFileException("Bad CRC");
                }

                // get VID, PID and version number
                dfuFile.Vid = dfuTail.Vid;
                dfuFile.Pid = dfuTail.Pid;
                dfuFile.Version = dfuTail.Version;

                if (verbosity >= VerbosityLevel.Normal)
                {
                    Console.WriteLine(" OK");
                }

                /////////////////////////
                // now parse the DFU file

                if (verbosity >= VerbosityLevel.Normal)
                {
                    Console.Write("Parsing DFU file...");
                }

                // build targets array
                dfuFile.DfuTargets = new DfuTarget[dfuHeader.TargetsCount];

                // loop through each target, then loop through each image element
                int targetCursor = Marshal.SizeOf(typeof(DfuHeader));

                for (int targetIndex = 0; targetIndex < dfuFile.DfuTargets.Length; ++targetIndex)
                {
                    dfuFile.DfuTargets[targetIndex] = new DfuTarget();
                    DfuTarget dfuTarget = dfuFile.DfuTargets[targetIndex];

                    DfuImageTarget dfuImageTarget = new DfuImageTarget();

                    StructFromBytes(ref dfuImageTarget, fileData, targetCursor);
                    
                    // check signature
                    if (dfuImageTarget.Signature[0] == (byte)'T' &&
                            dfuImageTarget.Signature[1] == (byte)'a' &&
                            dfuImageTarget.Signature[2] == (byte)'r' &&
                            dfuImageTarget.Signature[3] == (byte)'g' &&
                            dfuImageTarget.Signature[4] == (byte)'e' &&
                            dfuImageTarget.Signature[5] == (byte)'t')
                    {
                        // we are good
                    }
                    else
                    {
                        throw new DfuFileException($"Bad signature for target { targetIndex } @ position { targetCursor }");
                    }

                    // get target name, if set
                    if (dfuImageTarget.IsNamed > 0)
                    {
                        // this requires a bit of processing to read the string with the name
                        int nameLenght = 0;
                        byte* pRawBuffer = dfuImageTarget.TargetName;

                        // move through buffer until a \0 (terminator) is found
                        // target name is 255 chars (max)
                        for (int i = 0; i < 255; i++)
                        {
                            if (*pRawBuffer++ == 0)
                            {
                                nameLenght = i;
                                break;
                            }
                        }

                        // load byte array from pointer
                        byte[] nameBuffer = new byte[nameLenght];
                        pRawBuffer = dfuImageTarget.TargetName;
                        for (int i = 0; i < nameLenght; i++)
                        {
                            nameBuffer[i] = *pRawBuffer++;
                        }

                        dfuTarget.Name = Encoding.ASCII.GetString(nameBuffer, 0, nameLenght);
                    }
                    else
                    {
                        dfuTarget.Name = "";
                    }

                    if (dfuImageTarget.NumElements == 0)
                    {
                        throw new DfuFileException($"Target { targetIndex } has no elements");
                    }
                    else
                    {
                        dfuTarget.DfuElements = new DfuElement[dfuImageTarget.NumElements];

                        int elementCursor = targetCursor + Marshal.SizeOf(typeof(DfuImageTarget));

                        for (int elementIndex = 0; elementIndex < dfuTarget.DfuElements.Length; ++elementIndex)
                        {
                            dfuTarget.DfuElements[elementIndex] = new DfuElement();
                            DfuElement dfuElem = dfuTarget.DfuElements[elementIndex];

                            DfuImageElement dfuImageElement = new DfuImageElement();
                            StructFromBytes(ref dfuImageElement, fileData, elementCursor);

                            dfuElem.Address = dfuImageElement.Address;
                            dfuElem.Data = fileData.Skip(elementCursor + Marshal.SizeOf(typeof(DfuImageElement))).Take((int)dfuImageElement.Size).ToArray();
                            elementCursor += Marshal.SizeOf(typeof(DfuImageElement)) + (int)dfuImageElement.Size;
                        }
                    }

                    targetCursor += Marshal.SizeOf(typeof(DfuImageTarget)) + (int)dfuImageTarget.TargetSize;
                }

                if (verbosity >= VerbosityLevel.Normal)
                {
                    Console.WriteLine(" OK");
                }

                return dfuFile;
            }
            catch (Exception ex)
            {
                throw new DfuFileException("DFU file read failed. " + ex.Message);
            }
        }

        private static uint CalculateCRC(byte[] data)
        {
            uint crcValue = 0xFFFFFFFF;
            int i;

            for (i = 0; i < data.Length - 4; i++)
            {
                crcValue = _crcTable[((crcValue) ^ (data[i])) & 0xff] ^ ((crcValue) >> 8);
            }

            return crcValue;
        }

        #region CrcTable

        private static readonly uint[] _crcTable = {
        0x00000000, 0x77073096, 0xee0e612c, 0x990951ba, 0x076dc419, 0x706af48f,
        0xe963a535, 0x9e6495a3, 0x0edb8832, 0x79dcb8a4, 0xe0d5e91e, 0x97d2d988,
        0x09b64c2b, 0x7eb17cbd, 0xe7b82d07, 0x90bf1d91, 0x1db71064, 0x6ab020f2,
        0xf3b97148, 0x84be41de, 0x1adad47d, 0x6ddde4eb, 0xf4d4b551, 0x83d385c7,
        0x136c9856, 0x646ba8c0, 0xfd62f97a, 0x8a65c9ec, 0x14015c4f, 0x63066cd9,
        0xfa0f3d63, 0x8d080df5, 0x3b6e20c8, 0x4c69105e, 0xd56041e4, 0xa2677172,
        0x3c03e4d1, 0x4b04d447, 0xd20d85fd, 0xa50ab56b, 0x35b5a8fa, 0x42b2986c,
        0xdbbbc9d6, 0xacbcf940, 0x32d86ce3, 0x45df5c75, 0xdcd60dcf, 0xabd13d59,
        0x26d930ac, 0x51de003a, 0xc8d75180, 0xbfd06116, 0x21b4f4b5, 0x56b3c423,
        0xcfba9599, 0xb8bda50f, 0x2802b89e, 0x5f058808, 0xc60cd9b2, 0xb10be924,
        0x2f6f7c87, 0x58684c11, 0xc1611dab, 0xb6662d3d, 0x76dc4190, 0x01db7106,
        0x98d220bc, 0xefd5102a, 0x71b18589, 0x06b6b51f, 0x9fbfe4a5, 0xe8b8d433,
        0x7807c9a2, 0x0f00f934, 0x9609a88e, 0xe10e9818, 0x7f6a0dbb, 0x086d3d2d,
        0x91646c97, 0xe6635c01, 0x6b6b51f4, 0x1c6c6162, 0x856530d8, 0xf262004e,
        0x6c0695ed, 0x1b01a57b, 0x8208f4c1, 0xf50fc457, 0x65b0d9c6, 0x12b7e950,
        0x8bbeb8ea, 0xfcb9887c, 0x62dd1ddf, 0x15da2d49, 0x8cd37cf3, 0xfbd44c65,
        0x4db26158, 0x3ab551ce, 0xa3bc0074, 0xd4bb30e2, 0x4adfa541, 0x3dd895d7,
        0xa4d1c46d, 0xd3d6f4fb, 0x4369e96a, 0x346ed9fc, 0xad678846, 0xda60b8d0,
        0x44042d73, 0x33031de5, 0xaa0a4c5f, 0xdd0d7cc9, 0x5005713c, 0x270241aa,
        0xbe0b1010, 0xc90c2086, 0x5768b525, 0x206f85b3, 0xb966d409, 0xce61e49f,
        0x5edef90e, 0x29d9c998, 0xb0d09822, 0xc7d7a8b4, 0x59b33d17, 0x2eb40d81,
        0xb7bd5c3b, 0xc0ba6cad, 0xedb88320, 0x9abfb3b6, 0x03b6e20c, 0x74b1d29a,
        0xead54739, 0x9dd277af, 0x04db2615, 0x73dc1683, 0xe3630b12, 0x94643b84,
        0x0d6d6a3e, 0x7a6a5aa8, 0xe40ecf0b, 0x9309ff9d, 0x0a00ae27, 0x7d079eb1,
        0xf00f9344, 0x8708a3d2, 0x1e01f268, 0x6906c2fe, 0xf762575d, 0x806567cb,
        0x196c3671, 0x6e6b06e7, 0xfed41b76, 0x89d32be0, 0x10da7a5a, 0x67dd4acc,
        0xf9b9df6f, 0x8ebeeff9, 0x17b7be43, 0x60b08ed5, 0xd6d6a3e8, 0xa1d1937e,
        0x38d8c2c4, 0x4fdff252, 0xd1bb67f1, 0xa6bc5767, 0x3fb506dd, 0x48b2364b,
        0xd80d2bda, 0xaf0a1b4c, 0x36034af6, 0x41047a60, 0xdf60efc3, 0xa867df55,
        0x316e8eef, 0x4669be79, 0xcb61b38c, 0xbc66831a, 0x256fd2a0, 0x5268e236,
        0xcc0c7795, 0xbb0b4703, 0x220216b9, 0x5505262f, 0xc5ba3bbe, 0xb2bd0b28,
        0x2bb45a92, 0x5cb36a04, 0xc2d7ffa7, 0xb5d0cf31, 0x2cd99e8b, 0x5bdeae1d,
        0x9b64c2b0, 0xec63f226, 0x756aa39c, 0x026d930a, 0x9c0906a9, 0xeb0e363f,
        0x72076785, 0x05005713, 0x95bf4a82, 0xe2b87a14, 0x7bb12bae, 0x0cb61b38,
        0x92d28e9b, 0xe5d5be0d, 0x7cdcefb7, 0x0bdbdf21, 0x86d3d2d4, 0xf1d4e242,
        0x68ddb3f8, 0x1fda836e, 0x81be16cd, 0xf6b9265b, 0x6fb077e1, 0x18b74777,
        0x88085ae6, 0xff0f6a70, 0x66063bca, 0x11010b5c, 0x8f659eff, 0xf862ae69,
        0x616bffd3, 0x166ccf45, 0xa00ae278, 0xd70dd2ee, 0x4e048354, 0x3903b3c2,
        0xa7672661, 0xd06016f7, 0x4969474d, 0x3e6e77db, 0xaed16a4a, 0xd9d65adc,
        0x40df0b66, 0x37d83bf0, 0xa9bcae53, 0xdebb9ec5, 0x47b2cf7f, 0x30b5ffe9,
        0xbdbdf21c, 0xcabac28a, 0x53b39330, 0x24b4a3a6, 0xbad03605, 0xcdd70693,
        0x54de5729, 0x23d967bf, 0xb3667a2e, 0xc4614ab8, 0x5d681b02, 0x2a6f2b94,
        0xb40bbe37, 0xc30c8ea1, 0x5a05df1b, 0x2d02ef8d  };

        #endregion

        internal static void PartialErase(
            IntPtr hDevice,
            uint startAddress,
            uint size, 
            List<MappingSector> mapSector,
            bool outputMessages = false)
        {
            foreach (MappingSector s in mapSector)
            {
                if ((startAddress < s.StartAddress + s.SectorSize) && (startAddress + size > s.StartAddress))
                {
                    EraseSector(hDevice, s.StartAddress);
                }
            }
        }

        private static void EraseSector(
            IntPtr hDevice,
            uint Address)
        {
            DfuStatus dfuStatus = new DfuStatus();

            uint returnValue;
            byte[] Command = { 0x41, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };

            Command[1] = (byte)(Address & 0xFF);
            Command[2] = (byte)((Address >> 8) & 0xFF);
            Command[3] = (byte)((Address >> 16) & 0xFF);
            Command[4] = (byte)((Address >> 24) & 0xFF);

            returnValue = STDFU_SelectCurrentConfiguration(ref hDevice, 0, 0, 0);
            if (returnValue == STDFU_NOERROR)
            {
                STDFU_GetStatus(ref hDevice, ref dfuStatus);

                while (dfuStatus.bState != STATE_DFU_IDLE)
                {
                    STDFU_ClrStatus(ref hDevice);
                    STDFU_GetStatus(ref hDevice, ref dfuStatus);
                }

                returnValue = STDFU_Dnload(ref hDevice, Command, 5, 0);
                if (returnValue == STDFU_NOERROR)
                {
                    STDFU_GetStatus(ref hDevice, ref dfuStatus);

                    while (dfuStatus.bState != STATE_DFU_IDLE)
                    {
                        STDFU_ClrStatus(ref hDevice);
                        STDFU_GetStatus(ref hDevice, ref dfuStatus);
                    }
                }
                else
                {
                    throw new DownloadException("STDFU_Dnload returned " + returnValue.ToString("X8"));
                }
            }
            else
            {
                throw new SelectConfigurationException("STDFU_SelectCurrentConfiguration returned " + returnValue.ToString("X8"));
            }
        }

        internal static void MassErase(
            IntPtr hDevice,
            bool outputMessages = false)
        {
            DfuStatus dfuStatus = new DfuStatus();

            byte[] EraseCommand = { 0x41, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };

            uint returnValue = STDFU_SelectCurrentConfiguration(ref hDevice, 0, 0, 1);
            if (returnValue == STDFU_NOERROR)
            {
                STDFU_GetStatus(ref hDevice, ref dfuStatus);

                while (dfuStatus.bState != STATE_DFU_IDLE)
                {
                    STDFU_ClrStatus(ref hDevice);
                    STDFU_GetStatus(ref hDevice, ref dfuStatus);
                }

                returnValue = STDFU_Dnload(ref hDevice, EraseCommand, 1, 0);
                if (returnValue == STDFU_NOERROR)
                {
                    STDFU_GetStatus(ref hDevice, ref dfuStatus);
                    while (dfuStatus.bState != STATE_DFU_IDLE)
                    {
                        STDFU_ClrStatus(ref hDevice);
                        STDFU_GetStatus(ref hDevice, ref dfuStatus);
                    }
                }
                else
                {
                    throw new DownloadException($"{returnValue.ToString("X8")}");
                }
            }
            else
            {
                throw new SelectConfigurationException($"{returnValue.ToString("X8")}");
            }
        }

        internal static void WriteBlock(
            IntPtr hDevice,
            uint address, 
            byte[] data,
            uint blockNumber)
        {
            DfuStatus dfuStatus = new DfuStatus();

            if (0 == blockNumber)
            {
                SetAddressPointer(hDevice, address);
            }

            STDFU_GetStatus(ref hDevice, ref dfuStatus);
            while (dfuStatus.bState != STATE_DFU_IDLE)
            {
                STDFU_ClrStatus(ref hDevice);
                STDFU_GetStatus(ref hDevice, ref dfuStatus);
            }

            STDFU_Dnload(ref hDevice, data, (uint)data.Length, (ushort)(blockNumber + 2));

            STDFU_GetStatus(ref hDevice, ref dfuStatus);
            while (dfuStatus.bState != STATE_DFU_IDLE)
            {
                STDFU_ClrStatus(ref hDevice);
                STDFU_GetStatus(ref hDevice, ref dfuStatus);
            }
        }

        private static void SetAddressPointer(
            IntPtr hDevice,
            uint address)
        {
            byte[] command = new byte[5];
            DfuStatus dfuStatus = new DfuStatus();

            STDFU_GetStatus(ref hDevice, ref dfuStatus);
            while (dfuStatus.bState != STATE_DFU_IDLE)
            {
                STDFU_ClrStatus(ref hDevice);
                STDFU_GetStatus(ref hDevice, ref dfuStatus);
            }

            command[0] = 0x21;
            command[1] = (byte)(address & 0xFF);
            command[2] = (byte)((address >> 8) & 0xFF);
            command[3] = (byte)((address >> 16) & 0xFF);
            command[4] = (byte)((address >> 24) & 0xFF);

            STDFU_Dnload(ref hDevice, command, 5, 0);

            STDFU_GetStatus(ref hDevice, ref dfuStatus);
            while (dfuStatus.bState != STATE_DFU_IDLE)
            {
                STDFU_ClrStatus(ref hDevice);
                STDFU_GetStatus(ref hDevice, ref dfuStatus);
            }
        }

        internal static void Detach(IntPtr hDevice, uint address)
        {
            DfuStatus dfuStatus = new DfuStatus();

            STDFU_GetStatus(ref hDevice, ref dfuStatus);
            while (dfuStatus.bState != STATE_DFU_IDLE)
            {
                STDFU_ClrStatus(ref hDevice);
                STDFU_GetStatus(ref hDevice, ref dfuStatus);
            }

            byte[] command = new byte[5];
            command[0] = 0x21;
            command[1] = (byte)(address & 0xFF);
            command[2] = (byte)((address >> 8) & 0xFF);
            command[3] = (byte)((address >> 16) & 0xFF);
            command[4] = (byte)((address >> 24) & 0xFF);

            // set command pointer to the launch address
            STDFU_Dnload(ref hDevice, command, 5, 0);

            STDFU_GetStatus(ref hDevice, ref dfuStatus);
            while (dfuStatus.bState != STATE_DFU_IDLE)
            {
                STDFU_ClrStatus(ref hDevice);
                STDFU_GetStatus(ref hDevice, ref dfuStatus);
            }

            // send DFU detach command
            STDFU_Dnload(ref hDevice, command, 0, 0);

            STDFU_GetStatus(ref hDevice, ref dfuStatus);
            STDFU_ClrStatus(ref hDevice);
            STDFU_GetStatus(ref hDevice, ref dfuStatus);
        }
    }


    #region Exceptions

    [Serializable]
    internal class SelectConfigurationException : Exception
    {
        public SelectConfigurationException()
        {
        }

        public SelectConfigurationException(string message) : base(message)
        {
        }

        public SelectConfigurationException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected SelectConfigurationException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }

    [Serializable]
    internal class DownloadException : Exception
    {
        public DownloadException()
        {
        }

        public DownloadException(string message) : base(message)
        {
        }

        public DownloadException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected DownloadException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }

    [Serializable]
    internal class DfuFileException : Exception
    {
        public DfuFileException()
        {
        }

        public DfuFileException(string message) : base(message)
        {
        }

        public DfuFileException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected DfuFileException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }

    [Serializable]
    internal class WrongOrInvalidStringDescriptorException : Exception
    {
        public WrongOrInvalidStringDescriptorException()
        {
        }

        public WrongOrInvalidStringDescriptorException(string message) : base(message)
        {
        }

        public WrongOrInvalidStringDescriptorException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected WrongOrInvalidStringDescriptorException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }

    [Serializable]
    internal class ErrorGettingStringDescriptorException : Exception
    {
        public ErrorGettingStringDescriptorException()
        {
        }

        public ErrorGettingStringDescriptorException(string message) : base(message)
        {
        }

        public ErrorGettingStringDescriptorException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected ErrorGettingStringDescriptorException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }

    [Serializable]
    internal class ErrorGettingInterfaceDescriptorException : Exception
    {
        public ErrorGettingInterfaceDescriptorException()
        {
        }

        public ErrorGettingInterfaceDescriptorException(string message) : base(message)
        {
        }

        public ErrorGettingInterfaceDescriptorException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected ErrorGettingInterfaceDescriptorException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }

    #endregion
}
