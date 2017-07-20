namespace ArduinoDriver.SerialProtocol
{
    public class EndRequest : ArduinoRequest
    {
        public EndRequest()
            : base(CommandConstants.End)
        {
        }
    }
}
