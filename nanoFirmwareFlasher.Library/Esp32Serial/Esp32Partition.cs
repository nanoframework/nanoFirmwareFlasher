// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace nanoFramework.Tools.FirmwareFlasher.Esp32Serial
{
    internal sealed class Esp32Partition
    {
        internal string Label { get; }
        internal byte Type { get; }
        internal byte Subtype { get; }
        internal int Address { get; }
        internal int Size { get; }
        internal uint Flags { get; }

        internal Esp32Partition(string label, byte type, byte subtype, int address, int size, uint flags)
        {
            Label = label;
            Type = type;
            Subtype = subtype;
            Address = address;
            Size = size;
            Flags = flags;
        }
    }
}
