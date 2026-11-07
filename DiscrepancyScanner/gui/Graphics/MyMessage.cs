using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace DiscrepancyScanner
{
    [DataContract]
    public class MyMessage
    {
        [DataMember]
        MyPoint location;
        [DataMember]
        string message;
        [DataMember]
        MessageType type;

        public MyMessage(MyPoint location, string message, MessageType type)
        {
            this.location = location;
            this.message = message;
            this.type = type;
        }

        public MyPoint Location { get => location; set => location = value; }
        public string Message { get => message; set => message = value; }
        public MessageType Type { get => type; set => type = value; }
    }
}
