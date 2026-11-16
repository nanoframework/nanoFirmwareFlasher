// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace nanoFramework.Tools.FirmwareFlasher.Swd
{
    internal sealed class Stm32ExternalFlashSession : IDisposable
    {
        private readonly SwdProtocol _swd;
        private readonly Stm32QspiNorProgrammer _programmer;
        private readonly uint _chipId;
        private readonly VerbosityLevel _verbosity;
        private bool _prepared;

        internal Stm32ExternalFlashSession(SwdProtocol swd, ArmMemAp mem, uint chipId,
            Action<string, int, int> progress, VerbosityLevel verbosity)
        {
            _swd = swd;
            _chipId = chipId;
            _verbosity = verbosity;
            _programmer = new Stm32QspiNorProgrammer(mem) { ProgressReport = progress };
        }

        internal void Prepare(byte[] image)
        {
            Stm32WifiImage.Validate(Stm32WifiImage.Address, image);
            if ((_chipId & 0xFFF) != 0x441)
            {
                throw new SwdProtocolException("MXCHIP external flash requires an STM32F412 target.");
            }

            if (_prepared)
            {
                return;
            }

            _prepared = true;
            try
            {
                _swd.ResetAndHalt();
                _programmer.Initialize(_chipId);
            }
            catch (SwdProtocolException)
            {
                Dispose();
                throw;
            }

            if (_verbosity >= VerbosityLevel.Detailed)
            {
                OutputWriter.WriteLine($"External Wi-Fi NOR JEDEC ID (LSB first): 0x{_programmer.JedecId:X6}");
            }
        }

        internal void Program(uint address, byte[] image)
        {
            Stm32WifiImage.Validate(address, image);
            Prepare(image);
            bool written = _programmer.Program(image);
            _programmer.Cleanup();
            _prepared = false;
            if (_verbosity >= VerbosityLevel.Normal)
            {
                OutputWriter.WriteLine(written
                    ? $"External Wi-Fi firmware programmed and verified ({image.Length:N0} bytes)."
                    : "External Wi-Fi firmware unchanged; verified, skipping erase/program.");
            }
        }

        public void Dispose()
        {
            if (!_prepared)
            {
                return;
            }

            _prepared = false;
            try
            {
                _programmer.Cleanup();
            }
            catch (SwdProtocolException ex)
            {
                OutputWriter.WriteLine($"ERROR: External-flash cleanup failed: {ex.Message}");
            }

            try
            {
                _swd.SystemReset();
            }
            catch (SwdProtocolException ex)
            {
                OutputWriter.WriteLine($"ERROR: Reset after external-flash failure failed: {ex.Message}");
            }
        }
    }
}
