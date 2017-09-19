using eExNetworkLibrary;
using eExNetworkLibrary.IP;
using eExNetworkLibrary.UDP;
using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using NLog;
using Caching;
using Org.Mentalis.Network.ProxySocket;
using System.Threading.Tasks;

namespace TunTest
{
    class UDPForwarder
    {
        private FileStream tap;

        private LRUCache<string, ProxySocket> natTable = new LRUCache<string, ProxySocket>(capacity: 10240, seconds: 30, refreshEntries: false);

        private static Logger logger = LogManager.GetCurrentClassLogger();

        private static Random random = new Random();
        public static string RandomString(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            return new string(Enumerable.Repeat(chars, length)
              .Select(s => s[random.Next(s.Length)]).ToArray());
        }

        public UDPForwarder(FileStream tap)
        {
            this.tap = tap;
        }

        public void forwardFrame(IPFrame frame)
        {
            var id = RandomString(10);
            var udpFrame = (UDPFrame)frame.EncapsulatedFrame;
            var e = new System.Net.IPEndPoint(System.Net.IPAddress.Parse("192.168.1.220"), udpFrame.DestinationPort);
            logger.Debug("{0} Source Port: {1}", id, udpFrame.SourcePort);
            logger.Debug("{0} Dest Port: {1}", id, udpFrame.DestinationPort);
            ProxySocket socket;
            var cacheKey = string.Format("{0}:{1}->{2}", udpFrame.SourcePort, udpFrame.DestinationPort, e.ToString());
            if (!natTable.TryGetValue(cacheKey, out socket))
            {
                socket = new ProxySocket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                socket.ProxyEndPoint = new System.Net.IPEndPoint(System.Net.IPAddress.Parse("192.168.1.150"), 1080);
                socket.ProxyType = ProxyTypes.Socks5;
                socket.Connect(e);
                Task.Run(() =>
                {
                    try
                    {
                        logger.Debug("{0} Create a new UDP Receive Task", id);
                        var buffer = new byte[8192];
                        ProxySocket tmp;
                        while (natTable.TryGetValue(cacheKey, out tmp))
                        {
                            logger.Debug("start receive");
                            var bytesReceived = socket.Receive(buffer);
                            logger.Debug("{0} Received packet", id);
                            natTable.Add(cacheKey, socket);
                            var receivedIPFrame = new IPv4Frame();
                            receivedIPFrame.SourceAddress = frame.DestinationAddress;
                            receivedIPFrame.DestinationAddress = frame.SourceAddress;
                            receivedIPFrame.Protocol = IPProtocol.UDP;
                            var receivedUDPFrame = new UDPFrame();
                            receivedUDPFrame.SourcePort = udpFrame.DestinationPort;
                            receivedUDPFrame.DestinationPort = udpFrame.SourcePort;
                            logger.Debug("{0} RSource Port: {1}", id, receivedUDPFrame.SourcePort);
                            logger.Debug("{0} RDest Port: {1}", id, receivedUDPFrame.DestinationPort);
                            receivedUDPFrame.EncapsulatedFrame = new RawDataFrame(buffer, 0, bytesReceived);
                            receivedIPFrame.EncapsulatedFrame = receivedUDPFrame;
                            receivedUDPFrame.Checksum = receivedUDPFrame.CalculateChecksum(receivedIPFrame.GetPseudoHeader());
                            tap.Write(receivedIPFrame.FrameBytes, 0, receivedIPFrame.Length);
                            tap.Flush();
                            logger.Debug("{0} wrote", id);
                        }
                    }
                    catch (SocketException err)
                    {
                        logger.Error(err);
                    }
                });
            }
            natTable.Add(cacheKey, socket);
            socket.BeginSend(udpFrame.EncapsulatedFrame.FrameBytes, 0, udpFrame.EncapsulatedFrame.FrameBytes.Length, 0, ar =>
            {
                socket.EndSend(ar);
                logger.Debug("{0} Sent to Dest", id);
            }, null);
        }
    }
}
