using System;
using System.Runtime.Serialization;

namespace McSync.Exceptions
{
    [Serializable]
    public class JreNotFoundException : Exception
    {
        public JreNotFoundException()
        {
        }

        public JreNotFoundException(string message) : base(message)
        {
        }

        public JreNotFoundException(string message, Exception innerException) : base(message, innerException)
        {
        }
        
        protected JreNotFoundException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}