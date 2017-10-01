using Caching;
using eExNetworkLibrary.IP;
using eExNetworkLibrary.TCP;
using NLog;
using Org.Mentalis.Network.ProxySocket;
using System;
using System.IO;
using System.Linq;

namespace Tun2Any
{
    class TCPForwarder
    {

        private FileStream tap;

        private static Logger logger = LogManager.GetCurrentClassLogger();

        private LRUCache<string, TCPConn> natTable = new LRUCache<string, TCPConn>(capacity: 10240, seconds: 30, refreshEntries: false);
        
        private static Random random = new Random();

        public TCPForwarder(FileStream tap)
        {
            this.tap = tap;
        }

        public static string RandomString(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            return new string(Enumerable.Repeat(chars, length)
              .Select(s => s[random.Next(s.Length)]).ToArray());
        }

        public void forwardFrame(IPFrame frame)
        {
            var id = RandomString(10);
            var tcpFrame = (TCPFrame)frame.EncapsulatedFrame;
            //var e = new System.Net.IPEndPoint(System.Net.IPAddress.Parse("192.168.1.220"), tcpFrame.DestinationPort);
            var e = new System.Net.IPEndPoint(frame.DestinationAddress, tcpFrame.DestinationPort);
            logger.Debug("{0} Source Port: {1}", id, tcpFrame.SourcePort);
            logger.Debug("{0} Dest Port: {1}", id, tcpFrame.DestinationPort);
            TCPConn conn;
            var cacheKey = string.Format("{0}:{1}->{2}", tcpFrame.SourcePort, tcpFrame.DestinationPort, e.ToString());

            if (!natTable.TryGetValue(cacheKey, out conn))
            {
                var s = new System.Net.IPEndPoint(frame.SourceAddress, tcpFrame.SourcePort);
                conn = new TCPConn(id, s, e, tap);
                conn.Start();
            }
            natTable.Add(cacheKey, conn);
            conn.Receive(tcpFrame);
        }
    }
}
