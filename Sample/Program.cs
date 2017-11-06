using System;
using System.Threading;
using ObsidianX.Web.EventSource;

namespace Sample
{
    internal class Program
    {
        public static void Main (string[] args)
        {
            var tokenSource = new CancellationTokenSource ();

            var eventServer = new TestServer (tokenSource.Token);
            eventServer.Start ();

            var eventSource = new EventSource ("http://localhost:12345/", tokenSource.Token);
            eventSource.OnOpen += OnOpen;
            eventSource.OnStateChanged += OnStateChanged;
            eventSource.OnError += OnError;
            eventSource.OnMessage += OnMessage;
            eventSource.Start ();

            if (Console.ReadKey ().Key == ConsoleKey.Q) {
                tokenSource.Cancel ();
            }
        }

        public static void OnOpen ()
        {
            Console.WriteLine ("Connection opened");
        }

        public static void OnStateChanged (ReadyState state)
        {
            Console.WriteLine ($"State changed: {state}");
        }

        public static void OnError (string reason, string data)
        {
            Console.WriteLine ($"Error: {reason}");
            Console.WriteLine (data);
        }

        public static void OnMessage (Event evt)
        {
            Console.WriteLine (evt);
        }
    }
}