//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace nanoFramework.Tools.FirmwareFlasher
{
    /// <summary>
    /// Class that handles the download of STM32 firmware files from Cloudsmith.
    /// </summary>
    internal class Stm32Firmware : FirmwarePackage
    {
        [Obsolete("This property is discontinued and it will be removed in a future version.")]
        public bool HasDfuPackage => !string.IsNullOrEmpty(DfuPackage);

        public string NanoBooterFile { get; private set; }

        public string NanoClrFile { get; private set; }

        public string DfuPackage { get; private set; }

        public Stm32Firmware(
            string targetName,
            string fwVersion,
            bool preview)
            :base(
                 targetName,
                 fwVersion,
                 preview)
        {
        }

        internal new async System.Threading.Tasks.Task<ExitCodes> DownloadAndExtractAsync()
        {
            // perform download and extract
            var executionResult = await base.DownloadAndExtractAsync();

            if (executionResult == ExitCodes.OK)
            {
                var dfuFile = Directory.EnumerateFiles(LocationPath, "*.dfu").ToArray();
                if (dfuFile.Any())
                {
                    DfuPackage = dfuFile.First();
                }
                NanoBooterFile = Directory.EnumerateFiles(LocationPath, "nanoBooter.hex").FirstOrDefault();
                NanoClrFile = Directory.EnumerateFiles(LocationPath, "nanoCLR.hex").FirstOrDefault();
            }

            return executionResult;
        }

        public uint GetdBooterStartAddress()
        {
            uint address;

            // find out what's the CLR block start

            // do this by reading the HEX format file...
            var textLines = File.ReadAllLines(NanoBooterFile);

            // ... and decoding the start address
            var addressRecord = textLines.FirstOrDefault();

            // 1st line is an Extended Segment Address Records (HEX86)
            // format ":02000004FFFFFC"

            // perform sanity checks
            if (addressRecord == null ||
                addressRecord.Length != 15 ||
                addressRecord.Substring(0, 9) != ":02000004")
            {
                // wrong format
                throw new FormatException("Wrong data in nanoBooter file");
            }

            // looking good, grab the upper 16bits
            address = (uint)int.Parse(addressRecord.Substring(9, 4), System.Globalization.NumberStyles.HexNumber);
            address <<= 16;

            // now the 2nd line to get the lower 16 bits of the address
            addressRecord = textLines.Skip(1).FirstOrDefault();

            // 2nd line is a Data Record
            // format ":10246200464C5549442050524F46494C4500464C33"

            // perform sanity checks
            if (addressRecord == null ||
                addressRecord.Substring(0, 1) != ":" ||
                addressRecord.Length < 7)
            {
                // wrong format
                throw new FormatException("Wrong data in nanoBooter file");
            }

            // looking good, grab the lower 16bits
            address += (uint)int.Parse(addressRecord.Substring(3, 4), System.Globalization.NumberStyles.HexNumber);

            return address;
        }
    }
}
