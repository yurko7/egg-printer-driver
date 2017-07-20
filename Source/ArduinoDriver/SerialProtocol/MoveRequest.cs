using System;

namespace ArduinoDriver.SerialProtocol
{
    public class MoveRequest : ArduinoRequest
    {
        public MoveRequest(params Point[] points)
            : base(CommandConstants.Move)
        {
            foreach (Point point in points)
            {
                Bytes.Add((Byte)(point.X >> 8));
                Bytes.Add((Byte)(point.X & 0xFF));
                Bytes.Add((Byte)(point.Y >> 8));
                Bytes.Add((Byte)(point.Y & 0xFF));
            }
        }
    }
}
