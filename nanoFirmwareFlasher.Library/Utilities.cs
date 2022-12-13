//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System.IO;
using System.Reflection;

namespace nanoFramework.Tools.FirmwareFlasher
{
    internal class Utilities
    {
        public static string ExecutingPath;

        static Utilities()
        {
            // need this to be able to use ProcessStart at the location where the .NET Core CLI tool is running from
            string codeBase = System.AppContext.BaseDirectory;
            var fullPath = Path.GetFullPath(codeBase);
            ExecutingPath = Path.GetDirectoryName(fullPath);
        }
    }
}
