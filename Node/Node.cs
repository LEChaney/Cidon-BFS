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

namespace Cidon
{
    public class NodeModule : NancyModule
    {
        enum {

        }

        public NodeModule()
        {
            var config = NodeHost.config;
            //var adjNodes = new ArraySegment<string>(args, 2, args.Length - 2);

            Get("/{msg}", async x =>
            {
                string msg = x.msg;
                Console.WriteLine(msg);
                return "";
            });
        }
    }
    public class NodeHost
    {
        public static Dictionary<string, string> config;
        public static string port;
        public static string nodeId;
        public static string[] adjIds;

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
            NodeHost.nodeId = args[1];
            NodeHost.adjIds = new ArraySegment<string>(args, 2, args.Length - 2).ToArray();
            NodeHost.config = ReadConfig(configPath);
            NodeHost.port = config[nodeId];

            using (var host = new NancyHost(new Uri("http://localhost:" + port)))
            {
                host.Start();
                Console.WriteLine("Running on http://localhost:" + port);
                Console.ReadLine();
            }
        }
    }
}
