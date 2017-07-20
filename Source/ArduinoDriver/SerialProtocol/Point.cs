using System;

namespace ArduinoDriver.SerialProtocol
{
    public struct Point
    {
        public Point(Int16 x, Int16 y)
        {
            X = x;
            Y = y;
        }

        public Int16 X { get; }

        public Int16 Y { get; }
    }
}
