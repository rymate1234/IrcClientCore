﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace IrcClientCore.Handlers.BuiltIn
{
    class ServerJoinedHandler : BaseHandler
    {
        public override Task<bool> HandleLine(IrcMessage parsedLine)
        {
            if (Irc.Server.NickservPassword != null && Irc.Server.NickservPassword != "")
            {
                Irc.SendMessage("nickserv", "identify " + Irc.Server.NickservPassword);
            }

            if (Irc.Server.Channels != null && Irc.Server.Channels != "")
            {
                var channelsList = Irc.Server.Channels.Split(',');
                foreach (string channel in channelsList)
                {
                    Irc.JoinChannel(channel);
                }
            }

            return Task.FromResult(true);
        }
    }
}