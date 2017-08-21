using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using System.Data;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace RxWeb
{
    class Program2
    {

        public static NetActor Factory()
        {
            var response = @"HTTP/1.1 200
Connection:close
thanks!";
            //create a netactor on port 8081
            NetActor a = new NetActor(8081);
            //write out to the console when you get sent data
            a.Incoming.Subscribe(i =>
            {
                Console.WriteLine("handling request...");
                a.Outgoing.OnNext(response);
                a.Dispose();
                a = null;
                Factory();
            });
            //create an observable from the console and bind it to the netactor
            var o = Observable.Start<string>(() => Console.ReadLine());
            //publish from the console to the actor
            o.Subscribe<string>(a.Outgoing.OnNext);
            return a;
        }

    }
    /// <summary>
    /// Binds to localhost. Pretty much 100% based on msdn code
    /// </summary>
    /// <seealso cref="http://msdn.microsoft.com/en-us/library/fx6588te.aspx"/>
    public class NetActor : IDisposable
    {
        //this socket is us
        Socket _Server;
        //this socket is them
        Socket _Client;
        //Incoming messages from the client
        private ISubject<string> _Incoming { get; set; }
        //allow the ability to subscribe to incoming messages
        public IObservable<string> Incoming
        {
            get
            {
                return _Incoming.AsObservable();
            }
        }

        //outgoing messages
        public ISubject<string> Outgoing { get; private set; }

        public NetActor(int port)
        {
            _Incoming = new Subject<string>();
            Outgoing = new Subject<string>();

            _Server = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            IPHostEntry ipHostInfo = Dns.GetHostEntry("localhost");
            IPAddress ipAddress = ipHostInfo.AddressList.FirstOrDefault(d=>d.AddressFamily == AddressFamily.InterNetwork);
            IPEndPoint localEndPoint = new IPEndPoint(ipAddress, port);
            // _Server.Bind(new IPEndPoint(IPAddress.Parse("127.0.0.1"), port));

            _Server.Bind(localEndPoint);
            _Server.Listen(100);
            _Server.BeginAccept((o) => BeginAccept(o), _Server);
        }
        private void BeginAccept(IAsyncResult ar)
        {
            var listener = (Socket)ar.AsyncState;
            //create a state that contains the ability to listen
            var state = new StateObject()
            {
                workSocket = _Server.EndAccept(ar)
            };

            //create the ability to push data out
            Outgoing.Subscribe((s) => Send(state.workSocket, s));

            //classic microsoft code, gotta love [object] state
            state.workSocket.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0, new AsyncCallback(BeginRead), state);

            //Observable.FromAsyncPattern(() => state.workSocket.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0, new AsyncCallback(BeginRead), state), state.workSocket.EndReceive);
            //    .

            //lets try to override this with Observable.FromAsyncPattern....
            //      worthless. this func returns just iasyncresult and not a tangible something... anything...
            //      like in the microsoft example docs. anger face
            //var o = Observable.FromAsyncPattern<byte[], int, int, SocketFlags>(
            //    (byt, offset, size, flags, cb, st) => state.workSocket.BeginReceive(byt, offset, size, flags, cb, st),
            //        (a) => state.workSocket.EndReceive(a));
        }

        private void BeginRead(IAsyncResult ar)
        {
            try
            {
                var state = (StateObject)ar.AsyncState;
                int bytes = state.workSocket.EndReceive(ar);

                if (bytes > 0)
                {
                    state.sb.Append(Encoding.ASCII.GetString(state.buffer, 0, bytes));
                    var content = state.sb.ToString();
                    _Incoming.OnNext(content);
                    if (content.IndexOf("<EOF>") != -1)
                    {
                        _Incoming.OnCompleted();
                    }
                }
                else
                {
                    state.workSocket.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0, new AsyncCallback(BeginRead), state);
                }
            }
            catch (Exception exc)
            {
                _Incoming.OnError(exc);
            }
        }

        private void Send(Socket handler, String data)
        {
            if (handler == null) return;
            // Convert the string data to byte data using ASCII encoding.
            byte[] byteData = Encoding.ASCII.GetBytes(data);

            // Begin sending the data to the remote device.
            handler.BeginSend(byteData, 0, byteData.Length, 0,
                new AsyncCallback(SendCallback), handler);
        }

        private void SendCallback(IAsyncResult ar)
        {
            try
            {
                // Retrieve the socket from the state object.
                Socket handler = (Socket)ar.AsyncState;

                // Complete sending the data to the remote device.
                int bytesSent = handler.EndSend(ar);

                handler.Shutdown(SocketShutdown.Both);
                handler.Close();

            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }


        // State object for reading client data asynchronously
        private class StateObject
        {
            // Client  socket.
            public Socket workSocket = null;
            // Size of receive buffer.
            public const int BufferSize = 1024;
            // Receive buffer.
            public byte[] buffer = new byte[BufferSize];
            // Received data string.
            public StringBuilder sb = new StringBuilder();
        }
        public void End()
        {
            if (_Client != null)
            {
                _Client.Shutdown(SocketShutdown.Both);
                _Client.Close();
            }
            if (_Server != null)
            {

                _Server.Close();
            }
        }

        public void Dispose()
        {
            End();
        }
    }
}