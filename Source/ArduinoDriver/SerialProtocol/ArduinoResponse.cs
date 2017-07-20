using System;
using System.Collections.Generic;
using System.IO;

namespace ArduinoDriver.SerialProtocol
{
    public abstract class ArduinoResponse : ArduinoMessage
    {
        public static ArduinoResponse Create(byte[] bytes)
        {
            var commandByte = bytes[0];
            if (!_factories.ContainsKey(commandByte))
            {
                throw new IOException(string.Format("Unexpected command byte in response: {0}!", commandByte));
            }

            var factory = _factories[commandByte];
            return factory(bytes);
        }

        private static readonly Dictionary<byte, Func<byte[], ArduinoResponse>> _factories = new Dictionary<byte, Func<byte[], ArduinoResponse>>
        {
            { CommandConstants.HandshakeAck, bytes => new HandShakeResponse(bytes[1], bytes[2]) },
            { CommandConstants.BeginAck, bytes => new BeginResponse() },
            { CommandConstants.EndAck, bytes => new EndResponse() },
            { CommandConstants.PenAck, bytes => new PenResponse((PenState)bytes[1]) },
            { CommandConstants.MoveAck, bytes => new MoveResponse(bytes[1]) },
            { CommandConstants.DotAck, bytes => new DotResponse() },
            { CommandConstants.LineAck, bytes => new LineResponse() },
            { CommandConstants.Error, bytes => new ErrorResponse(bytes[1], bytes[2], bytes[3]) },
        };
    }
}
