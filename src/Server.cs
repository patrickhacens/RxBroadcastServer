using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading.Tasks;

namespace ReactiveServer
{

    interface IServer : IDisposable
    {
        bool IsRunning { get; }

        void Start();

        void Stop();
    }

    class Server : IServer
    {
        public bool IsRunning => _isRunning;
        private bool _isRunning;

        private IPAddress listenTo;
        private int port;

        private Socket listener;
        private List<Socket> clients = new List<Socket>();

        public Server(IPAddress listenTo, int port)
        {
            this.listenTo = listenTo;
            this.port = port;
        }

        public void Start()
        {
            if (!IsRunning)
            {
                _isRunning = true;
                listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                listener.Bind(new IPEndPoint(listenTo, port));
                listener.Listen(60);
                listener.BeginAccept(AcceptCallback, null);
            }
        }

        public void Stop()
        {
            if (IsRunning)
            {
                listener.Close();
                clients.ForEach(d => { d.Close();  d.Dispose(); });
                clients.Clear();
                _isRunning = false;
            }
        }

        private void AcceptCallback(IAsyncResult ar)
        {
            Socket client = listener.EndAccept(ar);
            Console.WriteLine($"New client connected {client.RemoteEndPoint.ToString()}");
            clients.Add(client);
            byte[] buffer = new byte[1 << 10];
            client.BeginReceive(buffer, 0, buffer.Length, SocketFlags.None, ReceiveCallBack, new State(client, buffer));
            listener.BeginAccept(AcceptCallback, null);
        }

        private void ReceiveCallBack(IAsyncResult ar)
        {
            if (ar.AsyncState is State state)
            {
                int readData = state.Socket.EndReceive(ar);
                Console.WriteLine($"Data received from {state.Socket.RemoteEndPoint}");
                this.Broadcasts(state.Buffer.Take(readData).ToArray());
                state.Socket.BeginReceive(state.Buffer, 0, state.Buffer.Length, SocketFlags.None, ReceiveCallBack, state);
            }
        }

        public void Broadcasts(byte[] buffer)
        {
            Parallel.ForEach(clients, (client) => client.Send(buffer));
        }

        public void Dispose()
        {
            listener.Close();
            listener.Dispose();
            clients.ForEach(d => { d.Close(); d.Dispose(); });
            clients.Clear();
        }

        private class State
        {
            public Socket Socket { get; set; }
            public byte[] Buffer { get; set; }

            public State(Socket socket, byte[] buffer)
            {
                this.Socket = socket;
                this.Buffer = buffer;
            }
        }
    }

    class RxServer : IServer
    {
        public bool IsRunning => _isRunning;
        private bool _isRunning;

        private IPAddress listenTo;
        private int port;

        private Socket listener;
        Subject<byte[]> broadcastData = new Subject<byte[]>();
        Subject<object> closeConnection = new Subject<object>();

        IDisposable listening;
        List<IDisposable> broadcastings = new List<IDisposable>();

        public RxServer(IPAddress listenTo, int port)
        {
            this.listenTo = listenTo;
            this.port = port;
        }

        public void Start()
        {
            if (!IsRunning)
            {
                _isRunning = true;
                listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                listener.Bind(new IPEndPoint(listenTo, port));
                listener.Listen(60);
                listening = Observable.Defer(() => Observable.FromAsyncPattern(listener.BeginAccept, listener.EndAccept)())
                        .Repeat()
                        .Subscribe(client =>
                        {
                            var broadcastSubscription = broadcastData.Subscribe(data => client.Send(data));
                            closeConnection.Subscribe(_ => { broadcastSubscription.Dispose(); client.Close(); }, OnError);
                            Console.WriteLine($"New client connected {client.RemoteEndPoint.ToString()}");
                            byte[] buffer = new byte[1 << 10];
                            broadcastings.Add(Observable.Defer(() => Observable.FromAsyncPattern<byte[], int, int, SocketFlags, int>(client.BeginReceive, client.EndReceive)(buffer, 0, buffer.Length, SocketFlags.None))
                                    .Repeat()
                                    .Select(read => buffer.Take(read).ToArray())
                                    .Subscribe((data) => broadcastData.OnNext(data), OnError));
                        }, OnError);
            }
        }

        public virtual void OnError(Exception err)
        {
            var color = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(err.GetBaseException().Message);
            Console.ForegroundColor = color;
        }

        public void Stop()
        {
            if (IsRunning)
            {
                broadcastings.ForEach(d => d.Dispose());
                broadcastings.Clear();
                listening.Dispose();
                listener.Dispose();
                broadcastData.Dispose();
                closeConnection.OnNext(null);
                closeConnection.Dispose();
                _isRunning = false;
            }
        }

        public void Dispose()
        {
            broadcastings.ForEach(d => d.Dispose());
            broadcastings.Clear();
            listening.Dispose();
            listener.Dispose();
            broadcastData.Dispose();
            closeConnection.OnNext(null);
            closeConnection.Dispose();
            _isRunning = false;
        }

        //public void Start()
        //{
        //    if (!IsRunning)
        //    {
        //        _isRunning = true;
        //        listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        //        listener.Bind(new IPEndPoint(listenTo, port));
        //        listener.Listen(60);

        //        Subject<byte[]> broadcastData = new Subject<byte[]>();

        //        var connectedSockets = Observable.Create<Socket>(observer =>
        //        {
        //            var whenConnect = Observable.FromAsyncPattern(listener.BeginAccept, listener.EndAccept);
        //            return whenConnect().Subscribe(observer);
        //        }).Repeat();

        //        connectedSockets.Subscribe(client =>
        //        {
        //            Console.WriteLine($"New client connected {client.RemoteEndPoint.ToString()}");
        //            broadcastData.Subscribe((data) => client.Send(data));
        //            var receivedData = Observable.Create<byte[]>(observer =>
        //            {
        //                var buffer = new byte[1 << 10];
        //                var whenReceiveData = Observable.FromAsyncPattern<byte[], int, int, SocketFlags, int>(client.BeginReceive, client.EndReceive);
        //                return whenReceiveData(buffer, 0, buffer.Length, SocketFlags.None).Select(d => buffer.Take(d).ToArray()).Subscribe(observer);
        //            }).Repeat();
        //            receivedData.Subscribe(broadcastData);
        //            //receivedData.Subscribe(data => Console.Write(Encoding.Default.GetString(data)));
        //        });
        //    }
        //}
    }
}
