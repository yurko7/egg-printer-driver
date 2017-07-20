using System;

namespace ArduinoDriver.SerialProtocol
{
    public class DotRequest : ArduinoRequest
    {
        public DotRequest(Point point)
            : base(CommandConstants.Dot)
        {
            Bytes.Add((Byte)(point.X >> 8));
            Bytes.Add((Byte)(point.X & 0xFF));
            Bytes.Add((Byte)(point.Y >> 8));
            Bytes.Add((Byte)(point.Y & 0xFF));
        }
    }
}
