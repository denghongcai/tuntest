using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TunTest
{
    class TCPForwarder
    {
        private FileStream tap;

        private static Logger logger = LogManager.GetCurrentClassLogger();

        public TCPForwarder(FileStream tap)
        {
            this.tap = tap;
        }

        public void forwardFrame(IPFrame frame)
        {
        }
    }
}
