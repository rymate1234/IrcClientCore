﻿using System;

namespace IrcClientCore.Commands
{
    internal class KickCommand : BaseCommand
    {
        public override void RunCommand(string[] args)
        {
            if (args.Length < 2)
            {
                return;
            }

            var nick = args[1];
            var kick = "KICK " + Irc.CurrentChannel + " " + nick;

            if (args.Length > 3)
            {
                kick += " :" + String.Join(" ", args, 2, args.Length - 2);
            }

            Irc.WriteLine(kick);
        }
    }
}