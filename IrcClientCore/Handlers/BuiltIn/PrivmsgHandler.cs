﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IrcClientCore.Handlers.BuiltIn
{
    class PrivmsgHandler : BaseHandler
    {
        public MessageType Type = MessageType.Normal;

        public override async Task<bool> HandleLine(IrcMessage parsedLine)
        {
            // handle messages to this irc client
            var destination = parsedLine.CommandMessage.Parameters[0];
            var content = parsedLine.TrailMessage.TrailingContent;

            if (content == "")
            {
                content = parsedLine.CommandMessage.Parameters.Last();
            }

            if (destination == Irc.Server.Username)
            {
                destination = parsedLine.PrefixMessage.Nickname;
            }

            var msg = new Message();
            msg.Channel = destination;
            msg.Type = Type;
            msg.User = parsedLine.PrefixMessage.Nickname ?? parsedLine.PrefixMessage.Prefix;

            if (destination.StartsWith("@#"))
            {
                msg.OpOnlyMessage = true;
                msg.Channel = destination = destination.Substring(1);
            }

            if (msg.User == null)
            {
                msg.User = "";
            }

            if (parsedLine.Metadata.ContainsKey(IrcMessage.Time))
            {
                var time = DateTime.Parse(parsedLine.Metadata[IrcMessage.Time]);
                msg.Date = time;
            }

            if (parsedLine.Metadata.ContainsKey(IrcMessage.Id))
            {
                msg.MessageId = parsedLine.Metadata[IrcMessage.Id];
            }

            if (parsedLine.Metadata.ContainsKey(IrcMessage.Reply))
            {
                msg.ReplyTo = parsedLine.Metadata[IrcMessage.Reply];
            }

            if (content.Contains("ACTION"))
            {
                msg.Text = content.Replace("ACTION ", "");
                msg.Type = MessageType.Action;
            }
            else
            {
                msg.Text = content;
            }

            if ((parsedLine.TrailMessage.TrailingContent.Contains(Irc.Server.Username) || parsedLine.CommandMessage.Parameters[0] == Irc.Server.Username))
            {
                msg.Mention = true;
                Irc.AddMention(msg);
            }

            if (destination == "*")
            {
                Irc.ChannelList.ServerLog?.Buffers.Add(msg);
            }
            else
            {
                if (!Irc.ChannelList.Contains(destination))
                {
                    await Irc.AddChannel(destination);
                }

                Irc.AddMessage(destination, msg);

                if (Type == MessageType.Notice)
                {
                    Irc.InfoBuffer.Add(msg);
                }
            }
            return true;
        }
    }
}
