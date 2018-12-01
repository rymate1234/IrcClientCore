using System;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace IrcClientCore
{
    public class IrcSocket : Irc
    {
        private TcpClient _conn;
        private Stream _stream;
        private StreamReader _clientStreamReader;
        private StreamWriter _clientStreamWriter;

        public IrcSocket(IrcServer server) : base(server)
        {
        }

        public override async void Connect()
        {
            if (Server == null)
                return;

            IsAuthed = false;
            ReadOrWriteFailed = false;
            _conn = new TcpClient();
            _conn.NoDelay = true;
            try
            {
                await _conn.ConnectAsync(Server.Hostname, Server.Port);

                if (_conn.Connected)
                {
                    if (Server.Ssl)
                    {
                        var sslStream = new SslStream(_conn.GetStream(), false, new RemoteCertificateValidationCallback(CheckCert));
                        sslStream.AuthenticateAsClient(Server.Hostname);
                        _stream = sslStream;
                    }
                    else
                    {
                        _stream = _conn.GetStream();
                    }

                    _clientStreamReader = new StreamReader(_stream);
                    _clientStreamWriter = new StreamWriter(_stream);

                    AttemptAuth();

                    while (true)
                    {
                        var line = await _clientStreamReader.ReadLineAsync();
                        await HandleLine(line);
                    }
                }
            } 
            catch (Exception e) 
            { 
                Console.WriteLine(e.Message);
                Console.WriteLine(e.StackTrace);
            }
        }

        private bool CheckCert(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            if (Server.IgnoreCertErrors)
            {
                return true;
            }

            return sslPolicyErrors == SslPolicyErrors.None;
        }

        public override void DisconnectAsync(string msg = "Powered by WinIRC", bool attemptReconnect = false)
        {
            WriteLine("QUIT :" + msg);

            if (Server.ShouldReconnect && attemptReconnect)
            {
                IsReconnecting = true;
                ReconnectionAttempts++;

                Task.Run(async () => {
                    if (ReconnectionAttempts < 3)
                        await Task.Delay(1000);
                    else
                        await Task.Delay(60000);

                    if (IsReconnecting)
                        Connect();
                }).Start();
            }
            else
            {
                IsConnected = false;
                HandleDisconnect?.Invoke(this);
            }
        }

        public override void WriteLine(string str)
        {
            try {
                _clientStreamWriter.WriteLine(str);
                _clientStreamWriter.Flush();
            } catch (Exception e) {
                Console.WriteLine("Failed to send: " + str);
                Console.WriteLine(e);
            }
        }
    }
}