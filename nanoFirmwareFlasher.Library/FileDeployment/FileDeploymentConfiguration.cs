using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace nanoFramework.Tools.FirmwareFlasher.FileDeployment
{
    internal class FileDeploymentConfiguration
    {
        public string SerialPort { get; set; }

        public List<DeploymentFile> Files { get; set; }
    }
}
