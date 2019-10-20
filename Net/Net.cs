using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Nancy.Hosting.Self;
using Nancy;
using Nancy.ModelBinding;
using System.Net;
using System.Collections.Concurrent;

namespace Cidon
{
    public enum TokenType : byte
    {
        Forward = 1,
        Vis = 2,
        Return = 3,
    }

    public class Message
    {
        public Message(uint time, string from, string to, TokenType tok, uint payload)
        {
            this.time = time;
            this.from = from;
            this.to = to;
            this.token = tok;
            this.payload = payload;
        }

        public Message(string msg)
        {
            string[] strTokens = msg.Split(' ');
            time = UInt32.Parse(strTokens[0]);
            from = strTokens[1];
            to = strTokens[2];
            token = (TokenType)Byte.Parse(strTokens[3]);
            payload = UInt32.Parse(strTokens[4]);
        }

        public string Serialize()
        {
            return string.Format("{0} {1} {2} {3} {4}", time, from, to, (byte)token, payload);
        }

        // time from to tok pay
        public uint time;
        public readonly string from;
        public readonly string to;
        public readonly TokenType token;
        public readonly uint payload;
    }

    public class MsgBundle
    {
        public MsgBundle(List<Message> msgList)
        {
            this.msgList = msgList;
        }

        public MsgBundle(string msgBundleStr)
        {
            var msgStrs = msgBundleStr.Split(',');
            foreach (var msgStr in msgStrs)
            {
                msgList.Add(new Message(msgStr));
            }
        }

        public string Serialize()
        {
            var bundleStr = msgList[0].Serialize();
            for (int i = 1; i < msgList.Count; ++i)
            {
                bundleStr += "," + msgList[i].Serialize();
            }
            return bundleStr;
        }

        public readonly List<Message> msgList = new List<Message>();
    }

    public class NetRecvModule : NancyModule
    {
        public NetRecvModule()
        {
            Get("/{msg}", x =>
            {
                var msgBundle = new MsgBundle(x.msg);

                // Add messages to queue along with their time-to-send
                foreach (var msg in msgBundle.msgList)
                {
                    // Print received messages
                    Console.WriteLine(string.Format("... {0} {1} < {2} {3} {4} {5}", Net.time, Net.nodeId, msg.from, msg.to, (byte)msg.token, msg.payload));

                    // Get message delay
                    var delayConfigKey = msg.from + "-" + msg.to;
                    uint delay;
                    if (Net.config.ContainsKey(delayConfigKey))
                        delay = UInt32.Parse(Net.config[delayConfigKey]);
                    else
                        delay = UInt32.Parse(Net.config["-"]);
                    var timeToSend = Net.time + delay;

                    // Add message to queue and sort
                    Net.msgSendQueue.Enqueue(Tuple.Create(msg, timeToSend));
                    Net.msgSendQueue = new Queue<Tuple<Message, uint>>(Net.msgSendQueue.OrderBy(elem => elem.Item2));
                }

                // Until we've sent a forward or return token, or the queue is empty
                bool shouldStopSending = false;
                var msgsToSend = new List<Message>();
                while (Net.msgSendQueue.Count > 0 && !shouldStopSending)
                {
                    // Update current time from top of message queue
                    Net.time = Net.msgSendQueue.Peek().Item2;
                    // Pop all messages to be sent at this timepoint
                    while (Net.msgSendQueue.Count > 0 && Net.msgSendQueue.Peek().Item2 == Net.time)
                    {
                        var msgTimePair = Net.msgSendQueue.Dequeue();
                        var msgToSend = msgTimePair.Item1;
                        msgToSend.time = msgTimePair.Item2;
                        msgsToSend.Add(msgToSend);

                        // Check if we should stop sending messages and wait for new messages to arrive
                        if (msgToSend.token == TokenType.Forward || msgToSend.token == TokenType.Return)
                        {
                            // flush queue if we're returning the token back to the initiator (hard coded to id 1)
                            if (msgToSend.to == "1")
                                shouldStopSending = false;
                            else
                            {
                                shouldStopSending = true;
                                // Need to handle case where a node has sent a token to a node that should have been marked as visited by the sender.
                                // But due to network delays, the node is still marked as unvisited by the sending node.
                                // Search for matching visited token in the queue going in reverse direction to the main token.
                                foreach (var queuedMsgTimePair in Net.msgSendQueue)
                                {
                                    var queuedMsg = queuedMsgTimePair.Item1;
                                    if (queuedMsg.token == TokenType.Vis && queuedMsg.from == msgToSend.to && queuedMsg.to == msgToSend.from)
                                    {
                                        shouldStopSending = false;
                                    }
                                }
                            }
                        }


                        // Print sent messages
                        Console.WriteLine(string.Format("... {0} {1} > {2} {3} {4} {5}", Net.time, Net.nodeId, msgToSend.from, msgToSend.to, (byte)msgToSend.token, msgToSend.payload));

                        // Terminate condition
                        if (msgToSend.to == "1" && msgToSend.token == TokenType.Return)
                        {
                            Console.WriteLine(string.Format("... {0} {1} < {2} {3} {4} {5}", msgToSend.time, "0", "1", "0", (byte)msgToSend.token, msgToSend.payload + 1));
                        }
                    }
                }
                // Send after popping from queue to avoid ping back forward tokens potentially concurrently modifying the queue
                foreach (var msgToSend in msgsToSend)
                {
                    var sendPort = Net.config[msgToSend.to];
                    var sendClient = new WebClient();
                    sendClient.DownloadStringAsync(new Uri("http://localhost:" + sendPort + "/" + msgToSend.Serialize()));
                }
                
                return "";
            });
        }
    }
    public class Net
    {
        public static Dictionary<string, string> config;
        public static string port;
        public static string nodeId;
        public static string adjId;
        public static string portAdj;
        public static uint time = 0;
        public static Queue<Tuple<Message, uint>> msgSendQueue = new Queue<Tuple<Message, uint>>();

        static Dictionary<string, string> ReadConfig(string configPath)
        {
            var lines = File.ReadAllLines(configPath);

            var dict = lines
                .Select(line =>
                {
                    var i = line.IndexOf("//");
                    return i < 0 ? line : line.Substring(0, i);
                })
                .Select(line => line.Trim())
                .Where(line => line != "")
                .Select(line => line.Split(' '))
                .Select(p => new { Key = p[0], Val = p[1] }) //.Dump()
                .ToDictionary(p => p.Key, p => p.Val);

            return dict;
        }

        public static void Main(string[] args)
        {
            var configPath = args[0];
            Net.nodeId = "0";
            Net.adjId = "1";
            Net.config = ReadConfig(configPath);
            Net.port = config[nodeId];
            Net.portAdj = config[adjId];

            using (var host = new NancyHost(new Uri("http://localhost:" + port)))
            {
                host.Start();

                var initMsg = "0 0 1 1 0";
                var initClient = new WebClient();
                initClient.DownloadStringAsync(new Uri("http://localhost:" + portAdj + "/" + initMsg));

                Console.WriteLine("Running on http://localhost:" + port);
                Console.ReadLine();
            }
        }
    }
}
