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

namespace Cidon
{
    public class NetModule : NancyModule
    {
        public NetModule()
        {
            Get("/{msg}", x =>
            {
                var msg = x.msg;
                Console.WriteLine(msg);
                return "";
            });
        }
    }
    public class NetHost
    {
        public static Dictionary<string, string> config;
        public static string port;
        public static string nodeId;
        public static string adjId;
        public static string portAdj;

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
            NetHost.nodeId = "0";
            NetHost.adjId = "1";
            NetHost.config = ReadConfig(configPath);
            NetHost.port = config[nodeId];
            NetHost.portAdj = config[adjId];

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
