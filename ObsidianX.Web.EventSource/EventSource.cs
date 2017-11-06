using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;

namespace ObsidianX.Web.EventSource
{
    /// <summary>
    /// Emulates the EventSource class found in some modern browsers for receiving events from a server via long-polling.
    /// See: https://developer.mozilla.org/en-US/docs/Web/API/EventSource
    /// </summary>
    public class EventSource
    {
        public delegate void OpenDelegate ();

        public delegate void StateChangedDelegate (ReadyState state);

        public delegate void ErrorDelegate (string reason, string data);

        public delegate void EventDelegate (Event evt);

        /// <summary>
        /// Fires when the connection has opened and the server has returned a success status code
        /// </summary>
        public event OpenDelegate OnOpen;

        /// <summary>
        /// Fires when the <see cref="ReadyState"/> has changed
        /// </summary>
        public event StateChangedDelegate OnStateChanged;

        /// <summary>
        /// Fires when there is an error
        /// </summary>
        public event ErrorDelegate OnError;

        /// <summary>
        /// Fires when an event has been fully recieved from the server
        /// </summary>
        public event EventDelegate OnMessage;

        /// <summary>
        /// Gets the current state of the EventSource
        /// </summary>
        public ReadyState ReadyState {
            get => _state;
            private set {
                _state = value;
                OnStateChanged?.Invoke (value);
            }
        }

        /// <summary>
        /// The URL that this EventSource will consume events from
        /// </summary>
        public string Url { get; }

        /// <summary>
        /// True if CORS is enabled for this EventSource
        /// </summary>
        // public bool WithCredentials { get; }

        public Dictionary<string, string> Headers { get; }

        private int _reconnectTimer;
        private long _lastId;
        private ReadyState _state;
        private readonly CancellationTokenSource _cancelTokenSource;
        private readonly CancellationToken? _cancelToken;
        private Uri _uri;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="uri">The Uri to connect to for events</param>
        public EventSource (Uri uri)
        {
            _uri = uri;
            Url = uri.OriginalString;
            Headers = new Dictionary<string, string> ();
            // WithCredentials = false;
            _lastId = -1;
            _reconnectTimer = 5000;
            _state = ReadyState.Closed;
            _cancelTokenSource = new CancellationTokenSource ();
            _cancelToken = null;
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="uri">The Uri to connect to for events</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> to use for interrupting the connection</param>
        public EventSource (Uri uri, CancellationToken cancellationToken) : this (uri)
        {
            _cancelToken = cancellationToken;
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="url">The URL to connect to for events</param>
        public EventSource (string url) : this (new Uri (url))
        {
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="url">The URL to connect to for events</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> to use for interrupting the connection</param>
        public EventSource (string url, CancellationToken cancellationToken) : this (new Uri (url), cancellationToken)
        {
        }

        /// <summary>
        /// Connects and begins reading from the event stream
        /// </summary>
        public async void Start ()
        {
            var sb = new StringBuilder ();

            using (var client = new HttpClient ()) {
                client.Timeout = Timeout.InfiniteTimeSpan;
                client.DefaultRequestHeaders.Accept.Add (new MediaTypeWithQualityHeaderValue ("text/event-stream"));
                foreach (KeyValuePair<string, string> header in Headers) {
                    client.DefaultRequestHeaders.Add (header.Key, header.Value);
                }

                ReadyState = ReadyState.Connecting;
                CancellationToken cancelToken = _cancelToken ?? _cancelTokenSource.Token;

                while (!cancelToken.IsCancellationRequested) {
                    var request = new HttpRequestMessage (HttpMethod.Get, _uri);

                    if (_lastId >= 0) {
                        if (request.Headers.Contains ("Last-Event-ID")) {
                            request.Headers.Remove ("Last-Event-ID");
                        }
                        request.Headers.Add ("Last-Event-ID", _lastId.ToString ());
                    }

                    try {
                        using (HttpResponseMessage response = await client.SendAsync (request, HttpCompletionOption.ResponseHeadersRead, cancelToken)) {
                            if (!response.IsSuccessStatusCode) {
                                if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden) {
                                    if (response.RequestMessage.RequestUri != _uri) {
                                        // redirected, immediately try again
                                        _uri = response.RequestMessage.RequestUri;
                                        continue;
                                    }
                                }
                                OnError?.Invoke ("Error connecting to server", $"Response code: {(int) response.StatusCode} {response.StatusCode}");
                                break;
                            }
                            using (Stream body = await response.Content.ReadAsStreamAsync ()) {
                                using (var reader = new StreamReader (body)) {
                                    ReadyState = ReadyState.Open;
                                    OnOpen?.Invoke ();
                                    try {
                                        while (!reader.EndOfStream && !cancelToken.IsCancellationRequested) {
                                            string line = reader.ReadLine ();
                                            if (string.IsNullOrEmpty (line) && sb.Length > 0) {
                                                ParseMessage (sb.ToString ());
                                                sb.Clear ();
                                            } else {
                                                sb.Append (line).Append ("\n");
                                            }
                                        }
                                    } catch (IOException e) {
                                        OnError?.Invoke ("Connection failure", e.Message);
                                    }
                                }
                            }
                        }
                    } catch {
                        // ignored
                    }

                    Thread.Sleep (_reconnectTimer);
                }
            }

            ReadyState = ReadyState.Closed;
        }

        /// <summary>
        /// Closes the connection.
        /// 
        /// Note: If a <see cref="CancellationToken"/> was provided when instantiating this EventSource, this method
        /// does nothing.
        /// </summary>
        public void Close ()
        {
            if (_cancelToken == null) {
                _cancelTokenSource.Cancel ();
            }
        }

        private void ParseMessage (string msg)
        {
            var evt = new Event {
                name = "message",
                data = ""
            };

            string[] lines = msg.Trim ().Split ('\n');
            foreach (string line in lines) {
                string[] tokens = line.Split (new[] {':'}, 2);
                if (tokens.Length < 2) {
                    OnError?.Invoke ("Invalid event: line has no field prefix", msg);
                    return;
                }

                string body = tokens[1].Trim ();

                switch (tokens[0]) {
                    case "id":
                        if (!long.TryParse (body, out evt.id)) {
                            OnError?.Invoke ("Invalid event: ID field is not an integer", msg);
                            return;
                        } else {
                            _lastId = evt.id;
                        }
                        break;
                    case "event":
                        evt.name = body;
                        break;
                    case "retry":
                        if (!int.TryParse (body, out _reconnectTimer)) {
                            OnError?.Invoke ("Invalid retry value in event", line);
                        }
                        break;
                    case "data":
                        evt.data += body;
                        break;
                    default:
                        OnError?.Invoke ("Invalid field in event: ", line);
                        break;
                }
            }

            OnMessage?.Invoke (evt);
        }
    }
}