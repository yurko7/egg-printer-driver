using System;

namespace ArduinoDriver.SerialProtocol
{
    public class LineRequest : ArduinoRequest
    {
        public LineRequest(Point from, Point to)
            : base(CommandConstants.Line)
        {
            Bytes.Add((Byte)(from.X >> 8));
            Bytes.Add((Byte)(from.X & 0xFF));
            Bytes.Add((Byte)(from.Y >> 8));
            Bytes.Add((Byte)(from.Y & 0xFF));
            Bytes.Add((Byte)(to.X >> 8));
            Bytes.Add((Byte)(to.X & 0xFF));
            Bytes.Add((Byte)(to.Y >> 8));
            Bytes.Add((Byte)(to.Y & 0xFF));
        }
    }
}
