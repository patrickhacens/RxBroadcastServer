using RxWeb;
using System;
using System.Net;
using System.Net.Sockets;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ReactiveServer
{
    class Program
    {
        static void Main(string[] args)
        {
            IServer server = new RxServer(IPAddress.Any, 666);
            server.Start();

            Thread.Sleep(10000);
            

            server.Stop();
            Console.WriteLine("Server is stoped");
            //IServer server2 = new Server(IPAddress.Any, 667);
            //server2.Start();

            Thread.Sleep(Timeout.Infinite);
        }


        static byte[] GetResponse(string request)
        {
            Byte[] response = Encoding.UTF8.GetBytes("HTTP/1.1 101 Switching Protocols" + Environment.NewLine
                               + "Connection: Upgrade" + Environment.NewLine
                               + "Upgrade: websocket" + Environment.NewLine
                               + "Sec-WebSocket-Accept: " + Convert.ToBase64String(SHA1.Create().ComputeHash(Encoding.UTF8.GetBytes(new Regex("Sec-WebSocket-Key: (.*)").Match(request).Groups[1].Value.Trim() + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11"))) + Environment.NewLine+ Environment.NewLine);
            return response;
        }
    }
}
