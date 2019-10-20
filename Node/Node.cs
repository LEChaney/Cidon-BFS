using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Nancy.Hosting.Self;
using Nancy;
using Nancy.ModelBinding;
using System.Threading;
using System.Net;

namespace Cidon
{
    public enum MarkerType
    {
        Unvisited,
        Visited,
        Son,
        Father
    }

    public enum TokenType : byte
    {
        Forward = 1,
        Vis = 2,
        Return = 3,
    }

    public class Link
    {
        public Link(string nodeId)
        {
            this.nodeId = nodeId;
            this.marker = MarkerType.Unvisited;
        }

        public MarkerType marker;
        public readonly string nodeId;
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
        public readonly uint time;
        public readonly string from;
        public readonly string to;
        public readonly TokenType token;
        public uint payload;
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

        private readonly List<Message> msgList = new List<Message>();
    }

    public class NodeRecvModule : NancyModule
    {
        public NodeRecvModule()
        {
            var config = Node.config;
            //var adjNodes = new ArraySegment<string>(args, 2, args.Length - 2);

            Get("/{msg}", x =>
            {
                lock (Node.nodeLock)
                {
                    Message msg = new Message(x.msg);
                    List<Message> msgsToSend = new List<Message>();

                    // Print received message
                    Console.WriteLine(string.Format("... {0} {1} < {2} {3} {4} {5}", msg.time, Node.nodeId, msg.from, msg.to, (byte)msg.token, msg.payload));

                    // Terminate condition
                    if (Node.nodeId == "1" && msg.token == TokenType.Return)
                    {
                        Console.WriteLine(string.Format("... {0} {1} > {2} {3} {4} {5}", msg.time, "1", "1", "0", (byte)msg.token, msg.payload + 1));
                    }

                    // Mark incoming link as father if this is the first time we've received a token
                    if (msg.token != TokenType.Vis && !Node.hasHadToken && msg.from != Node.netNodeId)
                    {
                        Node.links[msg.from].marker = MarkerType.Father;
                    }

                    // Handle receiving forward token if we've already had one
                    if (msg.token == TokenType.Forward && Node.hasHadToken)
                    {
                        Node.links[msg.from].marker = MarkerType.Visited;
                    }

                    // Handle receiving visited token
                    bool skipSendVisit = false;
                    if (msg.token == TokenType.Vis && msg.from != Node.netNodeId)
                    {
                        Node.links[msg.from].marker = MarkerType.Visited;
                        // Regerate last token / message conditions if we sent a forward token to a visited node
                        if (msg.from == Node.lastNodeSentTokenTo)
                        {
                            msg = Node.savedMessage;
                            Node.hasHadToken = false;
                            skipSendVisit = true;
                        }
                    }

                    // Handle receiving forward token
                    if ((msg.token == TokenType.Forward && !Node.hasHadToken) || msg.token == TokenType.Return)
                    {
                        bool foundUnvisited = false;
                        var nodeIds = new List<string>(Node.links.Keys);
                        var fatherNodeId = "";
                        foreach (string nodeId in nodeIds)
                        {
                            // Find father node
                            if (fatherNodeId == "" && Node.links[nodeId].marker == MarkerType.Father)
                                fatherNodeId = nodeId;

                            if (Node.links[nodeId].marker == MarkerType.Unvisited)
                            {
                                // Find first unvisited node
                                if (!foundUnvisited)
                                {
                                    // If we found one, then send a forward token and mark the link as a son
                                    foundUnvisited = true;
                                    msgsToSend.Add(new Message(msg.time, Node.nodeId, nodeId, TokenType.Forward, msg.payload));
                                    Node.links[nodeId].marker = MarkerType.Son;

                                    // Save the forward token / messge in case we need to regenerate it later
                                    Node.savedMessage = msg;
                                    Node.lastNodeSentTokenTo = nodeId;
                                }
                                // Send vis token on all other unvisited nodes.
                                // Skip this if token is a return token
                                else if (msg.token != TokenType.Return && !skipSendVisit)
                                {
                                    msgsToSend.Add(new Message(msg.time, Node.nodeId, nodeId, TokenType.Vis, 0));
                                }
                            }

                            // Send visited token over all visited links to inform neighbors that they should mark this node visited
                            if (Node.links[nodeId].marker == MarkerType.Visited && msg.token != TokenType.Return && !skipSendVisit)
                            {
                                msgsToSend.Add(new Message(msg.time, Node.nodeId, nodeId, TokenType.Vis, 0));
                            }
                        }

                        // If we have no unvisited nodes, then send a return token on the father link
                        if (!foundUnvisited && fatherNodeId != "")
                        {
                            msgsToSend.Add(new Message(msg.time, Node.nodeId, fatherNodeId, TokenType.Return, msg.payload + 1));
                        }

                        // Print sent messages
                        foreach (Message msgToSend in msgsToSend)
                        {
                            Console.WriteLine(string.Format("... {0} {1} > {2} {3} {4} {5}", msg.time, Node.nodeId, msgToSend.from, msgToSend.to, (byte)msgToSend.token, msgToSend.payload));
                        }

                        // Record that we have seen the token already in case it gets returned to us
                        Node.hasHadToken = true;

                        // Send messages as bundle to net node
                        if (msgsToSend.Count > 0)
                        {
                            var msgBundle = new MsgBundle(msgsToSend);
                            var bundleClient = new WebClient();
                            bundleClient.DownloadStringAsync(new Uri("http://localhost:" + Node.netNodePort + "/" + msgBundle.Serialize()));
                        }
                    }
                }

                return "";
            });
        }
    }
    public static class Node
    {
        public static Dictionary<string, string> config;
        public static string port;
        public static string nodeId;
        public static string netNodeId = "0";
        public static string netNodePort = "";
        public static Dictionary<string, Link> links = new Dictionary<string, Link>();
        public static object nodeLock = new object();
        
        public static bool hasHadToken = false;
        public static Message savedMessage = null;
        public static string lastNodeSentTokenTo = "";

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

        static Dictionary<string, Link> ConstructLinks(List<string> adjIds)
        {
            var links = new Dictionary<string, Link>();
            foreach (var id in adjIds)
            {
                links.Add(id, new Link(id));
            }

            return links;
        }

        public static void Main(string[] args)
        {
            var configPath = args[0];
            Node.nodeId = args[1];
            Node.config = ReadConfig(configPath);
            Node.port = config[nodeId];
            Node.netNodePort = config[netNodeId];
            var adjIds = new ArraySegment<string>(args, 2, args.Length - 2).ToList();
            Node.links = ConstructLinks(adjIds);

            using (var host = new NancyHost(new Uri("http://localhost:" + port)))
            {
                host.Start();
                Console.WriteLine("Running on http://localhost:" + port);
                Console.ReadLine();
            }
        }
    }
}
