using System.IO;
using System.Net;
using System.Threading;

namespace Sample
{
    public class TestServer
    {
        private readonly HttpListener _listener;
        private CancellationToken _cancelToken;
        private long _id;

        public TestServer (CancellationToken token)
        {
            _listener = new HttpListener ();
            _listener.Prefixes.Add ("http://localhost:12345/");

            _cancelToken = token;
        }

        public async void Start ()
        {
            _listener.Start ();
            HttpListenerContext context = await _listener.GetContextAsync ();

            using (var writer = new StreamWriter (context.Response.OutputStream)) {
                while (!_cancelToken.IsCancellationRequested && context.Response.OutputStream.CanWrite) {
                    string evt = GenerateEvent ();

                    writer.Write (evt);
                    writer.Flush ();

                    Thread.Sleep (2000);
                }
            }
        }

        private string GenerateEvent ()
        {
            return $"id: {_id++}\n" +
                   "event: customEvent\n" +
                   "data: test\n" +
                   "data: one\n" +
                   "data: two\n\n";
        }
    }
}