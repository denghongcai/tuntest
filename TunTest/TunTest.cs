using System;
using System.IO;
using System.Threading;
using TunTest;
using System.Linq;
using eExNetworkLibrary;
using eExNetworkLibrary.ProtocolParsing;
using eExNetworkLibrary.IP;
using eExNetworkLibrary.UDP;
using eExNetworkLibrary.DNS;
using NLog;

namespace TestTun
{
    /// <summary>
    /// Summary description for Class1.
    /// </summary>
    class TunTap
	{
        private static Logger logger = LogManager.GetCurrentClassLogger();
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
		static void Main(string[] args)
		{
            Device device = new Device("TUNTEST");

            device.setMediaStatusAsConnected();
            device.setTunMode("10.111.111.1", "255.0.0.0");

            Tap = device.getStream();

            var udpForwarder = new UDPForwarder(Tap);
            var tcpForwarder = new TCPForwarder(Tap);

			byte [] buf = new byte[10000];
            object state = new int();
            WaitObject = new EventWaitHandle(false, EventResetMode.AutoReset);
            object state2 = new int();
            WaitObject2 = new EventWaitHandle(false, EventResetMode.AutoReset);
            IAsyncResult res, res2;
            while (true)
			{
				res = Tap.BeginRead(buf, 0, 10000, ar => {
                    BytesRead = Tap.EndRead(ar);
                    logger.Debug("Read " + BytesRead.ToString());
                    try
                    {
                        var frame = IPFrame.Create(buf.Take(BytesRead).ToArray());
                        logger.Debug("Version: " + frame.Version);
                        logger.Debug("Protocol Type: " + frame.Protocol);
                        logger.Debug("Source Address: " + frame.SourceAddress.ToString());
                        logger.Debug("Dest Address: " + frame.DestinationAddress.ToString());

                        var parser = new ProtocolParser();
                        parser.ParseCompleteFrame(frame);

                        if(frame.EncapsulatedFrame.FrameType == FrameTypes.UDP)
                        {
                            udpForwarder.forwardFrame(frame);
                        }
                        else if(frame.EncapsulatedFrame.FrameType == FrameTypes.TCP)
                        {
                            tcpForwarder.forwardFrame(frame);
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.ToString());
                    }
                    WaitObject.Set();
                }, state);

                WaitObject.WaitOne();
                //
                // Reverse IPv4 addresses and send back to tun
                //
                //for (int i = 0; i< 4; ++i)
                //{
                //    byte tmp = buf[12+i]; buf[12+i] = buf[16+i]; buf[16+i] = tmp;
                //}
                //res2 = Tap.BeginWrite(buf, 0, BytesRead, ar => {
                //    Tap.EndWrite(ar);
                //    WaitObject2.Set();
                //}, state2);
                //WaitObject2.WaitOne();
            }
		}

		static EventWaitHandle WaitObject, WaitObject2;
		static int BytesRead;
        static FileStream Tap;
	}
}

