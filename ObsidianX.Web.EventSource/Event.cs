namespace ObsidianX.Web.EventSource
{
    public struct Event
    {
        /// <summary>
        /// Optional ID of the event
        /// </summary>
        public long id;

        /// <summary>
        /// The event type
        /// </summary>
        public string name;

        /// <summary>
        /// The message of the event
        /// </summary>
        public string data;

        public override string ToString ()
        {
            return $"Event: {name}\n" +
                   $"\tID: {id}\n" +
                   $"\tData: {data}";
        }
    }
}