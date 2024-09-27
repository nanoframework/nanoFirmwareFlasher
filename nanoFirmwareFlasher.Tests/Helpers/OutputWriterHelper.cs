// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using nanoFramework.Tools.FirmwareFlasher;

namespace nanoFirmwareFlasher.Tests.Helpers
{
    internal sealed class OutputWriterHelper : IDisposable, OutputWriter.IOutputWriter
    {
        #region Fields
        private ConsoleColor _foregroundColor = ConsoleColor.White;
        private readonly StringBuilder _output = new();
        #endregion

        #region Construction/destruction
        public OutputWriterHelper()
        {
            OutputWriter.SetOutputWriter(this);
        }
        public void Dispose()
        {
            OutputWriter.SetOutputWriter(null);
        }
        #endregion

        #region Properties
        /// <summary>
        /// Get the output so far. Changes of foreground color are coded
        /// as ~`{ColorName}`~.
        /// </summary>
        public string Output
            => _output.ToString();
        #endregion

        #region Test support
        /// <summary>
        /// Reset the writer to its initial state
        /// </summary>
        public void Reset()
        {
            _foregroundColor = ConsoleColor.White;
            _output.Clear();
        }

        /// <summary>
        /// Assert that the output is equal to <paramref name="expected"/>. Both the actual
        /// and expected values are trimmed before the comparison.
        /// </summary>
        /// <param name="expected"></param>
        public void AssertAreEqual(string expected)
        {
            Assert.AreEqual(
                expected.Trim().Replace("\r\n", Environment.NewLine).Replace("\n", Environment.NewLine) + '\n',
                _output.ToString().Trim() + '\n' // extra \n to make output in test results look better
            );
        }
        #endregion

        #region OutputWriter.IOutputWriter implementation
        ConsoleColor OutputWriter.IOutputWriter.ForegroundColor
        {
            get => _foregroundColor;
            set
            {
                if (_foregroundColor != value)
                {
                    _foregroundColor = value;
                    _output.Append($"~`{value}`~");
                    Debug.Write($"~`{value}`~");
                }
            }
        }

        void OutputWriter.IOutputWriter.Write(string text)
        {
            _output.Append(text);
            Debug.Write(text);
        }
        #endregion


    }
}
