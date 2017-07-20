namespace ArduinoDriver.SerialProtocol
{
    public static class  CommandConstants
    {
        public static readonly byte[] SyncBytes     = { 0xff, 0xfe, 0xfd, 0xfc };
        public static readonly byte[] SyncAckBytes  = { 0xfc, 0xfd, 0xfe, 0xff };

        public const byte StartOfMessage            = 0xFB;
        public const byte AllBytesWritten           = 0xFA;
        public const byte StartOfResponseMarker     = 0xF9;
        public const byte Error                     = 0xEF;

        public const byte HandshakeInitiate         = 0x01;
        public const byte HandshakeAck              = 0x02;

        public const byte Begin = 0x03;
        public const byte BeginAck = 0x04;
        public const byte End = 0x05;
        public const byte EndAck = 0x06;
        public const byte Pen = 0x07;
        public const byte PenAck = 0x08;
        public const byte Move = 0x09;
        public const byte MoveAck = 0x0a;
        public const byte Dot = 0x0b;
        public const byte DotAck = 0x0c;
        public const byte Line = 0x0d;
        public const byte LineAck = 0x0e;
    }
}
