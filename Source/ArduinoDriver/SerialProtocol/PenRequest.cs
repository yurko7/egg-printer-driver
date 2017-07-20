namespace ArduinoDriver.SerialProtocol
{
    public class PenRequest : ArduinoRequest
    {
        public PenRequest(PenState state)
            : base(CommandConstants.Pen)
        {
            Bytes.Add((byte)state);
        }
    }
}
