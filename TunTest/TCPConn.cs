using eExNetworkLibrary;
using eExNetworkLibrary.IP;
using eExNetworkLibrary.TCP;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Tun2Any
{
    class TCPConn
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();

        enum State { LISTEN, SYN_RCVD, ESTABLISHED };

        private State state = State.LISTEN;

        private uint sequenceNumber = 0;
        private uint acknowledgementNumber;

        private FileStream tap;

        private string id;

        private Socket socket;

        private System.Net.IPEndPoint s;
        private System.Net.IPEndPoint e;

        private object thisLock = new object();

        private BlockingCollection<TCPFrame> receivedFrameQueue = new BlockingCollection<TCPFrame>();

        public TCPConn(string id, System.Net.IPEndPoint s, System.Net.IPEndPoint e, FileStream tap)
        {
            this.id = id;
            this.s = s;
            this.e = e;
            this.tap = tap;
        }

        
        public void Start()
        {
            Task.Run(() =>
            {
                TCPFrame tcpFrame;
                while (receivedFrameQueue.TryTake(out tcpFrame, TimeSpan.FromSeconds(30)))
                {
                    logger.Debug("{0} receive frame, SEQ: {1}, ACK: {2}", id, tcpFrame.SequenceNumber, tcpFrame.AcknowledgementNumber);
                    TCPFrame responseTCPFrame;
                    if (getNextFrame(tcpFrame, out responseTCPFrame))
                    {
                        var responseIPFrame = new IPv4Frame();
                        responseIPFrame.SourceAddress = e.Address;
                        responseIPFrame.DestinationAddress = s.Address;
                        responseIPFrame.Protocol = IPProtocol.TCP;
                        responseIPFrame.EncapsulatedFrame = responseTCPFrame;
                        responseTCPFrame.Checksum = responseTCPFrame.CalculateChecksum(responseIPFrame.GetPseudoHeader());
                        logger.Debug("{0} send frame to {1}:{2} from {3}:{4}", 
                            id, 
                            responseIPFrame.DestinationAddress, responseTCPFrame.DestinationPort,
                            responseIPFrame.SourceAddress, responseTCPFrame.SourcePort);
                        tap.Write(responseIPFrame.FrameBytes, 0, responseIPFrame.Length);
                        tap.Flush();
                    }
                }
            });
        }

        public void Receive(TCPFrame frame)
        {
            receivedFrameQueue.Add(frame);
        }

        private void End()
        {
            receivedFrameQueue.CompleteAdding();
        }

        private bool getNextFrame(TCPFrame frame, out TCPFrame nextFrame)
        {
            nextFrame = new TCPFrame(frame.FrameBytes);
            nextFrame.SourcePort = frame.DestinationPort;
            nextFrame.DestinationPort = frame.SourcePort;
            lock (thisLock)
            {
                switch (state)
                {
                    case State.LISTEN:
                        {
                            if (frame.SynchronizeFlagSet)
                            {
                                nextFrame.AcknowledgementFlagSet = true;
                                nextFrame.AcknowledgementNumber = frame.SequenceNumber + 1;
                                acknowledgementNumber = nextFrame.AcknowledgementNumber;
                                nextFrame.SequenceNumber = sequenceNumber;
                                logger.Debug("{0} State change to SYN_RCVD", id);
                                state = State.SYN_RCVD;


                                socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                                try
                                {
                                    Task.Run(() =>
                                    {
                                        var ae = new System.Net.IPEndPoint(System.Net.IPAddress.Parse("192.168.1.220"), e.Port);
                                        socket.Connect(ae);
                                        byte[] buffer = new byte[1024];
                                        int bytesReceived;
                                        while ((bytesReceived = socket.Receive(buffer)) > 0)
                                        {
                                            var dataFrame = new TCPFrame();
                                            dataFrame.SequenceNumber = sequenceNumber;
                                            dataFrame.AcknowledgementNumber = acknowledgementNumber;
                                            dataFrame.EncapsulatedFrame = new RawDataFrame(buffer, 0, bytesReceived);
                                            sequenceNumber = dataFrame.SequenceNumber + (uint)bytesReceived;
                                            dataFrame.PushFlagSet = true;
                                            dataFrame.AcknowledgementFlagSet = true;
                                            dataFrame.Urgent = true;
                                            dataFrame.SourcePort = s.Port;
                                            dataFrame.DestinationPort = e.Port;
                                            Receive(dataFrame);
                                        }
                                    });
                                }
                                catch (Exception e)
                                {
                                    logger.Debug(e);
                                    nextFrame.ResetFlagSet = true;
                                    return true;
                                }
                            }
                            else
                            {
                                if (socket != null)
                                {
                                    socket.Close();
                                    socket = null;
                                }
                                nextFrame.ResetFlagSet = true;
                            }
                            return true;
                        }
                    case State.SYN_RCVD:
                        {
                            if (frame.AcknowledgementFlagSet &&
                                frame.SequenceNumber == acknowledgementNumber &&
                                frame.AcknowledgementNumber == sequenceNumber + 1)
                            {
                                logger.Debug("{0} State change to ESTABLISHED", id);
                                state = State.ESTABLISHED;
                                nextFrame = null;
                                return false;
                            }
                            else
                            {
                                if (socket != null)
                                {
                                    socket.Close();
                                    socket = null;
                                }
                                nextFrame.ResetFlagSet = true;
                                return true;
                            }
                        }
                    case State.ESTABLISHED:
                        {
                            if (frame.AcknowledgementFlagSet)
                            {
                                if (frame.FinishFlagSet)
                                {
                                    if (socket != null)
                                    {
                                        socket.Close();
                                        socket = null;
                                    }
                                    nextFrame.SequenceNumber = frame.AcknowledgementNumber;
                                    nextFrame.AcknowledgementNumber = frame.SequenceNumber + 1;
                                    acknowledgementNumber = nextFrame.AcknowledgementNumber;
                                    return true;
                                }
                                else if (frame.Urgent)
                                {
                                    return true;
                                }
                                else
                                {
                                    nextFrame.SequenceNumber = frame.AcknowledgementNumber;
                                    nextFrame.AcknowledgementNumber = frame.SequenceNumber + Convert.ToUInt32(frame.EncapsulatedFrame.Length);
                                    if (acknowledgementNumber >= nextFrame.AcknowledgementNumber)
                                    {
                                        return false;
                                    }
                                    acknowledgementNumber = nextFrame.AcknowledgementNumber;
                                    nextFrame.PushFlagSet = false;
                                    nextFrame.EncapsulatedFrame = null;
                                    socket.Send(frame.EncapsulatedFrame.FrameBytes);
                                    logger.Debug("{0} receive frame, response with SEQ: {1}, ACK: {2}", id, nextFrame.SequenceNumber, nextFrame.AcknowledgementNumber);
                                    return true;
                                }
                            }
                            else
                            {
                                if (socket != null)
                                {
                                    socket.Close();
                                    socket = null;
                                }
                                nextFrame.ResetFlagSet = true;
                                return true;
                            }
                        }
                    default:
                        {
                            if (socket != null)
                            {
                                socket.Close();
                                socket = null;
                            }
                            nextFrame.ResetFlagSet = true;
                            return true;
                        }
                }
            }
        }
    }
}
