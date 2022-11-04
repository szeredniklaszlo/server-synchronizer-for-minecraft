using System;
using System.Linq;
using System.Text;

namespace MinecraftSynchronizer
{
    internal class Logger
    {

        internal void Error(Exception exception)
        {
            lock (Console.Out)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[ERROR] " + exception.Message);
                Console.ForegroundColor = ConsoleColor.White;
            }
        }

        internal void Error(string message)
        {
            Error(message, Array.Empty<object>());
        }

        internal void Error(string message, object innerContents)
        {
            Error(message, new[] { innerContents });
        }

        internal void Error(string message, object[] innerContents)
        {
            lock (Console.Out)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                LogPad("[ERROR]", message, innerContents);
                Console.ForegroundColor = ConsoleColor.White;
            }
        }

        internal void Local(string message)
        {
            Local(message, Array.Empty<object>());
        }

        internal void Local(string message, object innerContent)
        {
            Local(message, new[] { innerContent });
        }

        internal void Local(string message, object[] innerContents)
        {
            lock (Console.Out)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                LogPad("[LOCAL]", message, innerContents);
                Console.ForegroundColor = ConsoleColor.White;
            }
        }

        internal void Drive(string message, object innerContent)
        {
            lock (Console.Out)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                LogPad("[DRIVE]", message, new[] { innerContent });
                Console.ForegroundColor = ConsoleColor.White;
            }
        }

        internal void DriveWarn(string message, object innerContent)
        {
            lock (Console.Out)
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                LogPad("[DRIVE]", message, new[] { innerContent });
                Console.ForegroundColor = ConsoleColor.White;
            }
        }

        internal void Server(ServerStatus status)
        {
            lock (Console.Out)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                LogPad("[SERVER]", "Server status: {}", new[] { status.ToString() });
                Console.ForegroundColor = ConsoleColor.White;
            }
        }

        internal void Info(string message)
        {
            Info(message, Array.Empty<object>());
        }

        internal void Info(string message, object innerContent)
        {
            Info(message, new[] { innerContent });
        }

        internal void Info(string message, object[] innerContents)
        {
            lock (Console.Out)
            {
                Console.ForegroundColor = ConsoleColor.Gray;
                LogPad("[INFO]", message, innerContents);
                Console.ForegroundColor = ConsoleColor.White;
            }
        }

        private void LogPad(string prefix, string message, object[] innerContents)
        {
            message = $"{prefix.PadRight(8)} {message}";
            string[] messageParts = message.Split(new[] { "{}" }, StringSplitOptions.None);
            if (messageParts.Length != innerContents.Length + 1)
            {
                Error("Log error: {}", new[] { message });
                return;
            }

            StringBuilder result = new StringBuilder();

            for (int i = 0; i < innerContents.Length; i++)
            {
                result.Append(messageParts[i].PadRight(30))
                    .Append(innerContents[i]);
            }
            result.Append(messageParts.Last());

            Console.WriteLine(result.ToString());
        }
    }
}
