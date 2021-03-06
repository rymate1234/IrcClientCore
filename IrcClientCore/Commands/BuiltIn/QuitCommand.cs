﻿using System;

namespace IrcClientCore.Commands
{
    internal class QuitCommand : BaseCommand
    {
        public override void RunCommand(string channel, string[] args)
        {
            var message = string.Join(" ", args, 1, args.Length - 1);

            if (message != "")
            {
                Irc.DisconnectAsync(message);
            }
            else
            {
                Irc.DisconnectAsync();
            }
        }
    }
}