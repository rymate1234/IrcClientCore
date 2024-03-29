using IrcClientCore.Commands;
using IrcClientCore.Handlers;
using IrcClientCore.Handlers.BuiltIn;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Linq;

namespace IrcClientCore
{
    public class Irc : BaseObject
    {
        public string MOTD { get; internal set; }
        public IrcServer Server { get; set; }

        public bool IsAuthed { get; set; }

        public ChannelsGroup ChannelList { get; set; }

        public CommandManager CommandManager { get; private set; }
        protected HandlerManager HandlerManager { get; private set; }
        public ISocket Connection { get; private set; }
        public IBuffer InfoBuffer { get; private set; }

        public string Buffer;
        public bool Transferred = false;

        public bool IsConnecting
        {
            get => _isConnecting;
            set
            {
                _isConnecting = value;
                NotifyPropertyChanged();
            }
        }

        public bool IsReconnecting => IsConnecting;

        public bool IsConnected
        {
            get => _isConnected;
            set
            {
                _isConnected = value;
                NotifyPropertyChanged();
            }
        }

        private bool _isConnected = false;
        public int ReconnectionAttempts;
        private bool _isConnecting;

        public bool ReadOrWriteFailed { get; set; }

        public Action<Irc> HandleDisconnect { get; set; }
        public ObservableCollection<Message> Mentions { get; set; }

        public Action<List<ChannelListItem>> HandleDisplayChannelList { get; set; }

        public string Nickname
        {
            get => Server.Username;
            set
            {
                Server.Username = value;
                WriteLine("NICK " + value);

                foreach (var channel in ChannelList)
                {
                    channel.ClientMessage("Changed username to " + value);
                }
            }
        }

        public bool Bouncer { get; internal set; }
        public bool SupportsMessageTags { get; internal set; }
        internal string WhoisDestination { get; set; }
        public bool DebugMode { get; protected set; }

        public Irc(IrcServer server)
        {
            this.Server = server;
            IsAuthed = false;
            DebugMode = true;
        }

        public async void Initialise()
        {
            ChannelList = new ChannelsGroup(new ObservableCollection<Channel>(), this) { Server = Server.Name };
            Mentions = new ObservableCollection<Message>();
            this.CommandManager = new CommandManager(this);
            this.HandlerManager = new HandlerManager(this);
            this.Connection = this.CreateConnection();
            this.InfoBuffer = this.CreateChannelBuffer("info");

            await AddChannel("Server");
        }

        public virtual ISocket CreateConnection()
        {
            return new IrcSocket(this);
        }

        public async void Connect() { await Connection.Connect(); }
        public async void DisconnectAsync(string msg = "Powered by WinIRC", bool attemptReconnect = false) { await Connection.Disconnect(msg, attemptReconnect); }

        public async void AttemptAuth()
        {
            // Auth to the server
            Console.WriteLine("Attempting to auth");

            if (Server.Password != "")
            {
                await WriteLine("PASS " + Server.Password);
            }

            await WriteLine("CAP LS");

            AttemptRegister();

            IsAuthed = true;
        }

        protected async void AttemptRegister()
        {
            try
            {
                await WriteLine($"NICK {Server.Username}");
                await WriteLine($"USER {Server.Username} 8 * :{Server.Username}");
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                Console.WriteLine(e.StackTrace);
            }
        }

        public async Task RecieveLine(string receivedData)
        {
            if (DebugMode) Debug.WriteLine(receivedData);
            if (receivedData == null) return;
            if (receivedData.Contains("Nickname is already in use") && !Bouncer)
            {
                this.Server.Username += "_";
                AttemptAuth();
                return;
            }

            if (receivedData.StartsWith("ERROR"))
            {
                if (!IsConnecting)
                {
                    ReadOrWriteFailed = true;

                    var msg = Server.ShouldReconnect
                        ? "Attempting to reconnect..."
                        : "Please try again later.";

                    AddError("Error with connection: \n" + msg);

                    DisconnectAsync(attemptReconnect: Server.ShouldReconnect);
                }
                return;
            }

            ReconnectionAttempts = 0;

            var parsedLine = new IrcMessage(receivedData);
            await ProcessLine(parsedLine);
        }


        public virtual async Task ProcessLine(IrcMessage parsedLine)
        {
            var handlers = HandlerManager.GetHandlers(parsedLine.CommandMessage.Command);
            foreach (var handler in handlers)
            {
                var cont = await handler.HandleLine(parsedLine);
                if (!cont) break;
            }
        }

        public void SendAction(string channel, string message)
        {
            var msg = new Message();

            msg.Text = message;
            msg.Type = MessageType.Action;
            msg.User = Server.Username;

            if (!SupportsMessageTags)
                AddMessage(channel, msg);

            WriteLine(string.Format("PRIVMSG {0} :\u0001ACTION {1}\u0001", channel, message));
        }

        public void SendMessage(string channel, string message)
        {
            var msg = new Message();

            msg.Text = message;
            msg.User = Server.Username;
            msg.Type = MessageType.Normal;

            // if the server doesn't support self message add it to the buffer
            if (!SupportsMessageTags)
                AddMessage(channel, msg);

            WriteLine(string.Format("PRIVMSG {0} :{1}", channel, message));
        }

        public void SendReply(string channel, string message, string reply)
        {
            var msg = new Message();

            msg.Text = message;
            msg.User = Server.Username;
            msg.Type = MessageType.Normal;

            // if the server doesn't support self message add it to the buffer
            if (!SupportsMessageTags)
                AddMessage(channel, msg);

            WriteLine(string.Format("+draft/reply={0} PRIVMSG {1} :{2}", reply, channel, message));
        }

        public string GetChannelTopic(string channel)
        {
            if (ChannelList.Contains(channel))
                return ChannelList[channel].Store.Topic;

            return "";
        }

        public async Task JoinChannel(string channel)
        {
            await AddChannel(channel);
            await WriteLine(string.Format("JOIN {0}", channel));
        }

        public async void PartChannel(string channel)
        {
            await WriteLine (string.Format("PART {0}", channel));
            RemoveChannel(channel);
        }

        public void AddError(string message)
        {
            var msg = new Message();

            msg.Text = message;
            msg.User = "Error";
            msg.Type = MessageType.Info;
            msg.Mention = true;

            AddMessage("Server", msg);
        }

        public async void AddMessage(string channel, Message msg)
        {
            if (Server == null)
            {
                return;
            }

            if (!ChannelList.Contains(channel))
            {
                await AddChannel(channel);
            }

            var chan = ChannelList[channel];

            if (!chan.CurrentlyViewing)
            {
                chan.HasUnread = true;
                chan.UnreadCount++;

                if (msg.Mention)
                {
                    chan.HasMentions = true;
                }
            }

            if (msg.ReplyTo != null)
            {
                var item = chan.Buffers.GetMessage(msg.ReplyTo);
                if (item != null)
                {
                    item.Replies += 1;
                    chan.Buffers.UpdateMessage(item);
                }
            }

            msg.Channel = channel;

            chan.Buffers.Add(msg);
        }

        public async Task<bool> AddChannel(string channel)
        {
            if (string.IsNullOrEmpty(channel))
            {
                return false;
            }

            if (!ChannelList.Contains(channel))
            {
                var comparer = Comparer<string>.Default;

                var i = 0;

                while (i < ChannelList.Count && comparer.Compare(ChannelList[i].NameLower, channel.ToLower()) < 0)
                    i++;

                ChannelList.Insert(i, channel);
            }

            await Task.Delay(1);

            return ChannelList.Contains(channel);
        }

        public virtual IBuffer CreateChannelBuffer(string channel)
        {
            return new Buffer();
        }

        public void RemoveChannel(string channel)
        {
            if (ChannelList.Contains(channel))
            {
                ChannelList.Remove(channel);
            }
        }

        public void ClientMessage(string channel, string text)
        {
            var msg = new Message();
            msg.User = "";
            msg.Type = MessageType.Info;
            msg.Text = text;

            this.AddMessage(channel, msg);
        }

        public async Task WriteLine(string str)
        {
            await Connection.WriteLine(str);
        }

        public ObservableCollection<User> GetChannelUsers(string channel)
        {
            if (!ChannelList.Contains(channel))
            {
                return new ObservableCollection<User>();
            }

            var store = ChannelList[channel].Store;

            return store.Users;
        }

        public ObservableCollection<string> GetRawUsers(string channel)
        {
            if (!ChannelList.Contains(channel))
            {
                return new ObservableCollection<string>();
            }

            return ChannelList[channel].Store.RawUsers;
        }

        public void AddMention(Message message)
        {
            Mentions.Add(message);
        }
    }
}
