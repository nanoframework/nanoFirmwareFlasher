//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

namespace nanoFramework.Tools.FirmwareFlasher
{
    internal class Utilities
    {
        public static string ExecutingPath;

        static Utilities()
        {
            // need this to be able to use ProcessStart at the location where the .NET Core CLI tool is running from
            string codeBase = Assembly.GetExecutingAssembly().Location;
            var fullPath = Path.GetFullPath(codeBase);
            ExecutingPath = Path.GetDirectoryName(fullPath);
        }


        /// <summary>
        /// Takes a pathname that MAY be relative and makes it absolute. If the path is already absolute, it is left alone.
        /// </summary>
        public static string MakePathAbsolute(
            string baseFolder,
            string possiblyRelativePathname)
        {
            // Should work on UN*X or Windows
            string path;

            if (Regex.IsMatch(possiblyRelativePathname, @"^(/|[a-zA-Z]:)"))
            {
                path = possiblyRelativePathname;
            }
            else
            {
                path = Path.Combine(baseFolder, possiblyRelativePathname);
            }

            return path.Replace(@"\.\", @"\");
        }

    }
}
